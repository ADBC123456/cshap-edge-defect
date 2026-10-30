using System;

namespace DiscSorter.Core.Models
{
    /// <summary>一次入库任务（对应数据库 StorageTasks 表的一行）。</summary>
    public class StorageTask
    {
        public long TaskId { get; set; }

        /// <summary>PLC 的任务号。</summary>
        public long? JobId { get; set; }

        public DefectCategory Category { get; set; }

        public int SlotNo { get; set; }

        public StorageTaskStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string Note { get; set; }

        /// <summary>界面上显示的类别名称。</summary>
        public string CategoryText
        {
            get { return DefectCategories.GetName(Category); }
        }

        /// <summary>界面上显示的状态。</summary>
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case StorageTaskStatus.Reserved:
                        return "执行中";
                    case StorageTaskStatus.Completed:
                        return "已入库";
                    case StorageTaskStatus.Failed:
                        return "失败";
                    case StorageTaskStatus.Cancelled:
                        return "已取消";
                    case StorageTaskStatus.NeedsCheck:
                        return "待确认";
                    default:
                        return Status.ToString();
                }
            }
        }
    }
}
