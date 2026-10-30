namespace DiscSorter.Core.Plc
{
    /// <summary>一个 PLC 变量的说明（"PLC 通讯"页的变量表用）。</summary>
    public class PlcTagInfo
    {
        /// <summary>PLC 程序里的变量名，例如 "Rsc_xExecute"。</summary>
        public string Name { get; set; }

        /// <summary>OPC UA 路径，例如 "Arp.Plc.Eclr/Rsc_xExecute"。</summary>
        public string Path { get; set; }

        /// <summary>数据类型，例如 "BOOL"、"INT"。</summary>
        public string Type { get; set; }

        /// <summary>方向："PLC → 上位机"、"上位机 → PLC" 或 "双向"。</summary>
        public string Direction { get; set; }

        /// <summary>中文说明。</summary>
        public string Description { get; set; }
    }
}
