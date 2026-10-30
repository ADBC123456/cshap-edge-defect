using System;
using DiscSorter.Core.Models;
using DiscSorter.Core.Vision;

namespace DiscSorter.Core.Workflow
{
    /// <summary>
    /// 一件工件识别完成、结果已写给 PLC 时的事件参数。
    /// 注意：Result 里的 HALCON 图像归事件的接收方所有，接收方不用时要 Dispose()。
    /// </summary>
    public class InspectionFinishedEventArgs : EventArgs
    {
        public long JobId { get; set; }

        /// <summary>识别结果。图像源没有图片时为 null。</summary>
        public DetectionResult Result { get; set; }

        /// <summary>最终采用的标签（Q/H/K/W/NONE）。</summary>
        public string Label { get; set; }

        public DefectCategory Category { get; set; }

        public AllocationResult Allocation { get; set; }

        public InspectionRecord Record { get; set; }
    }
}
