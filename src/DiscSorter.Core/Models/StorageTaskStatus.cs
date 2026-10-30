namespace DiscSorter.Core.Models
{
    /// <summary>入库任务的状态。</summary>
    public enum StorageTaskStatus
    {
        /// <summary>执行中</summary>
        Reserved,

        /// <summary>已入库</summary>
        Completed,

        /// <summary>失败（确定没有放入）</summary>
        Failed,

        /// <summary>已取消（人工确认没有放入）</summary>
        Cancelled,

        /// <summary>待确认</summary>
        NeedsCheck
    }
}
