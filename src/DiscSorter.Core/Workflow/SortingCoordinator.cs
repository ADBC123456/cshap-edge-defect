using System;
using System.Collections.Generic;
using System.Diagnostics;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Plc;
using DiscSorter.Core.Vision;
using HalconDotNet;

namespace DiscSorter.Core.Workflow
{
    /// <summary>
    /// 分拣协调器：上位机这一侧的握手流程。
    ///
    /// 程序每 50 ms 调用一次 Tick()。每次读取 PLC 状态，和上一次比较，看哪个信号"变了"：
    ///   InspectRequest 0→1：取图 → HALCON 识别 → 确定类别 → 分配库位 → 写结果给 PLC → 保存检测记录
    ///   InspectRequest 1→0：清 ResultValid
    ///   SortDone       0→1：按 PLC 反馈的库位确认入库（不一致则标为待确认）→ 置 DoneAck
    ///   SortDone       1→0：清 DoneAck
    /// 另外检查超时：预留库位后超过 DoneTimeoutMs 还没收到完成信号，就把库位标为"待确认"。
    ///
    /// 为什么不会漏掉信号：每个信号都要等对方回应后才会撤销
    /// （PLC 一直停在第 7 步等结果、MatState 写了就一直保持），所以只要周期地读，就一定能看到它的变化。
    ///
    /// 图像来自 ImageSource：可以是图片文件夹（Images），也可以是海康相机。
    /// </summary>
    public class SortingCoordinator
    {
        private readonly IPlcClient plc;
        private readonly IDefectDetector detector;
        private readonly WarehouseService warehouse;
        private readonly RecordService records;
        private readonly ImageFolder images = new ImageFolder();
        private IImageSource imageSource;

        // 已经分配了库位、正在等 PLC 完成的任务。键是 PLC 任务号
        private readonly Dictionary<long, PendingTask> pendingTasks = new Dictionary<long, PendingTask>();

        // 上一次 Tick 时的 PLC 状态，用来判断信号的变化
        private PlcStatus previous;

        public SortingCoordinator(IPlcClient plc, IDefectDetector detector, WarehouseService warehouse, RecordService records)
        {
            this.plc = plc;
            this.detector = detector;
            this.warehouse = warehouse;
            this.records = records;
            previous = plc.GetStatus();
            imageSource = images;
            CategorySource = CategorySource.Algorithm;
            DoneTimeoutMs = 60000;
        }

        /// <summary>一件工件识别完成、结果已写给 PLC。</summary>
        public event EventHandler<InspectionFinishedEventArgs> InspectionFinished;

        /// <summary>流程提示或报警。</summary>
        public event EventHandler<SortingMessageEventArgs> MessageRaised;

        /// <summary>入库类别来源：算法，或文件名真值（调试用）。</summary>
        public CategorySource CategorySource { get; set; }

        /// <summary>预留库位后等 PLC 完成信号的最长时间（毫秒），超时把库位标为待确认。</summary>
        public int DoneTimeoutMs { get; set; }

        /// <summary>图片文件夹回放（不用相机时的图像来源）。</summary>
        public ImageFolder Images
        {
            get { return images; }
        }

        /// <summary>自动运行时的图像来源。默认是图片文件夹 Images；设为 null 也回到图片文件夹。</summary>
        public IImageSource ImageSource
        {
            get { return imageSource; }
            set
            {
                if (value == null)
                {
                    imageSource = images;
                }
                else
                {
                    imageSource = value;
                }
            }
        }

        /// <summary>
        /// 以上位机数据库为准，把库位状态写给 PLC 的 MatState：空闲写 0，已占用写库位号。
        /// 执行中、待确认的库位不动（执行中由 PLC 放料后自己写，待确认要等人工处理）。
        /// 在仓储页做了出库、确认等人工操作后调用。返回写了几个库位。
        /// </summary>
        public int SyncSlotsToPlc()
        {
            if (!plc.GetStatus().Connected)
            {
                return 0;
            }
            PlcVariables plcVariables = plc.GetVariables();
            int count = 0;
            foreach (StorageSlot slot in warehouse.GetSlots())
            {
                int wanted;
                if (slot.State == SlotState.Empty)
                {
                    wanted = 0;
                }
                else if (slot.State == SlotState.Occupied)
                {
                    wanted = slot.SlotNo;
                }
                else
                {
                    continue;
                }
                if (plcVariables.MatState[slot.SlotNo] != wanted)
                {
                    plc.WriteSlotState(slot.SlotNo, wanted);
                    count++;
                }
            }
            return count;
        }

