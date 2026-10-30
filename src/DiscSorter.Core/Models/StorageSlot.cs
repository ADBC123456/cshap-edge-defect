using System;

namespace DiscSorter.Core.Models
{
    /// <summary>一个库位（对应数据库 StorageSlots 表的一行）。</summary>
    public class StorageSlot
    {
        public int SlotNo { get; set; }

        public int Row { get; set; }

        public int Column { get; set; }

        public SlotState State { get; set; }

        /// <summary>占用这个库位的任务号，空闲时为 null。</summary>
        public long? TaskId { get; set; }

        /// <summary>库位里工件的类别，空闲时为 null。</summary>
        public DefectCategory? Category { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
