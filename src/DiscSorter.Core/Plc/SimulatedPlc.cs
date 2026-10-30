using System;
using System.Collections.Generic;

namespace DiscSorter.Core.Plc
{
    /// <summary>
    /// 仿真 PLC：按修改后的 CHAIN.st 的步号模拟自动流程，没有硬件也能跑通"取图—识别—分配—入库"闭环。
    /// 它和真实 PLC 交换的是同样的 5 个变量（见 PlcVariables），所以"PLC 通讯"页看到的交互和真机一样。
    ///
    /// 程序每 50 ms 调用一次 Tick()，每次只检查"当前这一步的条件满足了没有"，满足就进入下一步，
    /// 和 PLC 的扫描周期、CASE 步序程序是一个思路。一件工件的流程（括号里是步号）：
    ///   信号复位(1)     清 PC_Category、PC_TargetSlot
    ///   等放工件(2)     过了"来件间隔"→ 工位上有工件，Rsc_xExecute=1
    ///   拍照(3)→延时(6) 等工件稳定
    ///   等结果(7)       PC_Category 不为 0 → 下一步；超过 5 秒没结果 → 回到第 1 步重来（工件还在，会再拍一次）
    ///   校验库位(8)     PC_TargetSlot 在 1~12 且该库位空 → 搬运；否则 → 报警(40)
    ///   取料(10~16)     工件被吸走，Rsc_xExecute=0
    ///   去库位(20~23)   放料(24) 写 MatState[库位] = 库位号，回原位(26)，一件完成(100)
    ///   报警(40)        等人工取件后点"复位"
    /// </summary>
    public class SimulatedPlc : StationPlcBase
    {
        // 仿真 PLC 的"内存"，相当于真 PLC 里的变量
        private readonly PlcVariables memory = new PlcVariables();

        // 进入当前这一步的时间。MinValue 表示"下一次 Tick 时再记时间"
        private DateTime stepStartTime = DateTime.MinValue;

        private bool running;
        private bool stopRequested;
        private bool singleCycle;
        private int pos;

        public SimulatedPlc()
        {
            ArrivalMs = 1500;
            SortMs = 1500;
            PhotoDelayMs = 800;
            ResultTimeoutMs = 5000;
            Fault = SimulatedFault.None;
        }

        public override string Name
        {
            get { return "仿真 PLC"; }
        }

        public override bool IsSimulated
        {
            get { return true; }
        }

        /// <summary>两件工件之间的间隔（毫秒）。</summary>
        public int ArrivalMs { get; set; }

        /// <summary>取件、搬运、放置的总耗时（毫秒）。</summary>
        public int SortMs { get; set; }

        /// <summary>拍照延时（毫秒），PLC 程序里是 Photo_Delay 0.8 秒。</summary>
        public int PhotoDelayMs { get; set; }

        /// <summary>等识别结果的最长时间（毫秒），PLC 程序里是 T1 5 秒，超时回第 1 步重来。</summary>
        public int ResultTimeoutMs { get; set; }

        /// <summary>故障注入。</summary>
        public SimulatedFault Fault { get; set; }

        protected override bool IsRunning
        {
            get { return running; }
        }

        public override void Start()
        {
            BeginRun(false);
        }

        public override void RunSingle()
        {
            BeginRun(true);
        }

        public override void Stop()
        {
            stopRequested = true;
        }

        /// <summary>
        /// 真 PLC 的 MatState 是掉电保持（RETAIN）的，重启后还在。
        /// 仿真 PLC 在程序启动时用这个方法，按数据库里"已占用"的库位初始化 MatState，效果相同。
        /// </summary>
        public void LoadRetainedSlots(List<int> occupiedSlots)
        {
            foreach (int slot in occupiedSlots)
            {
                if (slot >= 1 && slot <= PlcVariables.SlotCount)
                {
                    memory.MatState[slot] = slot;
                }
            }
        }

        /// <summary>复位：清上位机写的结果；如果在报警，模拟"人工把工件取走，再按复位按钮"。</summary>
        public override void Reset()
        {
            base.Reset();
            if (memory.Step == PlcTags.StepAlarm)
            {
                memory.CaptureRequest = false;   // 人工取走了工件
                GoToStep(1);
            }
        }

        // ==================== 读写变量 ====================

        protected override bool ReadVariables(DateTime now, PlcVariables target)
        {
            RunStep(now);
            target.CaptureRequest = memory.CaptureRequest;
            target.Step = memory.Step;
            target.Category = memory.Category;
            target.TargetSlot = memory.TargetSlot;
            for (int slot = 0; slot <= PlcVariables.SlotCount; slot++)
            {
                target.MatState[slot] = memory.MatState[slot];
            }
            return true;
        }

