using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Vision;
using HalconDotNet;
using Microsoft.Win32;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 视觉调试页：加载 HALCON 程序、浏览图片、按图层查看识别过程、调参数并保存、看特征表。
    /// 这里用的 HalconDetector 和自动运行用的是同一个，所以这里调好的参数，自动运行时直接生效。
    /// （[ObservableProperty] 和 [RelayCommand] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class VisionViewModel : ObservableObject
    {
        private const string ImageFilter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|所有文件|*.*";

        private readonly HalconDetector detector;
        private readonly SettingsService settings;
        private readonly string defaultImageFolder;
        private readonly ImageFolder images = new ImageFolder();
        private readonly HikCamera camera;
        private HDict defaultParameters = new HDict();

        // 最近一次从相机取的图（调参数时反复识别这一张）。为 null 表示当前显示的是文件夹里的图
        private HImage cameraImage;
        private string cameraImagePath;

        // 实时预览：参数停止变化 0.4 秒后自动重新识别
        private readonly DispatcherTimer previewTimer = new DispatcherTimer();

        /// <summary>设计器预览用的构造函数：填一些示例数据。</summary>
        public VisionViewModel()
        {
            Parameters = new ObservableCollection<ParameterItemViewModel>();
            Parameters.Add(new ParameterItemViewModel());
            Parameters.Add(new ParameterItemViewModel());
            programPath = @"E:\woker\c#edge-defect\halcon\DiscDefect.hdev";
            programStatus = "已加载：dd_default_params、dd_detect、dd_locate_disk、dd_segment";
            capabilityText = "完整识别（含类别和特征）";
            imageText = "Q_1.png · 7 / 12";
            categoryText = "Q　→　入库类别 缺角 Q";
            circleText = "(367.6, 585.3)  R = 235.9";
            countText = "候选 1 · 缺陷 1";
            timeText = "33.0 ms";
            expectedText = "Q  ✓";
        }

        public VisionViewModel(HalconDetector detector, SettingsService settings, string defaultProgramPath, string defaultImageFolder,
            HikCamera camera)
        {
            this.detector = detector;
            this.settings = settings;
            this.camera = camera;
            this.defaultImageFolder = defaultImageFolder;
            Parameters = new ObservableCollection<ParameterItemViewModel>();

            previewTimer.Interval = TimeSpan.FromMilliseconds(400);
            previewTimer.Tick += PreviewTimer_Tick;

            ProgramPath = settings.Get(SettingKeys.ProgramPath, defaultProgramPath);
            if (!File.Exists(ProgramPath) && File.Exists(defaultProgramPath))
            {
                // 保存的路径失效了（例如工程文件夹改过名），退回工程里的默认程序
                ProgramPath = defaultProgramPath;
                settings.Set(SettingKeys.ProgramPath, ProgramPath);
            }
            string folder = settings.Get(SettingKeys.ImageFolder, defaultImageFolder);
            if (Directory.Exists(folder))
            {
                images.OpenFolder(folder);
            }
            LoadProgram();
        }

        /// <summary>算法参数列表（参数个数由 .hdev 决定，所以用列表显示）。</summary>
        public ObservableCollection<ParameterItemViewModel> Parameters { get; private set; }

        // ---------------- 算法程序 ----------------

        [ObservableProperty]
        private string programPath;

        [ObservableProperty]
        private string programStatus = "尚未加载";

        [ObservableProperty]
        private string capabilityText = "只显示原图";

        // ---------------- 图片和显示 ----------------

        [ObservableProperty]
        private string imageText = "未打开图片";

        [ObservableProperty]
        private bool showDisk = true;

        [ObservableProperty]
        private bool showRawRegion = false;

        [ObservableProperty]
        private bool showCandidates = true;

        [ObservableProperty]
        private bool showDefects = true;

        [ObservableProperty]
        private bool showLabels = true;

        /// <summary>三图并排：原图 | 标注 | 掩膜（和 Python 版控制台一样）。</summary>
        [ObservableProperty]
        private bool tripleView = false;

        [ObservableProperty]
        private bool livePreview = true;

        /// <summary>自动运行时锁定本页（识别器正被分拣流程使用，不能在运行中改参数）。</summary>
        [ObservableProperty]
        private bool isLocked = false;

        // ---------------- 识别结果 ----------------

        /// <summary>当前显示的识别结果（图像控件绑定它）。</summary>
        [ObservableProperty]
        private DetectionResult result;

        [ObservableProperty]
        private string categoryText = "—";

        [ObservableProperty]
        private string circleText = "—";

        [ObservableProperty]
        private string countText = "—";

        [ObservableProperty]
        private string timeText = "—";

        [ObservableProperty]
        private string expectedText = "—";

        [ObservableProperty]
        private string resultMessage = "";

        [ObservableProperty]
        private bool resultHasError = false;

        /// <summary>特征表（DataGrid 绑定它）。</summary>
        [ObservableProperty]
        private DataView featureTable;

        /// <summary>特征表里选中的行，图像上会加粗显示这个缺陷。</summary>
        [ObservableProperty]
        private int selectedFeatureIndex = -1;

        [ObservableProperty]
        private string parameterMessage = "";

        // ---------------- 按钮命令：程序 ----------------

        [RelayCommand]
        private void ReloadProgram()
        {
            LoadProgram();
        }

        [RelayCommand]
        private void BrowseProgram()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择 HALCON 程序";
            dialog.Filter = "HDevelop 程序|*.hdev";
            if (dialog.ShowDialog() != true)
            {
                return;
            }
            ProgramPath = dialog.FileName;
            settings.Set(SettingKeys.ProgramPath, ProgramPath);
            LoadProgram();
        }

        // ---------------- 按钮命令：图片 ----------------

        [RelayCommand]
        private void OpenImage()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "打开图片";
            dialog.Filter = ImageFilter;
            if (dialog.ShowDialog() != true)
            {
                return;
            }
            ForgetCameraImage();
            images.OpenFile(dialog.FileName);
            settings.Set(SettingKeys.ImageFolder, images.Folder);
            DetectCurrentImage();
        }

        [RelayCommand]
        private void OpenFolder()
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            dialog.Title = "打开图片文件夹";
            if (dialog.ShowDialog() != true)
            {
                return;
            }
            ForgetCameraImage();
            images.OpenFolder(dialog.FolderName);
            settings.Set(SettingKeys.ImageFolder, images.Folder);
            DetectCurrentImage();
        }

        [RelayCommand]
        private void PreviousImage()
        {
            ForgetCameraImage();
            images.MovePrevious();
            DetectCurrentImage();
        }

        [RelayCommand]
        private void NextImage()
        {
            ForgetCameraImage();
            images.MoveNext();
            DetectCurrentImage();
        }

        /// <summary>从相机拍一张（相机要先在"相机"页打开），之后调参数时反复识别这一张。</summary>
        [RelayCommand]
        private void GrabFromCamera()
        {
            if (camera == null || !camera.IsOpen)
            {
                ResultMessage = "相机没有打开：先到“相机”页枚举并打开相机";
                ResultHasError = true;
                return;
            }
            try
            {
                string path;
                HImage image = camera.Grab(out path);
                ForgetCameraImage();
                cameraImage = image;
                cameraImagePath = path;
                DetectCurrentImage();
            }
            catch (Exception ex)
            {
                ResultMessage = "相机取图失败：" + ex.Message;
                ResultHasError = true;
            }
        }

        [RelayCommand]
        private void Redetect()
        {
            DetectCurrentImage();
        }

        // ---------------- 按钮命令：参数 ----------------

        /// <summary>"应用"：把面板上的参数交给识别器，然后重新识别当前图片。</summary>
        [RelayCommand]
        private void ApplyParameters()
        {
            if (SendParametersToDetector())
            {
                ParameterMessage = "参数已应用（还没保存）";
                DetectCurrentImage();
            }
        }

        /// <summary>"保存"：写入 halcon\detector.json，下次启动和自动运行都用这组参数。</summary>
        [RelayCommand]
        private void SaveParameters()
        {
            if (detector.ProgramPath == null)
            {
                ParameterMessage = "没有加载程序，无法保存参数";
                return;
            }
            try
            {
                string path = ParameterStore.GetOverridesPath(detector.ProgramPath);
                ParameterStore.SaveValues(path, GetCurrentParameters());
                SendParametersToDetector();
                ParameterMessage = "已保存到 " + Path.GetFileName(path);
            }
            catch (FormatException ex)
            {
                ParameterMessage = ex.Message;
            }
            catch (IOException ex)
            {
                ParameterMessage = "保存失败：" + ex.Message;
            }
        }

        /// <summary>"恢复默认"：所有参数改回 dd_default_params 的默认值。</summary>
        [RelayCommand]
        private void ResetParameters()
        {
            previewTimer.Stop();
            foreach (ParameterItemViewModel item in Parameters)
            {
                item.ResetToDefault();
            }
            previewTimer.Stop();
            ApplyParameters();
            ParameterMessage = "已恢复默认值（还没保存）";
        }

        // ---------------- 内部方法 ----------------

        /// <summary>加载（或重新加载）HALCON 程序和参数，然后识别当前图片。</summary>
        private void LoadProgram()
        {
            previewTimer.Stop();
            Parameters.Clear();
            ParameterMessage = "";
            defaultParameters.Dispose();
            defaultParameters = new HDict();

            if (!File.Exists(ProgramPath))
            {
                detector.Unload();
                ProgramStatus = "找不到 " + Path.GetFileName(ProgramPath) + "，请检查路径后点“重新加载”。";
            }
            else
            {
                try
                {
                    defaultParameters = detector.Load(ProgramPath);
                    Dictionary<string, JsonElement> savedValues = ParameterStore.LoadSavedValues(ParameterStore.GetOverridesPath(ProgramPath));
                    Dictionary<string, ParameterMeta> metas = ParameterStore.LoadMetas(ParameterStore.GetMetaPath(ProgramPath));
                    foreach (DetectorParameter parameter in ParameterStore.Merge(defaultParameters, savedValues, metas))
                    {
                        ParameterItemViewModel item = new ParameterItemViewModel(parameter);
                        item.ValueChanged += Parameter_ValueChanged;
                        Parameters.Add(item);
                    }
                    SendParametersToDetector();
                    ProgramStatus = "已加载：" + string.Join("、", detector.ProcedureNames);
                    if (Parameters.Count == 0)
                    {
                        ParameterMessage = "程序中没有 dd_default_params，或者没有可调的参数";
                    }
                }
                catch (Exception ex)
                {
                    detector.Unload();
                    ProgramStatus = "加载失败：" + ex.Message;
                }
            }

            CapabilityText = DetectionResult.DescribeLevel(detector.Capability);
            DetectCurrentImage();
        }

        /// <summary>把面板上的参数交给识别器。有参数写得不对时提示并返回 false。</summary>
        private bool SendParametersToDetector()
        {
            try
            {
                HDict dict = ParameterStore.BuildDict(defaultParameters, GetCurrentParameters());
                detector.SetParameters(dict);
                dict.Dispose();
                return true;
            }
            catch (FormatException ex)
            {
                ParameterMessage = ex.Message;
                return false;
            }
        }

        private List<DetectorParameter> GetCurrentParameters()
        {
            List<DetectorParameter> list = new List<DetectorParameter>();
            foreach (ParameterItemViewModel item in Parameters)
            {
                list.Add(item.ToParameter());
            }
            return list;
        }

        /// <summary>丢掉相机图，回到文件夹里的图。</summary>
        private void ForgetCameraImage()
        {
            if (cameraImage != null)
            {
                cameraImage.Dispose();
                cameraImage = null;
                cameraImagePath = null;
            }
        }

        /// <summary>识别当前图片（相机图或文件夹里的图）并显示结果。</summary>
        private void DetectCurrentImage()
        {
            if (cameraImage != null)
            {
                ImageText = "相机图像 " + DateTime.Now.ToString("HH:mm:ss");
                ShowResult(detector.Detect(cameraImage, cameraImagePath));
                return;
            }

            ImageText = images.Description;
            string path = images.CurrentPath;
            if (path == null)
            {
                ShowResult(null);
                return;
            }

            try
            {
                HImage image = ImageFolder.ReadImage(path);
                DetectionResult newResult = detector.Detect(image, path);
                image.Dispose();
                ShowResult(newResult);
            }
            catch (Exception ex)
            {
                ResultMessage = "读取图片失败：" + ex.Message;
                ResultHasError = true;
            }
        }

        private void ShowResult(DetectionResult newResult)
        {
            DetectionResult previous = Result;
            Result = newResult;
            if (previous != null)
            {
                previous.Dispose();   // 新结果已经显示，旧结果可以释放了
            }

            SelectedFeatureIndex = -1;
            if (newResult == null)
            {
                FeatureTable = null;
                CategoryText = "—";
                CircleText = "—";
                CountText = "—";
                TimeText = "—";
                ExpectedText = "—";
                ResultMessage = "";
                ResultHasError = false;
                return;
            }

            FeatureTable = BuildFeatureTable(newResult.Features);
            CountText = "候选 " + newResult.CandidateCount + " · 缺陷 " + newResult.DefectCount;
            TimeText = newResult.ElapsedMs.ToString("0.0") + " ms";
            ResultMessage = newResult.Message;
            ResultHasError = newResult.HasError;

            if (newResult.Level == DetectionLevel.Full && newResult.AlgorithmLabel != null)
            {
                DefectCategory category = DefectCategories.FromAlgorithmLabel(newResult.AlgorithmLabel);
                CategoryText = newResult.AlgorithmLabel + "　→　入库类别 " + DefectCategories.GetName(category);
            }
            else
            {
                CategoryText = "算法未完成（" + DetectionResult.DescribeLevel(newResult.Level) + "）";
            }

            if (newResult.Level == DetectionLevel.None)
            {
                CircleText = "—";
            }
            else if (newResult.Found)
            {
                CircleText = "(" + newResult.Row.ToString("0.0") + ", " + newResult.Column.ToString("0.0") + ")  R = " + newResult.Radius.ToString("0.0");
            }
            else
            {
                CircleText = "未找到";
            }

            string expected = DefectCategories.GetLabelFromFileName(newResult.ImagePath);
            if (expected == null)
            {
                ExpectedText = "—";
            }
            else if (newResult.AlgorithmLabel == null)
            {
                ExpectedText = expected;
            }
            else if (newResult.AlgorithmLabel == expected)
            {
                ExpectedText = expected + "  ✓";
            }
            else
            {
                ExpectedText = expected + "  ✗";
            }
        }

        /// <summary>把特征表转成 DataGrid 能直接显示的 DataView。第一列是序号，列名换成中文。</summary>
        private static DataView BuildFeatureTable(FeatureTable features)
        {
            DataTable table = new DataTable();
            table.Columns.Add("#", typeof(int));
            foreach (string column in features.Columns)
            {
                table.Columns.Add(GetColumnTitle(column), typeof(string));
            }
            for (int i = 0; i < features.Rows.Count; i++)
            {
                DataRow row = table.NewRow();
                row[0] = i + 1;
                for (int j = 0; j < features.Columns.Count; j++)
                {
                    row[j + 1] = features.Rows[i][j];
                }
                table.Rows.Add(row);
            }
            return table.DefaultView;
        }

        /// <summary>
        /// 特征表的中文列名（键名见 docs\上位机\HALCON过程接口约定.md）。
        /// 没列出的键原样显示，但把下划线换成空格：WPF 会把 "_" 当成快捷键标记而不显示它。
        /// </summary>
        private static string GetColumnTitle(string key)
        {
            switch (key)
            {
                case "type":
                    return "类别";
                case "is_main":
                    return "主缺陷";
                case "area":
                    return "面积";
                case "contrast":
                    return "对比度";
                case "edge_gap":
                    return "离边距离";
                case "pocket_area":
                    return "切角面积";
                case "hole_ratio":
                    return "孔比例";
                case "thickness":
                    return "厚度";
                case "length":
                    return "长度";
                case "score":
                    return "得分";
                default:
                    return key.Replace("_", " ");
            }
        }

        /// <summary>参数被修改：重新开始 0.4 秒计时，计时到了才重新识别（避免拖动滑条时反复识别）。</summary>
        private void Parameter_ValueChanged(object sender, EventArgs e)
        {
            if (LivePreview)
            {
                previewTimer.Stop();
                previewTimer.Start();
            }
        }

        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            previewTimer.Stop();
            ApplyParameters();
        }
    }
}
