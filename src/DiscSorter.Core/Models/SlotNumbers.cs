namespace DiscSorter.Core.Models
{
    /// <summary>
    /// 库位编号规则：4 列 × 3 行，共 12 个库位。
    /// 库位号 = (列 - 1) × 3 + 行，所以列 1 是库位 1～3，列 4 是库位 10～12。
    /// 这个编号和 PLC 程序里 ServoPos[轴][库位号] 的下标一致。
    /// </summary>
    public static class SlotNumbers
    {
        public const int RowCount = 3;
        public const int ColumnCount = 4;
        public const int SlotCount = 12;

        public static int GetSlotNo(int row, int column)
        {
            return (column - 1) * RowCount + row;
        }

        public static int GetRow(int slotNo)
        {
            return (slotNo - 1) % RowCount + 1;
        }

        public static int GetColumn(int slotNo)
        {
            return (slotNo - 1) / RowCount + 1;
        }

        public static bool IsValid(int slotNo)
        {
            return slotNo >= 1 && slotNo <= SlotCount;
        }

        /// <summary>例如 5 → "库位 5（列 2 · 行 2）"。</summary>
        public static string Describe(int slotNo)
        {
            return "库位 " + slotNo + "（列 " + GetColumn(slotNo) + " · 行 " + GetRow(slotNo) + "）";
        }
    }
}
