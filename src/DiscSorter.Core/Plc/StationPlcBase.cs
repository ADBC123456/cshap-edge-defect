using System;
using System.Collections.Generic;

namespace DiscSorter.Core.Plc
{
    /// <summary>
    /// 仿真 PLC 和真实 PLC 共用的部分：把 PLC 的 5 个变量换算成分拣协调器使用的握手信号，并记录通讯日志。
    ///
    /// 子类只需要实现"怎么读变量、怎么写变量"：
    ///   SimulatedPlc 在内存里模拟 PLC 程序；OpcUaPlc 通过 OPC UA 读写真实 PLC。
    ///
    /// 换算规则（对应修改后的 CHAIN.st）：
    ///   请求检测 InspectRequest = Rsc_xExecute 为 1，且 tSTEP = 7（等待识别结果），且 PC_Category = 0（还没给结果）
    ///   任务号   JobId          = 上位机自己计数，InspectRequest 每出现一次加 1（旧 PLC 程序没有任务号）
    ///   写结果   WriteResult    = 先写 PC_TargetSlot，最后写 PC_Category
    ///   放置完成 SortDone       = 写结果以后，MatState[i] 由别的值变成 i（PLC 第 24 步写的），DoneSlot = i
    ///   报警码   ErrCode        = tSTEP = 40 时：下发的库位是 0 → 5（无可用库位），否则 → 4（库位非法或已占用）
    /// </summary>
    public abstract class StationPlcBase : IPlcClient
    {
        /// <summary>日志最多保留的条数，超过就删掉最早的。</summary>
        private const int MaxLogCount = 2000;

        private readonly PlcStatus status = new PlcStatus();
        private readonly List<PlcLogEntry> log = new List<PlcLogEntry>();

        // 最近一次读到的 PLC 变量
        private PlcVariables variables = new PlcVariables();
        private bool hasReadOnce;

        // 上位机自己数的任务号
        private long jobCounter;
        private bool previousInspect;

        // 上位机最近一次写给 PLC 的值（区分"PLC 自己改的"和"上位机写的"）
        private int writtenCategory;
        private int writtenSlot;

        // 等待放置完成的任务
        private bool hasPending;
        private long pendingJobId;
        private int pendingSlot;
        private int[] matStateAtWrite = new int[PlcVariables.SlotCount + 1];
        private bool sortDone;
        private int doneSlot;
        private long doneJobId;

        protected StationPlcBase()
        {
            Online = true;
        }

        public abstract string Name { get; }

        public abstract bool IsSimulated { get; }

        /// <summary>上位机是否响应拍照请求（真实 PLC 用"启动/停止"按钮切换）。</summary>
        protected bool Online { get; set; }

        /// <summary>上位机最近一次写的类别码（测试和界面用）。</summary>
        public int LastCategory
        {
            get { return writtenCategory; }
        }

        public event EventHandler<PlcLogEntry> LogAdded;

        // ==================== 子类实现 ====================

        /// <summary>读取 PLC 的 5 个变量，写进 target。读失败返回 false。</summary>
        protected abstract bool ReadVariables(DateTime now, PlcVariables target);

        /// <summary>写一个 INT 变量（path 是 PlcTags 里的路径）。失败返回 false。</summary>
        protected abstract bool WriteInt(string path, int value);

        /// <summary>写 MatState[slotNo]。失败返回 false。</summary>
        protected abstract bool WriteMatState(int slotNo, int value);

        /// <summary>是否在自动运行（显示用）。</summary>
        protected abstract bool IsRunning { get; }

        public abstract void Start();

        public abstract void Stop();

        public abstract void RunSingle();

        // ==================== 周期处理 ====================

        public void Tick(DateTime now)
        {
            PlcVariables current = variables.Clone();
            bool ok = ReadVariables(now, current);
            if (status.Connected != ok)
            {
                AddLog(PlcLogEntry.Host, "连接", ok ? "已连接" : "断开", ok ? "开始周期读取 PLC 变量" : "读取 PLC 变量失败");
            }
            status.Connected = ok;
            if (!ok)
            {
                status.InspectRequest = false;
                status.AutoRunning = false;
                return;
            }

            if (hasReadOnce)
            {
                LogChanges(variables, current);
            }
            hasReadOnce = true;
            variables = current;
            UpdateStatus();
            status.Heartbeat = (status.Heartbeat + 1) % 32768;
        }

