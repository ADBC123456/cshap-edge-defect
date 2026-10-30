using System;
using System.Collections.Generic;

namespace DiscSorter.Core.Plc
{
    /// <summary>
    /// 上位机和 PLC 通信的接口。有两个实现：SimulatedPlc（仿真）和 OpcUaPlc（真实 PLC），
    /// 它们共用 StationPlcBase 里的握手逻辑，分拣流程和界面不关心用的是哪一个。
    /// </summary>
    public interface IPlcClient
    {
        /// <summary>显示用的名称，例如"仿真 PLC"。</summary>
        string Name { get; }

        /// <summary>是否是仿真 PLC。</summary>
        bool IsSimulated { get; }

        /// <summary>周期调用（程序里每 50 ms 一次）：读取 PLC 变量，换算成握手信号。</summary>
        void Tick(DateTime now);

        /// <summary>取当前握手状态的副本（分拣协调器用）。</summary>
        PlcStatus GetStatus();

        /// <summary>取最近一次读到的 PLC 变量的副本（"PLC 通讯"页用）。</summary>
        PlcVariables GetVariables();

        /// <summary>取通讯日志的副本（最早的在前）。</summary>
        List<PlcLogEntry> GetLog();

        /// <summary>清空通讯日志。</summary>
        void ClearLog();

        /// <summary>新增一条通讯日志时触发。</summary>
        event EventHandler<PlcLogEntry> LogAdded;

        /// <summary>仿真 PLC：开始自动运行。真实 PLC：上位机联机，开始响应拍照请求。</summary>
        void Start();

        /// <summary>仿真 PLC：做完当前这一件再停。真实 PLC：上位机脱机，不再响应拍照请求。</summary>
        void Stop();

        /// <summary>只运行一件就停（单件调试）。</summary>
        void RunSingle();

        /// <summary>复位：清除上位机写给 PLC 的结果。仿真 PLC 还会模拟"人工取件 + 按复位按钮"。</summary>
        void Reset();

        /// <summary>写识别结果：先写目标库位，最后写类别码（类别码不为 0 就表示结果有效）。</summary>
        void WriteResult(long jobId, int category, int targetSlot);

        /// <summary>检测请求撤销后调用。现在的 PLC 程序在第 1 步自己清结果，所以这里不用写。</summary>
        void ClearResultValid();

        /// <summary>上位机确认入库（true）。</summary>
        void SetDoneAck(bool value);

        /// <summary>写库位状态 MatState[slotNo]：人工出库时写 0，人工确认已入库时写库位号。</summary>
        void WriteSlotState(int slotNo, int value);
    }
}