        /// <summary>周期调用：比较 PLC 信号的变化，做相应的处理。</summary>
        public void Tick(DateTime now)
        {
            PlcStatus current = plc.GetStatus();
            try
            {
                if (current.InspectRequest && !previous.InspectRequest)
                {
                    HandleInspectRequest(current.JobId, now);
                }
                if (!current.InspectRequest && previous.InspectRequest)
                {
                    plc.ClearResultValid();
                }
                if (current.SortDone && !previous.SortDone)
                {
                    HandleSortDone(current.DoneJobId, current.DoneSlot);
                }
                if (!current.SortDone && previous.SortDone)
                {
                    plc.SetDoneAck(false);
                }
                if (current.ErrCode != previous.ErrCode && current.ErrCode != PlcCodes.ErrNone)
                {
                    RaiseMessage("PLC 报警 " + current.ErrCode + "：" + PlcCodes.Describe(current.ErrCode), true);
                }
                CheckTimeouts(now);
            }
            catch (Exception ex)
            {
                RaiseMessage("处理 PLC 信号出错：" + ex.Message, true);
            }
            previous = current;
        }

        /// <summary>PLC 请求检测：取图 → 识别 → 分配库位 → 写结果 → 保存记录。</summary>
        private void HandleInspectRequest(long jobId, DateTime now)
        {
            Stopwatch watch = Stopwatch.StartNew();

            // 1. 取图并识别
            string imagePath = null;
            DetectionResult result = null;
            HImage image = null;
            try
            {
                image = imageSource.Grab(out imagePath);
            }
            catch (Exception ex)
            {
                RaiseMessage("任务 " + jobId + "：取图失败（" + imageSource.SourceName + "）：" + ex.Message + "，按 NG 处理", true);
            }
            if (image != null)
            {
                try
                {
                    result = detector.Detect(image, imagePath);
                }
                finally
                {
                    image.Dispose();
                }
                if (result.HasError)
                {
                    RaiseMessage("任务 " + jobId + "：" + result.Message, true);
                }
            }

            // 2. 确定类别
            string expectedLabel = DefectCategories.GetLabelFromFileName(imagePath);
            string label = DecideLabel(jobId, result, expectedLabel);
            DefectCategory category = DefectCategories.FromAlgorithmLabel(label);

            // 3. 分配库位，马上写给 PLC（PLC 在等结果）
            AllocationResult allocation = warehouse.Reserve(category, jobId);
            plc.WriteResult(jobId, (int)category, allocation.TargetSlot);
            if (allocation.Success)
            {
                PendingTask pending = new PendingTask();
                pending.TaskId = allocation.TaskId;
                pending.SlotNo = allocation.TargetSlot;
                pending.ReservedAt = now;
                pendingTasks[jobId] = pending;
                RaiseMessage("任务 " + jobId + "：" + label + " → " + DefectCategories.GetName(category)
                    + "，目标库位 " + allocation.TargetSlot, false);
            }
            else
            {
                RaiseMessage("任务 " + jobId + "：" + allocation.Alarm, true);
            }
            watch.Stop();

            // 4. 保存检测记录
            InspectionRecord record = BuildRecord(jobId, imagePath, result, label, category, expectedLabel, allocation, watch.Elapsed.TotalMilliseconds);
            records.Add(record);

            // 5. 通知界面
            InspectionFinishedEventArgs args = new InspectionFinishedEventArgs();
            args.JobId = jobId;
            args.Result = result;
            args.Label = label;
            args.Category = category;
            args.Allocation = allocation;
            args.Record = record;
            if (InspectionFinished != null)
            {
                InspectionFinished(this, args);
            }
            else if (result != null)
            {
                result.Dispose();   // 没有人接收，图像就地释放
            }
        }

