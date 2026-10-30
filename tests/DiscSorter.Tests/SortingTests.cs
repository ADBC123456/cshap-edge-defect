using System;
using System.Collections.Generic;
using System.IO;
using DiscSorter.Core.Common;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Plc;
using DiscSorter.Core.Vision;
using DiscSorter.Core.Workflow;
using HalconDotNet;
using Xunit;

namespace DiscSorter.Tests
{
    /// <summary>
    /// 仿真 PLC + 分拣协调器 + 真实数据库的闭环测试。
    /// 用"假时间"驱动：每一步把时间往前推 50 ms，再依次调用 plc.Tick 和 coordinator.Tick，
    /// 和程序里定时器做的事情完全一样，但不用真的等待，测试几毫秒就跑完。
    /// </summary>
    [Trait("Category", "Halcon")]
    public class SortingTests : IDisposable
    {
        private static readonly string SamplesFolder = Path.Combine(ProjectPaths.FindRoot(AppContext.BaseDirectory), "data", "samples");

        private readonly TestDatabase testDatabase = new TestDatabase();
        private readonly WarehouseService warehouse;
        private readonly RecordService records;
        private readonly SimulatedPlc plc = new SimulatedPlc();
        private readonly List<SortingMessageEventArgs> messages = new List<SortingMessageEventArgs>();
        private readonly List<int> errCodes = new List<int>();
        private SortingCoordinator coordinator;
        private DateTime now = new DateTime(2026, 10, 27, 8, 0, 0);
        private int finishedCount;

        public SortingTests()
        {
            warehouse = testDatabase.CreateWarehouse();
            records = new RecordService(testDatabase.Database);
            plc.ArrivalMs = 100;
            plc.SortMs = 100;
            plc.PhotoDelayMs = 100;
        }

        public void Dispose()
        {
            testDatabase.Dispose();
        }

        [Fact]
        public void FileNameRunFillsColumnsAndAlarmsWhenQColumnIsFull()
        {
            CreateCoordinator(new FakeDetector("NONE", DetectionLevel.Full), CategorySource.FileName);

            plc.Start();
            for (int i = 0; i < 2000 && CountState(SlotState.Occupied) < 9; i++)
            {
                if (plc.GetStatus().ErrCode == PlcCodes.ErrNoFreeSlot)
                {
                    plc.Reset();   // 模拟人工取件后点复位
                }
                Step();
            }
            plc.Stop();

            // data\samples 有 Q×5、H×2、K×2、W×2：Q 列只有 3 格，其余各进各的列
            List<StorageSlot> slots = warehouse.GetSlots();
            Assert.Equal(3, CountCategory(slots, 1, DefectCategory.Q));
            Assert.Equal(2, CountCategory(slots, 2, DefectCategory.H));
            Assert.Equal(2, CountCategory(slots, 3, DefectCategory.K));
            Assert.Equal(2, CountCategory(slots, 4, DefectCategory.W));
            Assert.Equal(11, finishedCount);

            int qWithoutSlot = 0;
            foreach (InspectionRecord record in records.GetRecent(20))
            {
                Assert.Equal(CategorySource.FileName, record.Source);
                if (record.TargetSlot == 0 && record.Category == DefectCategory.Q)
                {
                    qWithoutSlot++;
                }
            }
            Assert.Equal(2, qWithoutSlot);
            Assert.Contains(PlcCodes.ErrNoFreeSlot, errCodes);
            Assert.True(HasMessage("已满", true));
        }