        protected override bool WriteInt(string path, int value)
        {
            if (path == PlcTags.Category)
            {
                memory.Category = value;
            }
            else if (path == PlcTags.TargetSlot)
            {
                memory.TargetSlot = value;
            }
            return true;
        }

        protected override bool WriteMatState(int slotNo, int value)
        {
            memory.MatState[slotNo] = value;
            return true;
        }

        // ==================== 步序 ====================

        /// <summary>周期调用：检查当前这一步的条件，满足就进入下一步。</summary>
        private void RunStep(DateTime now)
        {
            if (stepStartTime == DateTime.MinValue)
            {
                stepStartTime = now;
            }
            double elapsedMs = (now - stepStartTime).TotalMilliseconds;

            switch (memory.Step)
            {
                case 0:
                    // 停止状态，等"启动"
                    break;

                case 1:
                    // 信号复位（修改后的程序在这里清上位机写的结果）
                    memory.Category = 0;
                    memory.TargetSlot = 0;
                    pos = 0;
                    GoToStep(2);
                    break;

                case 2:
                    // 等工件放到检测工位
                    if (memory.CaptureRequest)
                    {
                        GoToStep(3);   // 工件还在（例如等结果超时后重来），直接拍照
                    }
                    else if (stopRequested)
                    {
                        StopRunning();
                    }
                    else if (elapsedMs >= ArrivalMs)
                    {
                        memory.CaptureRequest = true;
                        GoToStep(3);
                    }
                    break;

                case 3:
                    GoToStep(6);
                    break;

                case 6:
                    if (elapsedMs >= PhotoDelayMs)
                    {
                        GoToStep(PlcTags.StepWaitResult);
                    }
                    break;

                case 7:
                    // 等上位机写类别码
                    if (memory.Category > 0)
                    {
                        GoToStep(8);
                    }
                    else if (elapsedMs >= ResultTimeoutMs)
                    {
                        GoToStep(1);   // 无法识别：回到第 1 步重来
                    }
                    break;

                case 8:
                    // 校验上位机给的库位
                    int slot = memory.TargetSlot;
                    if (slot >= 1 && slot <= PlcVariables.SlotCount && memory.MatState[slot] != slot)
                    {
                        pos = slot;
                        GoToStep(10);
                    }
                    else
                    {
                        GoToStep(PlcTags.StepAlarm);
                    }
                    break;

                case 10:
                    // 去取料位、吸取
                    if (elapsedMs >= SortMs * 0.3)
                    {
                        GoToStep(14);
                    }
                    break;

                case 14:
                    if (elapsedMs >= SortMs * 0.1)
                    {
                        memory.CaptureRequest = false;   // 工件被吸走
                        GoToStep(20);
                    }
                    break;

                case 20:
                    // 去库位。故障注入"伺服卡住"时停在这一步
                    if (Fault != SimulatedFault.NoSortDone && elapsedMs >= SortMs * 0.4)
                    {
                        GoToStep(PlcTags.StepPlace);
                    }
                    break;

                case 24:
                    // 放料，写 MatState。故障注入"放错库位"时放到下一个库位
                    int placeSlot = pos;
                    if (Fault == SimulatedFault.WrongDoneSlot)
                    {
                        placeSlot = pos % PlcVariables.SlotCount + 1;
                    }
                    memory.MatState[placeSlot] = placeSlot;
                    if (elapsedMs >= SortMs * 0.1)
                    {
                        GoToStep(26);
                    }
                    break;

                case 26:
                    if (elapsedMs >= SortMs * 0.1)
                    {
                        GoToStep(100);
                    }
                    break;

                case 40:
                    // 报警：等"复位"（见 Reset）
                    break;

                case 100:
                    if (singleCycle || stopRequested)
                    {
                        StopRunning();
                    }
                    else
                    {
                        GoToStep(1);
                    }
                    break;
            }
        }

        private void BeginRun(bool single)
        {
            if (running)
            {
                return;
            }
            running = true;
            stopRequested = false;
            singleCycle = single;
            GoToStep(1);
        }

        private void StopRunning()
        {
            running = false;
            GoToStep(0);
        }

        private void GoToStep(int step)
        {
            memory.Step = step;
            stepStartTime = DateTime.MinValue;   // 下一次 Tick 时开始计时
        }
    }
}
