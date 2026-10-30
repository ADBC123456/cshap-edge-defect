namespace DiscSorter.Core.Plc
{
    /// <summary>仿真 PLC 的故障注入，用来演示异常处理。</summary>
    public enum SimulatedFault
    {
        /// <summary>正常运行。</summary>
        None,

        /// <summary>搬运途中伺服卡住，一直不放料（MatState 不变），上位机应超时并把库位标为待确认。</summary>
        NoSortDone,

        /// <summary>放到了下一个库位（MatState 变化的库位和下发的不一致），上位机应把库位标为待确认。</summary>
        WrongDoneSlot
    }
}