        [Fact]
        public void AlgorithmCategoryIsUsedAndJudgedAgainstFileName()
        {
            CreateCoordinator(new FakeDetector("K", DetectionLevel.Full), CategorySource.Algorithm);

            plc.RunSingle();
            RunUntilStopped();

            InspectionRecord record = records.GetRecent(10)[0];
            Assert.Equal("H_1.jpg", record.FileName);
            Assert.Equal("K", record.AlgorithmLabel);
            Assert.Equal(DefectCategory.K, record.Category);
            Assert.Equal("H", record.ExpectedLabel);
            Assert.Equal(false, record.IsCorrect);
            Assert.Equal(7, record.TargetSlot);
            Assert.Equal((int)DefectCategory.K, plc.LastCategory);
            Assert.Equal(SlotState.Occupied, warehouse.GetSlots()[6].State);

            InspectionStats stats = records.GetStats(DateTime.Today.AddDays(-1));
            Assert.Equal(1, stats.Total);
            Assert.Equal(1, stats.Judged);
            Assert.Equal(0, stats.Correct);
        }

        [Fact]
        public void IncompleteAlgorithmSendsPartToNgForManualPickup()
        {
            CreateCoordinator(new FakeDetector(null, DetectionLevel.Segment), CategorySource.Algorithm);

            plc.RunSingle();
            for (int i = 0; i < 100 && plc.GetStatus().ErrCode == PlcCodes.ErrNone; i++)
            {
                Step();
            }

            // 默认映射里 NG 没有库区：下发库位 0，PLC 报警 5 等人工取件
            Assert.Equal(PlcCodes.ErrNoFreeSlot, plc.GetStatus().ErrCode);
            InspectionRecord record = records.GetRecent(10)[0];
            Assert.Equal(DefectCategory.NG, record.Category);
            Assert.Equal(0, record.TargetSlot);
            Assert.Equal((int)DefectCategory.NG, plc.LastCategory);
            Assert.True(HasMessage("dd_detect", true));
            Assert.True(HasMessage("没有分配库区", true));
        }

        [Fact]
        public void MissingDoneSignalTimesOutToNeedsCheck()
        {
            plc.Fault = SimulatedFault.NoSortDone;
            CreateCoordinator(new FakeDetector("Q", DetectionLevel.Full), CategorySource.Algorithm);
            coordinator.DoneTimeoutMs = 1000;

            plc.RunSingle();
            for (int i = 0; i < 100; i++)
            {
                Step();
            }

            Assert.Equal(SlotState.NeedsCheck, warehouse.GetSlots()[0].State);
            Assert.Equal(StorageTaskStatus.NeedsCheck, warehouse.GetRecentTasks(1)[0].Status);
            Assert.Equal(20, plc.GetStatus().Step);   // 卡在"去库位"
            Assert.True(HasMessage("等待完成反馈超时", true));
        }

        [Fact]
        public void WrongDoneSlotMarksNeedsCheck()
        {
            plc.Fault = SimulatedFault.WrongDoneSlot;
            CreateCoordinator(new FakeDetector("Q", DetectionLevel.Full), CategorySource.Algorithm);

            plc.RunSingle();
            RunUntilStopped();

            Assert.Equal(SlotState.NeedsCheck, warehouse.GetSlots()[0].State);
            Assert.True(HasMessage("不一致", true));
        }

        [Fact]
        public void PlcRetriesWhenResultNeverArrives()
        {
            // 不创建协调器，没有人回应检测请求：PLC 等 0.5 秒后回到第 1 步，工件还在，又请求一次
            plc.ResultTimeoutMs = 500;
            plc.RunSingle();
            for (int i = 0; i < 60; i++)
            {
                now = now.AddMilliseconds(50);
                plc.Tick(now);
            }

            Assert.True(plc.GetStatus().JobId >= 2);
            Assert.Equal(PlcCodes.ErrNone, plc.GetStatus().ErrCode);
            Assert.True(plc.GetStatus().PartPresent);
        }

