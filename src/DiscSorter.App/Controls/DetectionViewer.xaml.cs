using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DiscSorter.Core.Vision;
using HalconDotNet;

namespace DiscSorter.App.Controls
{
    /// <summary>显示方式。</summary>
    public enum ViewerMode
    {
        /// <summary>原图 + 各图层（圆、分割、候选、缺陷、标签）。</summary>
        Annotated,

        /// <summary>只显示原图。</summary>
        Original,

        /// <summary>黑底白色的分割掩膜（和 Python 版的"分类掩膜"对应）。</summary>
        Mask
    }

    /// <summary>
    /// 把 DetectionResult 按图层画到 HALCON 窗口上。
    /// 所有输入都是"依赖属性"（DependencyProperty），所以可以在 XAML 里直接绑定，例如：
    ///   &lt;controls:DetectionViewer Result="{Binding Result}" ShowDisk="{Binding ShowDisk}"/&gt;
    /// 任何一个属性变了，都会调用 Render() 重新画一遍。
    /// 鼠标滚轮缩放、拖动平移、双击复位是 HSmartWindowControlWPF 自带的功能。
    /// </summary>
    public partial class DetectionViewer : UserControl
    {
        private HSmartWindowControlWPF halconView;
        private bool windowReady;
        private int lastImageWidth;
        private int lastImageHeight;

        public DetectionViewer()
        {
            InitializeComponent();

            // 在 VS 设计器里不创建 HALCON 窗口，只显示占位文字
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                return;
            }

            halconView = new HSmartWindowControlWPF();
            halconView.HDoubleClickToFitContent = true;
            halconView.HInitWindow += HalconView_HInitWindow;
            Host.Children.Add(halconView);
            Placeholder.Visibility = Visibility.Collapsed;

            SizeChanged += DetectionViewer_SizeChanged;
        }

        // ==================== 依赖属性 ====================
        // 写法是固定的：一个 static readonly 字段 + 一个普通属性。
        // 最后一个参数 OnDisplayPropertyChanged 表示"这个属性变了就重画"。

