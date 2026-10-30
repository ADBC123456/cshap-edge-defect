using System;

namespace DiscSorter.Core.Models
{
    /// <summary>一条操作日志（对应数据库 OperationLogs 表的一行）。</summary>
    public class OperationLog
    {
        public long Id { get; set; }

        public DateTime Time { get; set; }

        /// <summary>事件类型，例如 Reserved、Completed、Full、Cleared、MappingChanged。</summary>
        public string EventType { get; set; }

        public string Message { get; set; }
    }
}
