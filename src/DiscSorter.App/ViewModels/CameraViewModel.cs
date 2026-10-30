using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Vision;
using DiscSorter.Core.Workflow;
using HalconDotNet;
using Microsoft.Win32;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 相机页：枚举局域网里的海康相机、打开/关闭、曝光增益、拍一张并识别，
    /// 以及选择自动运行时的图像来源（图片文件夹 / 相机）。
    /// （[ObservableProperty] 和 [RelayCommand] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class CameraViewModel : ObservableObject
    {
        private readonly HikCamera camera;
        private readonly IDefectDetector detector;
        private readonly SettingsService settings;
        private readonly SortingCoordinator coordinator;

        /// <summary>设计器预览用的构造函数：填一些示例数据。</summary>
        public CameraViewModel()
        {
            Cameras = new ObservableCollection<CameraInfo>();
            CameraInfo sample = new CameraInfo();
            sample.Transport = "GigE";
            sample.Model = "MV-CS050-10GC";
            sample.SerialNumber = "DA1234567";
            sample.IpAddress = "192.168.3.20";
            sample.HostIpAddress = "192.168.3.56";
            Cameras.Add(sample);
            selectedCamera = sample;
            enumerateText = "找到 1 台相机";
            isOpen = true;
            cameraStatus = "已打开：GigE  MV-CS050-10GC  SN:DA1234567  192.168.3.20";
            exposureText = "8000";
            gainText = "0";
            useCamera = true;
            saveImages = true;
            saveFolder = @"C:\Users\...\AppData\Local\DiscSorter\Images";
            resultText = "Q　→　入库类别 缺角 Q　·　识别 35.2 ms";
            message = "拍照成功";
        }

        public CameraViewModel(HikCamera camera, IDefectDetector detector, SettingsService settings, SortingCoordinator coordinator,
            string defaultSaveFolder)
        {
            this.camera = camera;
            this.detector = detector;
            this.settings = settings;
            this.coordinator = coordinator;
            Cameras = new ObservableCollection<CameraInfo>();

            SaveFolder = settings.Get(SettingKeys.CameraSaveFolder, defaultSaveFolder);
            SaveImages = settings.Get(SettingKeys.CameraSaveImages, "True") == "True";
            UseCamera = settings.Get(SettingKeys.ImageSourceKind, "Folder") == "Camera";

            // 上次用过相机：启动时按序列号自动打开
            string serial = settings.Get(SettingKeys.CameraSerial, "");
            if (serial != "")
            {
                Enumerate();
                foreach (CameraInfo info in Cameras)
                {
                    if (info.SerialNumber == serial)
                    {
                        SelectedCamera = info;
                        Open();
                    }
                }
                if (!IsOpen)
                {
                    Message = "上次使用的相机（SN:" + serial + "）没有找到，请检查后重新枚举";
                }
            }
        }

        /// <summary>枚举到的相机。</summary>
        public ObservableCollection<CameraInfo> Cameras { get; private set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
        private CameraInfo selectedCamera;

        [ObservableProperty]
        private string enumerateText = "还没有枚举";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
        [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
        [NotifyCanExecuteChangedFor(nameof(GrabAndDetectCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyParametersCommand))]
        private bool isOpen;

        [ObservableProperty]
        private string cameraStatus = "未打开";

        /// <summary>曝光时间（微秒），文本框里的文字。</summary>
        [ObservableProperty]
        private string exposureText = "";

        /// <summary>增益（dB），文本框里的文字。</summary>
        [ObservableProperty]
        private string gainText = "";

        /// <summary>自动运行时的图像来源：true 相机，false 图片文件夹。</summary>
        [ObservableProperty]
        private bool useCamera;

        [ObservableProperty]
        private bool saveImages;

        [ObservableProperty]
        private string saveFolder;

        /// <summary>自动运行时锁定本页（相机正被分拣流程使用）。</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GrabAndDetectCommand))]
        [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
        private bool isLocked;

        /// <summary>"拍一张并识别"的结果（图像控件绑定它）。</summary>
        [ObservableProperty]
        private DetectionResult result;

        [ObservableProperty]
        private string resultText = "—";

        [ObservableProperty]
        private string message = "";

        // ---------------- 按钮命令 ----------------

        /// <summary>枚举局域网里的网口相机和本机的 USB 相机。</summary>
        [RelayCommand]
        private void Enumerate()
        {
            Cameras.Clear();
            try
            {
                foreach (CameraInfo info in HikCamera.Enumerate())
                {
                    Cameras.Add(info);
                }
                EnumerateText = "找到 " + Cameras.Count + " 台相机";
                if (Cameras.Count == 0)
                {
                    Message = "没有找到相机：检查相机电源、网线，以及相机 IP 和本机网卡是否在同一网段（可以先用 MVS 客户端看看）";
                }
                else
                {
                    Message = "";
                    SelectedCamera = Cameras[0];
                }
            }
            catch (Exception ex)
            {
                // 没装 MVS 时这里会报找不到 MvCameraControl.dll
                EnumerateText = "枚举失败";
                Message = "枚举失败：" + ex.Message + "（需要安装海康 MVS 客户端或 Runtime）";
            }
        }

        [RelayCommand(CanExecute = nameof(CanOpen))]
        private void Open()
        {
            try
            {
                camera.Open(SelectedCamera);
                IsOpen = true;
                CameraStatus = "已打开：" + camera.OpenedCamera.DisplayText;
                settings.Set(SettingKeys.CameraSerial, SelectedCamera.SerialNumber);
                ReadParameters();
                ApplySaveSettings();
                Message = "相机已打开（软触发模式：上位机要图时才拍）";
            }
            catch (Exception ex)
            {
                IsOpen = false;
                CameraStatus = "未打开";
                Message = "打开失败：" + ex.Message;
            }
            UpdateImageSource();
        }

        [RelayCommand(CanExecute = nameof(CanClose))]
        private void Close()
        {
            camera.Close();
            IsOpen = false;
            CameraStatus = "未打开";
            Message = "相机已关闭";
            UpdateImageSource();
        }

        /// <summary>把文本框里的曝光和增益写给相机。</summary>
        [RelayCommand(CanExecute = nameof(CanApply))]
        private void ApplyParameters()
        {
            double exposure;
            double gain;
            if (!double.TryParse(ExposureText, out exposure) || !double.TryParse(GainText, out gain))
            {
                Message = "曝光和增益要填数字";
                return;
            }
            try
            {
                camera.SetExposureUs(exposure);
                camera.SetGainDb(gain);
                ReadParameters();
                Message = "已设置曝光和增益（保存在相机里，断电前一直有效）";
            }
            catch (Exception ex)
            {
                Message = "设置失败：" + ex.Message;
            }
        }

        /// <summary>拍一张并用当前算法识别，结果显示在右边。</summary>
        [RelayCommand(CanExecute = nameof(CanGrab))]
        private void GrabAndDetect()
        {
            try
            {
                string path;
                HImage image = camera.Grab(out path);
                DetectionResult newResult = detector.Detect(image, path);
                image.Dispose();
                ShowResult(newResult);
                if (path == null)
                {
                    Message = "拍照成功（没有保存图片）";
                }
                else
                {
                    Message = "拍照成功，已保存：" + path;
                }
            }
            catch (Exception ex)
            {
                Message = "拍照失败：" + ex.Message;
            }
        }

        [RelayCommand]
        private void ChooseSaveFolder()
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            dialog.Title = "选择相机图片的保存目录";
            if (dialog.ShowDialog() != true)
            {
                return;
            }
            SaveFolder = dialog.FolderName;
        }

        private bool CanOpen()
        {
            return SelectedCamera != null && !IsOpen;
        }

        private bool CanClose()
        {
            return IsOpen && !IsLocked;
        }

        private bool CanApply()
        {
            return IsOpen;
        }

        private bool CanGrab()
        {
            return IsOpen && !IsLocked;
        }

        // ---------------- 内部方法 ----------------

        private void ReadParameters()
        {
            try
            {
                ExposureText = camera.GetExposureUs().ToString("0");
                GainText = camera.GetGainDb().ToString("0.0");
            }
            catch (Exception ex)
            {
                Message = "读取曝光和增益失败：" + ex.Message;
            }
        }

        private void ApplySaveSettings()
        {
            if (camera == null)
            {
                return;   // 设计器预览
            }
            camera.SaveImages = SaveImages;
            camera.SaveFolder = SaveFolder;
        }

        /// <summary>按"图像来源"的选择，告诉分拣协调器从哪里取图。相机没打开时退回图片文件夹。</summary>
        private void UpdateImageSource()
        {
            if (coordinator == null)
            {
                return;   // 设计器预览
            }
            if (UseCamera && camera.IsOpen)
            {
                coordinator.ImageSource = camera;
            }
            else
            {
                coordinator.ImageSource = null;   // null 表示回到图片文件夹
            }
        }

        private void ShowResult(DetectionResult newResult)
        {
            DetectionResult previous = Result;
            Result = newResult;
            if (previous != null)
            {
                previous.Dispose();
            }

            string text;
            if (newResult.HasError)
            {
                text = "识别出错：" + newResult.Message;
            }
            else if (newResult.Level == DetectionLevel.Full && newResult.AlgorithmLabel != null)
            {
                DefectCategory category = DefectCategories.FromAlgorithmLabel(newResult.AlgorithmLabel);
                text = newResult.AlgorithmLabel + "　→　入库类别 " + DefectCategories.GetName(category);
            }
            else
            {
                text = "算法未完成（" + DetectionResult.DescribeLevel(newResult.Level) + "）";
            }
            ResultText = text + "　·　识别 " + newResult.ElapsedMs.ToString("0.0") + " ms";
        }

        // 下面这些方法由 [ObservableProperty] 自动调用：属性值改变后执行。

        partial void OnUseCameraChanged(bool value)
        {
            if (settings == null)
            {
                return;   // 设计器预览
            }
            settings.Set(SettingKeys.ImageSourceKind, value ? "Camera" : "Folder");
            UpdateImageSource();
        }

        partial void OnSaveImagesChanged(bool value)
        {
            if (settings == null)
            {
                return;
            }
            settings.Set(SettingKeys.CameraSaveImages, value ? "True" : "False");
            ApplySaveSettings();
        }

        partial void OnSaveFolderChanged(string value)
        {
            if (settings == null)
            {
                return;
            }
            settings.Set(SettingKeys.CameraSaveFolder, value);
            ApplySaveSettings();
        }
    }
}