        [Fact]
        public void ResultIsWrittenSlotFirstAndCategoryLast()
        {
            CreateCoordinator(new FakeDetector("H", DetectionLevel.Full), CategorySource.Algorithm);

            plc.RunSingle();
            RunUntilStopped();

            // 通讯日志里：先写 PC_TargetSlot，再写 PC_Category；PLC 放料后 MatState[4] 变成 4
            List<string> writes = new List<string>();
            bool placed = false;
            foreach (PlcLogEntry entry in plc.GetLog())
            {
                if (entry.Direction == PlcLogEntry.ToPlc)
                {
                    writes.Add(entry.Variable + "=" + entry.Value);
                }
                if (entry.Variable == "MatState[4]" && entry.Value == "0→4")
                {
                    placed = true;
                }
            }
            Assert.Equal(new List<string> { "PC_TargetSlot=4", "PC_Category=2" }, writes);
            Assert.True(placed);
            Assert.Equal(4, plc.GetVariables().MatState[4]);
            Assert.Equal(SlotState.Occupied, warehouse.GetSlots()[3].State);
        }

        [Fact]
        public void ClearedSlotIsSyncedToPlc()
        {
            CreateCoordinator(new FakeDetector("Q", DetectionLevel.Full), CategorySource.Algorithm);
            plc.RunSingle();
            RunUntilStopped();
            Assert.Equal(1, plc.GetVariables().MatState[1]);

            // 人工出库后，以数据库为准同步给 PLC
            warehouse.ClearSlot(1);
            int written = coordinator.SyncSlotsToPlc();
            Step();

            Assert.Equal(1, written);
            Assert.Equal(0, plc.GetVariables().MatState[1]);
        }

        private void CreateCoordinator(IDefectDetector detector, CategorySource source)
        {
            coordinator = new SortingCoordinator(plc, detector, warehouse, records);
            coordinator.CategorySource = source;
            coordinator.Images.OpenFolder(SamplesFolder);
            coordinator.MessageRaised += Coordinator_MessageRaised;
            coordinator.InspectionFinished += Coordinator_InspectionFinished;
        }

        private void Coordinator_MessageRaised(object sender, SortingMessageEventArgs e)
        {
            messages.Add(e);
        }

        private void Coordinator_InspectionFinished(object sender, InspectionFinishedEventArgs e)
        {
            finishedCount++;
            if (e.Result != null)
            {
                e.Result.Dispose();
            }
        }

        /// <summary>时间前进 50 ms，PLC 和协调器各走一步（和程序里的定时器一样）。</summary>
        private void Step()
        {
            now = now.AddMilliseconds(50);
            plc.Tick(now);
            coordinator.Tick(now);
            errCodes.Add(plc.GetStatus().ErrCode);
        }

        private void RunUntilStopped()
        {
            Step();
            for (int i = 0; i < 500 && plc.GetStatus().AutoRunning; i++)
            {
                Step();
            }
            Assert.False(plc.GetStatus().AutoRunning);
        }

        private int CountState(SlotState state)
        {
            int count = 0;
            foreach (StorageSlot slot in warehouse.GetSlots())
            {
                if (slot.State == state)
                {
                    count++;
                }
            }
            return count;
        }

        private static int CountCategory(List<StorageSlot> slots, int column, DefectCategory category)
        {
            int count = 0;
            foreach (StorageSlot slot in slots)
            {
                if (slot.Column == column && slot.Category == category)
                {
                    count++;
                }
            }
            return count;
        }

        private bool HasMessage(string text, bool isAlarm)
        {
            foreach (SortingMessageEventArgs message in messages)
            {
                if (message.IsAlarm == isAlarm && message.Text.Contains(text))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>假的识别器：总是给出指定的标签和识别程度。</summary>
        private class FakeDetector : IDefectDetector
        {
            private readonly string label;
            private readonly DetectionLevel level;

            public FakeDetector(string label, DetectionLevel level)
            {
                this.label = label;
                this.level = level;
            }

            public DetectionResult Detect(HImage image, string imagePath)
            {
                DetectionResult result = new DetectionResult();
                result.Image = image.CopyImage();
                result.ImagePath = imagePath;
                result.Level = level;
                result.Found = level != DetectionLevel.None;
                if (level == DetectionLevel.Full)
                {
                    result.AlgorithmLabel = label;
                }
                return result;
            }
        }
    }
}