        public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
            "Result", typeof(DetectionResult), typeof(DetectionViewer),
            new PropertyMetadata(null, OnDisplayPropertyChanged));

        public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
            "Mode", typeof(ViewerMode), typeof(DetectionViewer),
            new PropertyMetadata(ViewerMode.Annotated, OnDisplayPropertyChanged));

        public static readonly DependencyProperty ShowDiskProperty = DependencyProperty.Register(
            "ShowDisk", typeof(bool), typeof(DetectionViewer),
            new PropertyMetadata(true, OnDisplayPropertyChanged));

        public static readonly DependencyProperty ShowRawRegionProperty = DependencyProperty.Register(
            "ShowRawRegion", typeof(bool), typeof(DetectionViewer),
            new PropertyMetadata(false, OnDisplayPropertyChanged));

        public static readonly DependencyProperty ShowCandidatesProperty = DependencyProperty.Register(
            "ShowCandidates", typeof(bool), typeof(DetectionViewer),
            new PropertyMetadata(true, OnDisplayPropertyChanged));

        public static readonly DependencyProperty ShowDefectsProperty = DependencyProperty.Register(
            "ShowDefects", typeof(bool), typeof(DetectionViewer),
            new PropertyMetadata(true, OnDisplayPropertyChanged));

        public static readonly DependencyProperty ShowLabelsProperty = DependencyProperty.Register(
            "ShowLabels", typeof(bool), typeof(DetectionViewer),
            new PropertyMetadata(true, OnDisplayPropertyChanged));

        public static readonly DependencyProperty HighlightIndexProperty = DependencyProperty.Register(
            "HighlightIndex", typeof(int), typeof(DetectionViewer),
            new PropertyMetadata(-1, OnDisplayPropertyChanged));

        /// <summary>要显示的识别结果。</summary>
        public DetectionResult Result
        {
            get { return (DetectionResult)GetValue(ResultProperty); }
            set { SetValue(ResultProperty, value); }
        }

        public ViewerMode Mode
        {
            get { return (ViewerMode)GetValue(ModeProperty); }
            set { SetValue(ModeProperty, value); }
        }

        public bool ShowDisk
        {
            get { return (bool)GetValue(ShowDiskProperty); }
            set { SetValue(ShowDiskProperty, value); }
        }

        public bool ShowRawRegion
        {
            get { return (bool)GetValue(ShowRawRegionProperty); }
            set { SetValue(ShowRawRegionProperty, value); }
        }

        public bool ShowCandidates
        {
            get { return (bool)GetValue(ShowCandidatesProperty); }
            set { SetValue(ShowCandidatesProperty, value); }
        }

        public bool ShowDefects
        {
            get { return (bool)GetValue(ShowDefectsProperty); }
            set { SetValue(ShowDefectsProperty, value); }
        }

        public bool ShowLabels
        {
            get { return (bool)GetValue(ShowLabelsProperty); }
            set { SetValue(ShowLabelsProperty, value); }
        }

        /// <summary>加粗显示第几个缺陷（从 0 开始，对应特征表的行），-1 表示不加粗。</summary>
        public int HighlightIndex
        {
            get { return (int)GetValue(HighlightIndexProperty); }
            set { SetValue(HighlightIndexProperty, value); }
        }

        private static void OnDisplayPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            DetectionViewer viewer = (DetectionViewer)sender;
            viewer.Render();
        }

        // ==================== 窗口事件 ====================

        /// <summary>HALCON 窗口创建好了。等界面布局完成后再画第一帧。</summary>
        private void HalconView_HInitWindow(object sender, EventArgs e)
        {
            windowReady = true;
            SetWindowFont();
            Dispatcher.BeginInvoke(new Action(FitAndRender), DispatcherPriority.Background);
        }

        /// <summary>
        /// 图上文字用的字体，格式是"字体名-样式-字号"，按顺序尝试，第一个能用的就用它。
        /// 同一款字体在不同系统里名字可能不一样，例如中文 Windows 里 HALCON 只认"微软雅黑"，
        /// 不认"Microsoft YaHei"，所以多写几个备选。
        /// 某台电脑有哪些字体名，可以在 HDevelop 里执行 query_font (WindowHandle, Fonts) 查看。
        /// </summary>
        private static readonly string[] FontNames = new string[]
        {
            "Microsoft YaHei UI-Bold-14",
            "微软雅黑-Bold-14",
            "黑体-Bold-14"
        };

        /// <summary>依次尝试 FontNames 里的字体，设置成功就停；全部失败就保留 HALCON 的默认字体。</summary>
        private void SetWindowFont()
        {
            foreach (string fontName in FontNames)
            {
                try
                {
                    halconView.HalconWindow.SetFont(fontName);
                    return;
                }
                catch (HOperatorException)
                {
                    // 这台电脑没有这个字体名，试下一个
                }
            }
        }

        /// <summary>控件大小变了，重新让整幅图适应窗口。</summary>
        private void DetectionViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(FitAndRender), DispatcherPriority.Background);
        }

        private void FitAndRender()
        {
            lastImageWidth = 0;   // 让 Render() 重新计算显示范围
            Render();
        }

        // ==================== 画图 ====================

        /// <summary>清空窗口，按当前的显示方式和图层开关重新画。</summary>
        private void Render()
        {
            if (!windowReady)
            {
                return;
            }

            HWindow window = halconView.HalconWindow;
            window.ClearWindow();
            DetectionResult result = Result;
            if (result == null || result.Image == null || !result.Image.IsInitialized())
            {
                return;
            }

            int width;
            int height;
            result.Image.GetImageSize(out width, out height);
            if (width != lastImageWidth || height != lastImageHeight)
            {
                // 图像尺寸变了才重新适应窗口，这样逐张浏览时，用户放大的位置保持不变
                FitImage(width, height);
                lastImageWidth = width;
                lastImageHeight = height;
            }

            if (Mode == ViewerMode.Mask)
            {
                HImage mask = BuildMask(result, width, height);
                window.DispObj(mask);
                mask.Dispose();
                return;
            }

            window.DispObj(result.Image);
            if (Mode == ViewerMode.Original)
            {
                return;
            }

            window.SetDraw("margin");

            // 原始分割（半透明蓝色填充）
            if (ShowRawRegion && DetectionResult.CountRegions(result.RawRegion) > 0)
            {
                window.SetDraw("fill");
                window.SetColor("#38BDF866");
                window.DispObj(result.RawRegion);
                window.SetDraw("margin");
            }

            // 候选区域（黄色细线）
            if (ShowCandidates && DetectionResult.CountRegions(result.Candidates) > 0)
            {
                window.SetLineWidth(1);
                window.SetColor("#FACC15");
                window.DispObj(result.Candidates);
            }

            // 圆盘（绿色）和圆心十字
            if (ShowDisk && DetectionResult.CountRegions(result.Disk) > 0)
            {
                window.SetLineWidth(2);
                window.SetColor("#22C55E");
                window.DispObj(result.Disk);
                if (result.Found)
                {
                    window.DispCross(result.Row, result.Column, 16, 0.785398);
                }
            }

            // 缺陷（按类别上色）
            if (ShowDefects && DetectionResult.CountRegions(result.Defects) > 0)
            {
                DrawDefects(window, result);
            }

            // 左上角写类别
            if (result.AlgorithmLabel != null)
            {
                window.DispText(new HTuple("类别 " + result.AlgorithmLabel), "window",
                    new HTuple(8), new HTuple(8), new HTuple("white"),
                    new HTuple("box_color"), new HTuple("#000000B0"));
            }
        }

        private void DrawDefects(HWindow window, DetectionResult result)
        {
            int count = result.Defects.CountObj();
            for (int i = 0; i < count; i++)
            {
                string type = "?";
                if (i < result.DefectTypes.Count)
                {
                    type = result.DefectTypes[i];
                }
                string color = GetTypeColor(type);

                HRegion defect = result.Defects.SelectObj(i + 1);   // HALCON 的序号从 1 开始
                window.SetColor(color);
                if (i == HighlightIndex)
                {
                    window.SetLineWidth(5);
                }
                else
                {
                    window.SetLineWidth(2);
                }
                window.DispObj(defect);

                if (ShowLabels)
                {
                    int row1;
                    int column1;
                    int row2;
                    int column2;
                    defect.SmallestRectangle1(out row1, out column1, out row2, out column2);
                    int textRow = Math.Max(0, row1 - 26);
                    window.DispText(new HTuple((i + 1) + ":" + type), "image",
                        new HTuple(textRow), new HTuple(column1), new HTuple(color),
                        new HTuple("box"), new HTuple("false"));
                }
                defect.Dispose();
            }
        }

        /// <summary>
        /// 计算显示范围，让整幅图在窗口里居中、等比例显示（两边或上下留黑边）。
        /// </summary>
        private void FitImage(int width, int height)
        {
            double controlWidth = halconView.ActualWidth;
            double controlHeight = halconView.ActualHeight;
            if (controlWidth < 1 || controlHeight < 1)
            {
                return;
            }

            double scale = Math.Min(controlWidth / width, controlHeight / height);
            double partWidth = controlWidth / scale;
            double partHeight = controlHeight / scale;
            double left = (width - partWidth) / 2;
            double top = (height - partHeight) / 2;
            halconView.HImagePart = new Rect(left, top, partWidth, partHeight);
        }

        /// <summary>生成黑底白色的掩膜图：优先用缺陷，其次候选，再其次原始分割。</summary>
        private static HImage BuildMask(DetectionResult result, int width, int height)
        {
            HRegion source = null;
            if (DetectionResult.CountRegions(result.Defects) > 0)
            {
                source = result.Defects;
            }
            else if (DetectionResult.CountRegions(result.Candidates) > 0)
            {
                source = result.Candidates;
            }
            else if (DetectionResult.CountRegions(result.RawRegion) > 0)
            {
                source = result.RawRegion;
            }

            if (source == null)
            {
                HImage black = new HImage();
                black.GenImageConst("byte", width, height);
                return black;
            }

            HRegion union = source.Union1();
            HImage mask = union.RegionToBin(255, 0, width, height);
            union.Dispose();
            return mask;
        }

        /// <summary>缺陷类别的颜色，和 Styles.xaml 里的类别色一致。</summary>
        private static string GetTypeColor(string type)
        {
            switch (type.ToUpper())
            {
                case "Q":
                    return "#E11D48";
                case "H":
                    return "#16A34A";
                case "K":
                    return "#CA8A04";
                case "W":
                    return "#7C3AED";
                default:
                    return "#F3F4F6";
            }
        }
    }
}
