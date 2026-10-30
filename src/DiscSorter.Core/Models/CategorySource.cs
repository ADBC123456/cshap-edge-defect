namespace DiscSorter.Core.Models
{
    /// <summary>入库类别从哪里来。</summary>
    public enum CategorySource
    {
        /// <summary>HALCON 算法的识别结果。</summary>
        Algorithm,

        /// <summary>图片文件名前缀（调试用，算法写完之前先把仓储和 PLC 流程跑通）。</summary>
        FileName
    }
}
