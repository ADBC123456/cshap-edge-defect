using CommunityToolkit.Mvvm.ComponentModel;
using DiscSorter.Core.Plc;

namespace DiscSorter.App.ViewModels
{
    /// <summary>"PLC 通讯"页变量表的一行：变量名、OPC UA 路径、方向、当前值、含义。</summary>
    public partial class PlcVariableRow : ObservableObject
    {
        public PlcVariableRow(PlcTagInfo tag)
        {
            Name = tag.Name;
            Path = tag.Path;
            Type = tag.Type;
            Direction = tag.Direction;
            Description = tag.Description;
            value = "—";
        }

        public string Name { get; private set; }

        public string Path { get; private set; }

        public string Type { get; private set; }

        public string Direction { get; private set; }

        public string Description { get; private set; }

        /// <summary>当前值（每 50 ms 刷新一次）。</summary>
        [ObservableProperty]
        private string value;
    }
}
