namespace DiscSorter.Core.Models
{
    /// <summary>
    /// 库位分配结果。
    /// 分配成功时 TargetSlot 是 1～12；分配不到（该类没有库区或库区已满）时 TargetSlot 为 0，Alarm 是报警文字。
    /// </summary>
    public class AllocationResult
    {
        public DefectCategory Category { get; set; }

        /// <summary>新建的入库任务号，分配失败时为 0。</summary>
        public long TaskId { get; set; }

        /// <summary>目标库位，0 表示没有可用库位，需要人工取件。</summary>
        public int TargetSlot { get; set; }

        /// <summary>报警文字，分配成功时为 null。</summary>
        public string Alarm { get; set; }

        public bool Success
        {
            get { return TargetSlot > 0; }
        }
    }
}