        /// <summary>把 PLC 变量换算成握手信号。</summary>
        private void UpdateStatus()
        {
            status.Step = variables.Step;
            status.PartPresent = variables.CaptureRequest;
            status.AutoRunning = IsRunning;
            status.ResultValid = variables.Category != 0;

            // 请求检测：上升沿时任务号加 1
            bool inspect = Online && variables.CaptureRequest
                && variables.Step == PlcTags.StepWaitResult && variables.Category == 0;
            if (inspect && !previousInspect)
            {
                jobCounter++;
                status.JobId = jobCounter;
                AddLog(PlcLogEntry.Host, "请求检测", "任务 " + jobCounter,
                    "Rsc_xExecute=1 且 tSTEP=7 且 PC_Category=0：PLC 在等结果，开始拍照识别");
            }
            status.InspectRequest = inspect;
            previousInspect = inspect;

            // 放置完成：写结果以后，哪个库位的 MatState 新变成了自己的库位号
            if (hasPending && !sortDone)
            {
                int placed = FindNewlyPlacedSlot();
                if (placed > 0)
                {
                    sortDone = true;
                    doneSlot = placed;
                    doneJobId = pendingJobId;
                    AddLog(PlcLogEntry.Host, "放置完成", "库位 " + placed,
                        "MatState[" + placed + "] 变成 " + placed + "，任务 " + pendingJobId + " 已放入库位");
                }
            }
            status.SortDone = sortDone;
            status.DoneSlot = doneSlot;
            status.DoneJobId = doneJobId;
            status.SortBusy = hasPending && !sortDone && variables.Step >= 9 && variables.Step <= 26;

            // 报警码
            if (variables.Step == PlcTags.StepAlarm)
            {
                if (writtenSlot == 0)
                {
                    status.ErrCode = PlcCodes.ErrNoFreeSlot;
                }
                else
                {
                    status.ErrCode = PlcCodes.ErrInvalidSlot;
                }
            }
            else
            {
                status.ErrCode = PlcCodes.ErrNone;
            }
        }

        /// <summary>找写结果以后新放入的库位：优先看目标库位，再看其他库位（放错位置的情况）。</summary>
        private int FindNewlyPlacedSlot()
        {
            if (IsNewlyPlaced(pendingSlot))
            {
                return pendingSlot;
            }
            for (int slot = 1; slot <= PlcVariables.SlotCount; slot++)
            {
                if (IsNewlyPlaced(slot))
                {
                    return slot;
                }
            }
            return 0;
        }

        private bool IsNewlyPlaced(int slot)
        {
            return variables.MatState[slot] == slot && matStateAtWrite[slot] != slot;
        }

        /// <summary>比较前后两次读到的变量，把变化写进日志。</summary>
        private void LogChanges(PlcVariables before, PlcVariables after)
        {
            if (before.CaptureRequest != after.CaptureRequest)
            {
                string note = after.CaptureRequest ? "工位上有工件（E_Detection=1）" : "工件已被取走";
                AddLog(PlcLogEntry.FromPlc, "Rsc_xExecute", BoolText(before.CaptureRequest) + "→" + BoolText(after.CaptureRequest), note);
            }
            if (before.Step != after.Step)
            {
                AddLog(PlcLogEntry.FromPlc, "tSTEP", before.Step + "→" + after.Step, PlcTags.DescribeStep(after.Step));
            }
            // 上位机写的两个变量：只有 PLC 自己把它改掉时才记（上位机写的时候已经记过了）
            if (before.Category != after.Category && after.Category != writtenCategory)
            {
                AddLog(PlcLogEntry.FromPlc, "PC_Category", before.Category + "→" + after.Category, "PLC 清除了类别码（第 1 步）");
                writtenCategory = after.Category;
            }
            if (before.TargetSlot != after.TargetSlot && after.TargetSlot != writtenSlot)
            {
                AddLog(PlcLogEntry.FromPlc, "PC_TargetSlot", before.TargetSlot + "→" + after.TargetSlot, "PLC 清除了目标库位（第 1 步）");
            }
            for (int slot = 1; slot <= PlcVariables.SlotCount; slot++)
            {
                if (before.MatState[slot] != after.MatState[slot])
                {
                    string note;
                    if (after.MatState[slot] == slot)
                    {
                        note = "库位 " + slot + " 放入工件";
                    }
                    else if (after.MatState[slot] == 0)
                    {
                        note = "库位 " + slot + " 清空";
                    }
                    else
                    {
                        note = "库位 " + slot + " 的值异常";
                    }
                    AddLog(PlcLogEntry.FromPlc, "MatState[" + slot + "]", before.MatState[slot] + "→" + after.MatState[slot], note);
                }
            }
        }

