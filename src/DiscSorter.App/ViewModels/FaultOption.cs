using DiscSorter.Core.Plc;

namespace DiscSorter.App.ViewModels
{
    /// <summary>故障注入下拉框的一个选项：Value 是故障类型，Name 是显示的中文名。</summary>
    public class FaultOption
    {
        public FaultOption(SimulatedFault value, string name)
        {
            Value = value;
            Name = name;
        }

        public SimulatedFault Value { get; private set; }

        public string Name { get; private set; }
    }
}
