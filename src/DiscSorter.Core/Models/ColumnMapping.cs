namespace DiscSorter.Core.Models
{
    /// <summary>列映射：某一列存放哪个类别（对应数据库 ColumnMappings 表的一行）。</summary>
    public class ColumnMapping
    {
        public ColumnMapping()
        {
        }

        public ColumnMapping(int column, DefectCategory category)
        {
            Column = column;
            Category = category;
        }

        public int Column { get; set; }

        public DefectCategory Category { get; set; }
    }
}