        // ==================== 上位机的操作 ====================

        public PlcStatus GetStatus()
        {
            return status.Clone();
        }

        public PlcVariables GetVariables()
        {
            return variables.Clone();
        }

        public List<PlcLogEntry> GetLog()
        {
            return new List<PlcLogEntry>(log);
        }

        public void ClearLog()
        {
            log.Clear();
        }

        public void WriteResult(long jobId, int category, int targetSlot)
        {
            // 记下写之前各库位的状态，用来判断之后哪个库位是"新放入的"
            matStateAtWrite = (int[])variables.MatState.Clone();

            // 先写库位，最后写类别码：PLC 看到类别码不为 0 时，库位一定已经写好了
            bool slotOk = WriteInt(PlcTags.TargetSlot, targetSlot);
            AddLog(PlcLogEntry.ToPlc, "PC_TargetSlot", targetSlot.ToString(),
                targetSlot == 0 ? "没有可用库位，PLC 将报警等人工取件" : "任务 " + jobId + " 的目标库位" + WriteFailedText(slotOk));
            bool categoryOk = WriteInt(PlcTags.Category, category);
            AddLog(PlcLogEntry.ToPlc, "PC_Category", category.ToString(),
                "类别码（最后写，相当于\"结果有效\"）" + WriteFailedText(categoryOk));

            writtenSlot = targetSlot;
            writtenCategory = category;
            hasPending = targetSlot > 0;
            pendingJobId = jobId;
            pendingSlot = targetSlot;
            sortDone = false;
        }

        public void ClearResultValid()
        {
            // 修改后的 PLC 程序在第 1 步自己把 PC_Category、PC_TargetSlot 清 0，这里不用写
        }

        public void SetDoneAck(bool value)
        {
            if (value)
            {
                AddLog(PlcLogEntry.Host, "确认入库", "库位 " + doneSlot, "已写入数据库");
                hasPending = false;
                sortDone = false;
            }
        }

        public virtual void Reset()
        {
            WriteInt(PlcTags.TargetSlot, 0);
            WriteInt(PlcTags.Category, 0);
            writtenSlot = 0;
            writtenCategory = 0;
            hasPending = false;
            sortDone = false;
            AddLog(PlcLogEntry.ToPlc, "PC_Category / PC_TargetSlot", "0", "复位：清除上位机写的结果");
        }

        public void WriteSlotState(int slotNo, int value)
        {
            if (slotNo < 1 || slotNo > PlcVariables.SlotCount)
            {
                return;
            }
            bool ok = WriteMatState(slotNo, value);
            string note = value == 0 ? "人工出库，同步清空 PLC 的库位状态" : "人工确认已入库，同步给 PLC";
            AddLog(PlcLogEntry.ToPlc, "MatState[" + slotNo + "]", value.ToString(), note + WriteFailedText(ok));
        }

        /// <summary>子类和本类都用它记日志。</summary>
        protected void AddLog(string direction, string variable, string value, string note)
        {
            PlcLogEntry entry = new PlcLogEntry();
            entry.Time = DateTime.Now;
            entry.Direction = direction;
            entry.Variable = variable;
            entry.Value = value;
            entry.Note = note;
            log.Add(entry);
            if (log.Count > MaxLogCount)
            {
                log.RemoveAt(0);
            }
            if (LogAdded != null)
            {
                LogAdded(this, entry);
            }
        }

        private static string BoolText(bool value)
        {
            return value ? "1" : "0";
        }

        private static string WriteFailedText(bool ok)
        {
            return ok ? "" : "（写入失败！）";
        }
    }
}
