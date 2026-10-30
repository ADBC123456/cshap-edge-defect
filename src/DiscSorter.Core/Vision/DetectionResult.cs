using System;
using System.Collections.Generic;
using HalconDotNet;

namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 一次识别的全部输出：原图、各图层区域、圆、类别、特征表、耗时。
    /// 里面的 HALCON 图像和区域占用内存，不用时调用 Dispose() 释放。
    /// </summary>
    public class DetectionResult : IDisposable
    {
        public DetectionResult()
        {
            DefectTypes = new List<string>();
            Features = new FeatureTable();
        }

        /// <summary>原图（副本）。</summary>
        public HImage Image { get; set; }

        public string ImagePath { get; set; }

        /// <summary>这次识别做到了哪一步。</summary>
        public DetectionLevel Level { get; set; }

        /// <summary>圆盘区域。</summary>
        public HRegion Disk { get; set; }

        /// <summary>分割得到的原始区域（未分连通域）。</summary>
        public HRegion RawRegion { get; set; }

        /// <summary>候选区域（已分连通域、已按面积筛选）。</summary>
        public HRegion Candidates { get; set; }

        /// <summary>判定为缺陷的区域，顺序和 DefectTypes、特征表的行一一对应。</summary>
        public HRegion Defects { get; set; }

        public bool Found { get; set; }

        public double Row { get; set; }

        public double Column { get; set; }

        public double Radius { get; set; }

        /// <summary>算法给出的整图类别：Q/H/K/W/NONE。只有完整识别时才有值，否则为 null。</summary>
        public string AlgorithmLabel { get; set; }

        /// <summary>每个缺陷的类别。</summary>
        public List<string> DefectTypes { get; set; }

        public FeatureTable Features { get; set; }

        /// <summary>识别耗时（毫秒）。</summary>
        public double ElapsedMs { get; set; }

        /// <summary>提示或出错信息。</summary>
        public string Message { get; set; }

        public bool HasError { get; set; }

        public int CandidateCount
        {
            get { return CountRegions(Candidates); }
        }

        public int DefectCount
        {
            get { return CountRegions(Defects); }
        }

        public void Dispose()
        {
            if (Image != null)
            {
                Image.Dispose();
            }
            if (Disk != null)
            {
                Disk.Dispose();
            }
            if (RawRegion != null)
            {
                RawRegion.Dispose();
            }
            if (Candidates != null)
            {
                Candidates.Dispose();
            }
            if (Defects != null)
            {
                Defects.Dispose();
            }
        }

        /// <summary>区域里有几个对象。区域为空或未初始化时返回 0。</summary>
        public static int CountRegions(HRegion region)
        {
            if (region == null || !region.IsInitialized())
            {
                return 0;
            }
            return region.CountObj();
        }

        /// <summary>界面上显示的"能做到哪一步"。</summary>
        public static string DescribeLevel(DetectionLevel level)
        {
            switch (level)
            {
                case DetectionLevel.None:
                    return "只显示原图";
                case DetectionLevel.Disk:
                    return "原图 + 圆定位";
                case DetectionLevel.Segment:
                    return "原图 + 圆定位 + 分割";
                case DetectionLevel.Full:
                    return "完整识别（含类别和特征）";
                default:
                    return level.ToString();
            }
        }
    }
}
