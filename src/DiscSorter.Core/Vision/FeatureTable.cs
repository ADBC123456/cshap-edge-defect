using System.Collections.Generic;

namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 特征表：每行一个缺陷，每列一个特征。
    /// 数据来自 dd_detect 输出的 Result 字典（每个键是一列）。
    /// </summary>
    public class FeatureTable
    {
        public FeatureTable()
        {
            Columns = new List<string>();
            Rows = new List<List<string>>();
        }

        /// <summary>列名，例如 type、area。</summary>
        public List<string> Columns { get; private set; }

        /// <summary>每一行是一个缺陷的各列取值（都已转成文字）。</summary>
        public List<List<string>> Rows { get; private set; }
    }
}