        /// <summary>按"类别来源"决定这件工件的标签。</summary>
        private string DecideLabel(long jobId, DetectionResult result, string expectedLabel)
        {
            if (CategorySource == CategorySource.FileName)
            {
                if (expectedLabel == null)
                {
                    return "NONE";
                }
                return expectedLabel;
            }

            if (result != null && result.Level == DetectionLevel.Full && result.AlgorithmLabel != null)
            {
                return result.AlgorithmLabel;
            }

            if (result != null && !result.HasError)
            {
                RaiseMessage("任务 " + jobId + "：算法还没有 dd_detect，无法给出类别，按 NG 处理", true);
            }
            return "NONE";
        }

        private InspectionRecord BuildRecord(long jobId, string imagePath, DetectionResult result, string label,
            DefectCategory category, string expectedLabel, AllocationResult allocation, double totalMs)
        {
            InspectionRecord record = new InspectionRecord();
            record.JobId = jobId;
            record.Time = DateTime.Now;
            record.ImagePath = imagePath;
            record.Category = category;
            record.Source = CategorySource;
            record.ExpectedLabel = expectedLabel;
            record.TargetSlot = allocation.TargetSlot;
            if (allocation.Success)
            {
                record.TaskId = allocation.TaskId;
            }
            record.TotalMs = totalMs;

            if (result != null)
            {
                record.AlgorithmLabel = result.AlgorithmLabel;
                record.DetectMs = result.ElapsedMs;
                record.CircleFound = result.Found;
                if (result.Found)
                {
                    record.CircleRow = result.Row;
                    record.CircleColumn = result.Column;
                    record.Radius = result.Radius;
                }
            }

            // 只有"类别来自算法 + 算法完整识别 + 文件名带真值"时，才能判断对错
            if (CategorySource == CategorySource.Algorithm && result != null
                && result.Level == DetectionLevel.Full && expectedLabel != null)
            {
                record.IsCorrect = label == expectedLabel;
            }
            return record;
        }

        /// <summary>PLC 放置完成：确认入库，然后置 DoneAck。</summary>
        private void HandleSortDone(long jobId, int doneSlot)
        {
            if (pendingTasks.ContainsKey(jobId))
            {
                PendingTask pending = pendingTasks[jobId];
                pendingTasks.Remove(jobId);
                bool success = warehouse.Complete(pending.TaskId, doneSlot);
                if (success)
                {
                    RaiseMessage("任务 " + jobId + "：已入库，库位 " + doneSlot, false);
                }
                else
                {
                    RaiseMessage("任务 " + jobId + "：PLC 反馈库位 " + doneSlot + " 与预留库位 " + pending.SlotNo
                        + " 不一致，已标为待确认", true);
                }
            }
            else
            {
                RaiseMessage("任务 " + jobId + "：收到完成信号，但没有对应的执行中任务（可能已超时）", true);
            }
            plc.SetDoneAck(true);
        }

        /// <summary>检查等待完成信号是否超时。</summary>
        private void CheckTimeouts(DateTime now)
        {
            // 先找出超时的任务号，再逐个处理（遍历字典时不能删除其中的元素）
            List<long> expiredJobs = new List<long>();
            foreach (KeyValuePair<long, PendingTask> item in pendingTasks)
            {
                if ((now - item.Value.ReservedAt).TotalMilliseconds >= DoneTimeoutMs)
                {
                    expiredJobs.Add(item.Key);
                }
            }

            foreach (long jobId in expiredJobs)
            {
                PendingTask pending = pendingTasks[jobId];
                pendingTasks.Remove(jobId);
                try
                {
                    warehouse.MarkNeedsCheck(pending.TaskId, "等待 PLC 完成反馈超时（任务 " + jobId + "）");
                    RaiseMessage("任务 " + jobId + "：等待完成反馈超时，库位 " + pending.SlotNo + " 标为待确认", true);
                }
                catch (InvalidOperationException ex)
                {
                    RaiseMessage("任务 " + jobId + "：" + ex.Message, true);
                }
            }
        }

        private void RaiseMessage(string text, bool isAlarm)
        {
            if (MessageRaised != null)
            {
                MessageRaised(this, new SortingMessageEventArgs(text, isAlarm));
            }
        }

        /// <summary>一个等待完成的任务。</summary>
        private class PendingTask
        {
            public long TaskId { get; set; }

            public int SlotNo { get; set; }

            public DateTime ReservedAt { get; set; }
        }
    }
}
