using System;
using System.IO;

namespace DiscSorter.Core.Models
{
    /// <summary>一次检测记录（对应数据库 InspectionRecords 表的一行）。</summary>
    public class InspectionRecord
    {
        public long Id { get; set; }

        public long? JobId { get; set; }

        public DateTime Time { get; set; }

        public string ImagePath { get; set; }

        /// <summary>算法给出的标签（Q/H/K/W/NONE），算法没给出时为 null。</summary>
        public string AlgorithmLabel { get; set; }

        /// <summary>最终入库类别。</summary>
        public DefectCategory Category { get; set; }

        public CategorySource Source { get; set; }

        /// <summary>文件名前缀给出的真值标签，没有时为 null。</summary>
        public string ExpectedLabel { get; set; }

        /// <summary>算法是否判对。只有类别来自算法、并且文件名带真值时才有值。</summary>
        public bool? IsCorrect { get; set; }

        /// <summary>目标库位，0 表示人工取件。</summary>
        public int TargetSlot { get; set; }

        public long? TaskId { get; set; }

        public bool CircleFound { get; set; }

        public double? CircleRow { get; set; }

        public double? CircleColumn { get; set; }

        public double? Radius { get; set; }

        /// <summary>HALCON 识别耗时（毫秒）。</summary>
        public double? DetectMs { get; set; }

        /// <summary>从收到检测请求到写完结果的总耗时（毫秒）。</summary>
        public double? TotalMs { get; set; }

        // ---------- 以下是界面显示用的只读属性 ----------

        public string FileName
        {
            get
            {
                if (ImagePath == null)
                {
                    return "—";
                }
                return Path.GetFileName(ImagePath);
            }
        }

        public string CategoryText
        {
            get { return DefectCategories.GetName(Category); }
        }

        public string SourceText
        {
            get
            {
                if (Source == CategorySource.Algorithm)
                {
                    return "算法";
                }
                return "文件名真值";
            }
        }

        public string TargetText
        {
            get
            {
                if (TargetSlot == 0)
                {
                    return "0（人工取件）";
                }
                return TargetSlot.ToString();
            }
        }

        public string CorrectText
        {
            get
            {
                if (IsCorrect == null)
                {
                    return "—";
                }
                if (IsCorrect.Value)
                {
                    return "✓";
                }
                return "✗";
            }
        }
    }
}
