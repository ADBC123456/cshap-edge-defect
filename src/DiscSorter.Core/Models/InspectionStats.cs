using System.Collections.Generic;

namespace DiscSorter.Core.Models
{
    /// <summary>一段时间内的检测统计，监控页"今日统计"用。</summary>
    public class InspectionStats
    {
        public InspectionStats()
        {
            Counts = new Dictionary<DefectCategory, int>();
            foreach (DefectCategory category in DefectCategories.All)
            {
                Counts[category] = 0;
            }
        }

        /// <summary>检测总件数。</summary>
        public int Total { get; set; }

        /// <summary>每个类别的件数。</summary>
        public Dictionary<DefectCategory, int> Counts { get; private set; }

        /// <summary>能判断对错的件数（类别来自算法、文件名带真值）。</summary>
        public int Judged { get; set; }

        /// <summary>判对的件数。</summary>
        public int Correct { get; set; }

        /// <summary>平均总耗时（毫秒），没有记录时为 null。</summary>
        public double? AverageMs { get; set; }

        /// <summary>最大总耗时（毫秒），没有记录时为 null。</summary>
        public double? MaxMs { get; set; }
    }
}
