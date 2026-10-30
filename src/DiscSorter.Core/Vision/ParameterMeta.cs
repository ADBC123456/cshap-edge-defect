namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 参数的中文名和取值范围，对应 halcon\detector_meta.json 里的一项，例如：
    /// "dyn_threshold_offset": { "label": "动态阈值偏移", "min": 1, "max": 120, "step": 1 }
    /// </summary>
    public class ParameterMeta
    {
        public string Label { get; set; }

        public double? Min { get; set; }

        public double? Max { get; set; }

        public double? Step { get; set; }

        public string Description { get; set; }
    }
}
