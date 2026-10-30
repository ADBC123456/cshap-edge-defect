namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 界面上可以调节的一个算法参数。
    /// 取值统一用文字保存（方便在文本框里编辑），传给 HALCON 时再按 Kind 转成数字。
    /// </summary>
    public class DetectorParameter
    {
        /// <summary>参数名（dd_default_params 字典里的键），例如 dyn_threshold_offset。</summary>
        public string Key { get; set; }

        public ParameterKind Kind { get; set; }

        /// <summary>当前值。</summary>
        public string Value { get; set; }

        /// <summary>dd_default_params 给出的默认值。</summary>
        public string DefaultValue { get; set; }

        /// <summary>中文名，来自 detector_meta.json；没有时为 null。</summary>
        public string Label { get; set; }

        /// <summary>滑条最小值，没有时为 null（不显示滑条）。</summary>
        public double? Min { get; set; }

        /// <summary>滑条最大值。</summary>
        public double? Max { get; set; }

        /// <summary>滑条步长。</summary>
        public double? Step { get; set; }

        /// <summary>参数说明。</summary>
        public string Description { get; set; }
    }
}
