using System.Collections.Generic;

namespace DiscSorter.Core.Plc
{
    /// <summary>
    /// 与 PLC 交换的变量在 OPC UA 里的路径，以及 PLC 步号的中文含义。
    ///
    /// PLCnext 的变量路径格式："Arp.Plc.Eclr/" + 变量名；程序里的局部变量要加程序实例名，
    /// 例如 MatState 在实例 MainChain1 里，路径就是 "Arp.Plc.Eclr/MainChain1.MatState"。
    /// 如果 PLC 工程里的实例名不一样，只要改这里的常量。
    /// </summary>
    public static class PlcTags
    {
        /// <summary>PLCnext 全局数据空间在 OPC UA 里的命名空间 URI（连接时据此查出 ns 号）。</summary>
        public const string NamespaceUri = "http://phoenixcontact.com/OpcUA/PLCnext/GlobalDataSpace/";

        public const string CaptureRequest = "Arp.Plc.Eclr/Rsc_xExecute";
        public const string Step = "Arp.Plc.Eclr/tSTEP";
        // PC_Category、PC_TargetSlot 建在 MainChain 程序里（局部变量），所以路径带实例名 MainChain1
        public const string Category = "Arp.Plc.Eclr/MainChain1.PC_Category";
        public const string TargetSlot = "Arp.Plc.Eclr/MainChain1.PC_TargetSlot";
        public const string MatState = "Arp.Plc.Eclr/MainChain1.MatState";

        /// <summary>PLC 等待识别结果的步号。</summary>
        public const int StepWaitResult = 7;

        /// <summary>PLC 报警步（库位为 0、库位非法或已占用）。</summary>
        public const int StepAlarm = 40;

        /// <summary>PLC 放料并写 MatState 的步号。</summary>
        public const int StepPlace = 24;

        /// <summary>全部 5 个变量的说明，按变量表的显示顺序。</summary>
        public static List<PlcTagInfo> GetAll()
        {
            List<PlcTagInfo> tags = new List<PlcTagInfo>();
            tags.Add(Create("Rsc_xExecute", CaptureRequest, "BOOL", "PLC → 上位机",
                "拍照请求，等于工件到位信号 E_Detection"));
            tags.Add(Create("tSTEP", Step, "INT", "PLC → 上位机",
                "自动流程步号；7 = 等待识别结果，40 = 报警"));
            tags.Add(Create("PC_Category", Category, "INT", "上位机 → PLC",
                "类别码 1Q 2H 3K 4W 5NG；最后写，PLC 看到它不为 0 才继续（第 1 步由 PLC 清 0）"));
            tags.Add(Create("PC_TargetSlot", TargetSlot, "INT", "上位机 → PLC",
                "目标库位 1~12；0 表示没有库位，PLC 报警等人工取件"));
            tags.Add(Create("MatState[1..12]", MatState, "ARRAY[0..12] OF INT", "双向",
                "库位状态：PLC 放料后写 MatState[i] = i；上位机人工出库时写 0"));
            return tags;
        }

        /// <summary>PLC 步号的中文含义（对应修改后的 CHAIN.st）。</summary>
        public static string DescribeStep(int step)
        {
            switch (step)
            {
                case 0:
                    return "停止 / 等待启动";
                case 1:
                    return "信号复位";
                case 2:
                    return "等待放入工件";
                case 3:
                    return "拍照触发";
                case 6:
                    return "拍照延时（等工件稳定）";
                case 7:
                    return "等待识别结果";
                case 8:
                    return "校验目标库位";
                case 9:
                    return "检查伺服";
                case 10:
                case 11:
                case 12:
                case 13:
                    return "去取料位";
                case 14:
                    return "电磁铁吸取";
                case 15:
                    return "取料后抬起";
                case 16:
                    return "确认工件已取走";
                case 20:
                case 21:
                case 22:
                case 23:
                    return "去目标库位";
                case 24:
                    return "放料，写 MatState";
                case 25:
                case 26:
                    return "回原位";
                case 40:
                    return "报警：等人工取件后复位";
                case 100:
                    return "一件完成";
                default:
                    return "步 " + step;
            }
        }

        private static PlcTagInfo Create(string name, string path, string type, string direction, string description)
        {
            PlcTagInfo tag = new PlcTagInfo();
            tag.Name = name;
            tag.Path = path;
            tag.Type = type;
            tag.Direction = direction;
            tag.Description = description;
            return tag;
        }
    }
}
