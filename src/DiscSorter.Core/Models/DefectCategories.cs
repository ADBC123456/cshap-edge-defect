using System.IO;

namespace DiscSorter.Core.Models
{
    /// <summary>
    /// 和入库类别有关的小工具：中文名称、算法标签转类别、从文件名取真值。
    /// </summary>
    public static class DefectCategories
    {
        /// <summary>全部 5 个类别，按类别码从小到大排列。</summary>
        public static readonly DefectCategory[] All = new DefectCategory[]
        {
            DefectCategory.Q,
            DefectCategory.H,
            DefectCategory.K,
            DefectCategory.W,
            DefectCategory.NG
        };

        /// <summary>类别的中文名称，例如 Q → "缺角 Q"。</summary>
        public static string GetName(DefectCategory category)
        {
            switch (category)
            {
                case DefectCategory.Q:
                    return "缺角 Q";
                case DefectCategory.H:
                    return "划痕 H";
                case DefectCategory.K:
                    return "孔洞 K";
                case DefectCategory.W:
                    return "污渍 W";
                case DefectCategory.NG:
                    return "异常 NG";
                default:
                    return category.ToString();
            }
        }

        /// <summary>
        /// 把 HALCON 算法输出的标签（"Q"、"H"、"K"、"W"、"NONE"）转成入库类别。
        /// "NONE"、空值和其他认不出的标签都算 NG。
        /// </summary>
        public static DefectCategory FromAlgorithmLabel(string label)
        {
            if (label == null)
            {
                return DefectCategory.NG;
            }

            string text = label.Trim().ToUpper();
            if (text == "Q")
            {
                return DefectCategory.Q;
            }
            if (text == "H")
            {
                return DefectCategory.H;
            }
            if (text == "K")
            {
                return DefectCategory.K;
            }
            if (text == "W")
            {
                return DefectCategory.W;
            }
            return DefectCategory.NG;
        }

        /// <summary>
        /// 从图片文件名的前缀取真值标签，例如 "Q_1.jpg" → "Q"，"NONE_clean.png" → "NONE"。
        /// 前缀不是 Q/H/K/W/NONE 时返回 null。
        /// </summary>
        public static string GetLabelFromFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            string prefix = name.Split('_')[0].ToUpper();
            if (prefix == "Q" || prefix == "H" || prefix == "K" || prefix == "W" || prefix == "NONE")
            {
                return prefix;
            }
            return null;
        }
    }
}
