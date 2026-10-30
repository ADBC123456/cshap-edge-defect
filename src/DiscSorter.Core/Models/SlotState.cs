namespace DiscSorter.Core.Models
{
    /// <summary>库位状态。</summary>
    public enum SlotState
    {
        /// <summary>空闲</summary>
        Empty,

        /// <summary>执行中：已经分配给某个任务，等 PLC 放好工件</summary>
        Reserved,

        /// <summary>已占用：工件已放入</summary>
        Occupied,

        /// <summary>待确认：不能确定工件是否放入（超时、反馈不一致、程序重启），需要人工确认</summary>
        NeedsCheck
    }
}
