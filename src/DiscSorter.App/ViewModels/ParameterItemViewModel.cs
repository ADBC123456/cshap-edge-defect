using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DiscSorter.Core.Vision;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 参数面板的一行：有范围的数值参数显示"滑条 + 文本框"，其余只显示文本框。
    /// （[ObservableProperty] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class ParameterItemViewModel : ObservableObject
    {
        private readonly DetectorParameter parameter;

        /// <summary>设计器预览用的构造函数。</summary>
        public ParameterItemViewModel()
        {
            parameter = new DetectorParameter();
            parameter.Key = "dyn_threshold_offset";
            parameter.Kind = ParameterKind.Integer;
            parameter.DefaultValue = "22";
            parameter.Label = "动态阈值偏移";
            parameter.Min = 1;
            parameter.Max = 120;
            parameter.Step = 1;
            value = "30";
        }

        public ParameterItemViewModel(DetectorParameter parameter)
        {
            this.parameter = parameter;
            value = parameter.Value;
        }

        /// <summary>用户改了值（视觉页用它做"停止拖动 0.4 秒后自动重新识别"）。</summary>
        public event EventHandler ValueChanged;

        public string Key
        {
            get { return parameter.Key; }
        }

        /// <summary>显示的名称：有中文名用中文名，没有就用参数名。</summary>
        public string Label
        {
            get
            {
                if (parameter.Label != null)
                {
                    return parameter.Label;
                }
                return parameter.Key;
            }
        }

        public string DefaultText
        {
            get { return "（默认 " + parameter.DefaultValue + "）"; }
        }

        /// <summary>鼠标悬停时显示的说明。</summary>
        public string ToolTip
        {
            get
            {
                if (parameter.Description != null)
                {
                    return parameter.Key + "：" + parameter.Description;
                }
                return parameter.Key;
            }
        }

        /// <summary>有取值范围的数值参数才显示滑条。</summary>
        public bool HasRange
        {
            get { return parameter.Kind != ParameterKind.Text && parameter.Min != null && parameter.Max != null; }
        }

        public double Minimum
        {
            get
            {
                if (parameter.Min == null)
                {
                    return 0;
                }
                return parameter.Min.Value;
            }
        }

        public double Maximum
        {
            get
            {
                if (parameter.Max == null)
                {
                    return 100;
                }
                return parameter.Max.Value;
            }
        }

        public double Step
        {
            get
            {
                if (parameter.Step != null)
                {
                    return parameter.Step.Value;
                }
                if (parameter.Kind == ParameterKind.Integer)
                {
                    return 1;
                }
                return 0.1;
            }
        }

        /// <summary>文本框里的值。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SliderValue))]
        private string value;

        /// <summary>滑条的值。拖动滑条时按步长取整，再写回文本框。</summary>
        public double SliderValue
        {
            get
            {
                double number;
                if (double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    return number;
                }
                return Minimum;
            }
            set
            {
                double snapped = Math.Round(value / Step) * Step;
                if (parameter.Kind == ParameterKind.Integer)
                {
                    Value = Math.Round(snapped).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    Value = snapped.ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>生成带当前值的参数对象（交给 ParameterStore 转成 HALCON 字典或保存）。</summary>
        public DetectorParameter ToParameter()
        {
            DetectorParameter copy = new DetectorParameter();
            copy.Key = parameter.Key;
            copy.Kind = parameter.Kind;
            copy.Value = Value;
            copy.DefaultValue = parameter.DefaultValue;
            copy.Label = parameter.Label;
            copy.Min = parameter.Min;
            copy.Max = parameter.Max;
            copy.Step = parameter.Step;
            copy.Description = parameter.Description;
            return copy;
        }

        public void ResetToDefault()
        {
            Value = parameter.DefaultValue;
        }

        /// <summary>
        /// Value 改变后自动调用（这个方法名是 [ObservableProperty] 约定好的："On" + 属性名 + "Changed"）。
        /// </summary>
        partial void OnValueChanged(string value)
        {
            if (ValueChanged != null)
            {
                ValueChanged(this, EventArgs.Empty);
            }
        }
    }
}
