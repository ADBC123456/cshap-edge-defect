namespace DiscSorter.Core.Plc
{
    /// <summary>
    /// 上位机和 PLC 之间交换的 5 个变量，名字和 PLC 程序里的真实变量一一对应（路径见 PlcTags）。
    /// 仿真 PLC 和真实 PLC 都给出这几个值，"PLC 通讯"页直接显示它们。
    /// </summary>
    public class PlcVariables
    {
        /// <summary>库位个数。</summary>
        public const int SlotCount = 12;

        public PlcVariables()
        {
            // PLC 里 MatState 的类型是 ARRAY[0..12] OF INT，下标 0 不用，1~12 对应库位号
            MatState = new int[SlotCount + 1];
        }

        /// <summary>Rsc_xExecute：拍照请求。PLC 程序里它等于 E_Detection，也就是"检测工位上有工件"。</summary>
        public bool CaptureRequest { get; set; }

        /// <summary>tSTEP：PLC 自动流程的步号（CHAIN.st 里的 EA_Chain.Step）。</summary>
        public int Step { get; set; }

        /// <summary>PC_Category：上位机写入的类别码，1 Q、2 H、3 K、4 W、5 NG，0 表示还没有结果。</summary>
        public int Category { get; set; }

        /// <summary>PC_TargetSlot：上位机写入的目标库位，1~12，0 表示没有库位、需要人工取件。</summary>
        public int TargetSlot { get; set; }

        /// <summary>MainChain1.MatState：MatState[i] = i 表示库位 i 有料，0 表示空。</summary>
        public int[] MatState { get; set; }

        /// <summary>复制一份（数组也复制，改副本不会影响原来的）。</summary>
        public PlcVariables Clone()
        {
            PlcVariables copy = (PlcVariables)MemberwiseClone();
            copy.MatState = (int[])MatState.Clone();
            return copy;
        }
    }
}
