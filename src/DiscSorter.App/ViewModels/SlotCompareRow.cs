using CommunityToolkit.Mvvm.ComponentModel;

namespace DiscSorter.App.ViewModels
{
    /// <summary>"PLC 通讯"页库位对照表的一行：同一个库位，上位机数据库和 PLC 的 MatState 是否一致。</summary>
    public partial class SlotCompareRow : ObservableObject
    {
        public SlotCompareRow(int slotNo)
        {
            SlotNo = slotNo;
            hostText = "—";
            plcText = "—";
            resultText = "—";
        }

        public int SlotNo { get; private set; }

        /// <summary>上位机数据库里的状态，例如"已占用 Q"。</summary>
        [ObservableProperty]
        private string hostText;

        /// <summary>PLC 的 MatState 值，例如"5（有料）"。</summary>
        [ObservableProperty]
        private string plcText;

        /// <summary>"一致"、"不一致"或"执行中"等。</summary>
        [ObservableProperty]
        private string resultText;

        /// <summary>是否不一致（界面上标红）。</summary>
        [ObservableProperty]
        private bool isMismatch;
    }
}
