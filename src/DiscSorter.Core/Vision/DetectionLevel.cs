namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 算法能做到哪一步。取决于 halcon\DiscDefect.hdev 里已经写好了哪些过程。
    /// </summary>
    public enum DetectionLevel
    {
        /// <summary>没有可用的过程，只能显示原图。</summary>
        None,

        /// <summary>有 dd_locate_disk：能找圆。</summary>
        Disk,

        /// <summary>还有 dd_segment：能分割出候选区域。</summary>
        Segment,

        /// <summary>有 dd_detect：完整识别，给出类别和特征。</summary>
        Full
    }
}
