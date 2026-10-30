namespace DiscSorter.Core.Models
{
    /// <summary>
    /// 入库类别。枚举的数值就是和 PLC 约定的类别码（与比赛时 PLC 的 index 一致）。
    /// </summary>
    public enum DefectCategory
    {
        /// <summary>缺角</summary>
        Q = 1,

        /// <summary>划痕</summary>
        H = 2,

        /// <summary>孔洞</summary>
        K = 3,

        /// <summary>污渍</summary>
        W = 4,

        /// <summary>异常：无缺陷、找不到圆、算法给不出类别</summary>
        NG = 5
    }
}
