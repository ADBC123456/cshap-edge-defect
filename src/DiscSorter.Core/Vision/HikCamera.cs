using System;
using System.Collections.Generic;
using System.IO;
using HalconDotNet;
using MvCameraControl;

namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 海康工业相机（MVS SDK）。用法：
    ///   1. Enumerate() 找出局域网里的网口相机和 USB 相机；
    ///   2. Open(相机) 打开，设成"软触发"模式：平时不出图，上位机要图时才拍；
    ///   3. Grab() 发一次软触发，等这一帧到达，转成 HALCON 图像；
    ///   4. Close() 关闭。
    /// 全部是同步调用：Grab() 会等到图像到达（最多 TimeoutMs 毫秒）才返回。
    ///
    /// 和旧的 Python 程序（hik_camera.py）用的是同一套 SDK，只是换成了 .NET 版本。
    /// </summary>
    public class HikCamera : IImageSource
    {
        private static bool sdkInitialized;

        private IDevice device;
        private CameraInfo openedCamera;

        public HikCamera()
        {
            TimeoutMs = 2000;
        }

        /// <summary>等一帧图像的最长时间（毫秒）。</summary>
        public int TimeoutMs { get; set; }

        /// <summary>取图后是否把图片保存下来（积累数据集用）。</summary>
        public bool SaveImages { get; set; }

        /// <summary>图片保存目录，会按日期再建子文件夹。</summary>
        public string SaveFolder { get; set; }

        /// <summary>当前打开的相机，没打开时为 null。</summary>
        public CameraInfo OpenedCamera
        {
            get { return openedCamera; }
        }

        public bool IsOpen
        {
            get { return device != null; }
        }

        // ==================== SDK 初始化 ====================

        /// <summary>初始化 SDK（整个程序只需要一次，Enumerate 和 Open 会自动调用）。</summary>
        public static void InitializeSdk()
        {
            if (!sdkInitialized)
            {
                SDKSystem.Initialize();
                sdkInitialized = true;
            }
        }

        /// <summary>释放 SDK（程序退出时调用）。</summary>
        public static void FinalizeSdk()
        {
            if (sdkInitialized)
            {
                SDKSystem.Finalize();
                sdkInitialized = false;
            }
        }

        // ==================== 枚举 ====================

        /// <summary>枚举局域网里的海康网口相机和本机的 USB 相机。</summary>
        public static List<CameraInfo> Enumerate()
        {
            List<IDeviceInfo> devices = EnumerateDevices();
            List<CameraInfo> cameras = new List<CameraInfo>();
            foreach (IDeviceInfo device in devices)
            {
                cameras.Add(ToCameraInfo(device));
            }
            return cameras;
        }

        private static List<IDeviceInfo> EnumerateDevices()
        {
            InitializeSdk();
            List<IDeviceInfo> devices;
            int result = DeviceEnumerator.EnumDevices(DeviceTLayerType.MvGigEDevice | DeviceTLayerType.MvUsbDevice, out devices);
            CheckResult(result, "枚举相机");
            return devices;
        }

        private static CameraInfo ToCameraInfo(IDeviceInfo device)
        {
            CameraInfo info = new CameraInfo();
            info.Manufacturer = device.ManufacturerName;
            info.Model = device.ModelName;
            info.SerialNumber = device.SerialNumber;
            info.UserDefinedName = device.UserDefinedName;
            if (device.TLayerType == DeviceTLayerType.MvGigEDevice)
            {
                info.Transport = "GigE";
                IGigEDeviceInfo gigE = device as IGigEDeviceInfo;
                if (gigE != null)
                {
                    info.IpAddress = FormatIp(gigE.CurrentIp);
                    info.HostIpAddress = FormatIp(gigE.HostIP);
                }
            }
            else
            {
                info.Transport = "USB";
            }
            return info;
        }

        /// <summary>SDK 里 IP 是一个 32 位整数，例如 0xC0A80314 → "192.168.3.20"。</summary>
        private static string FormatIp(uint ip)
        {
            if (ip == 0)
            {
                return "";
            }
            return ((ip >> 24) & 0xFF) + "." + ((ip >> 16) & 0xFF) + "." + ((ip >> 8) & 0xFF) + "." + (ip & 0xFF);
        }

        // ==================== 打开 / 关闭 ====================

        /// <summary>按序列号打开相机，并设成软触发模式、开始取流。</summary>
        public void Open(CameraInfo camera)
        {
            Close();

            // 重新枚举一次，按序列号找到这台相机（下拉框里的信息可能是很久以前枚举的）
            IDeviceInfo found = null;
            foreach (IDeviceInfo deviceInfo in EnumerateDevices())
            {
                if (deviceInfo.SerialNumber == camera.SerialNumber)
                {
                    found = deviceInfo;
                }
            }
            if (found == null)
            {
                throw new InvalidOperationException("找不到序列号为 " + camera.SerialNumber + " 的相机，请检查网线和电源后重新枚举");
            }

            IDevice newDevice = DeviceFactory.CreateDevice(found);
            int result = newDevice.Open();
            if (result != MvError.MV_OK)
            {
                newDevice.Dispose();
                CheckResult(result, "打开相机（可能被其他程序占用，例如 MVS 客户端或 EPC 上的旧容器）");
            }

            // 网口相机：设成网络允许的最大包，减少丢包
            IGigEDevice gigEDevice = newDevice as IGigEDevice;
            if (gigEDevice != null)
            {
                int packetSize;
                if (gigEDevice.GetOptimalPacketSize(out packetSize) == MvError.MV_OK && packetSize > 0)
                {
                    newDevice.Parameters.SetIntValue("GevSCPSPacketSize", packetSize);
                }
            }

            // 软触发：相机平时不出图，上位机发"TriggerSoftware"命令时拍一张
            newDevice.Parameters.SetEnumValueByString("AcquisitionMode", "Continuous");
            newDevice.Parameters.SetEnumValueByString("TriggerMode", "On");
            newDevice.Parameters.SetEnumValueByString("TriggerSource", "Software");

            result = newDevice.StreamGrabber.StartGrabbing();
            if (result != MvError.MV_OK)
            {
                newDevice.Close();
                newDevice.Dispose();
                CheckResult(result, "开始取流");
            }

            device = newDevice;
            openedCamera = ToCameraInfo(found);
        }

        /// <summary>停止取流并关闭相机。没打开时什么也不做。</summary>
        public void Close()
        {
            if (device != null)
            {
                device.StreamGrabber.StopGrabbing();
                device.Close();
                device.Dispose();
                device = null;
                openedCamera = null;
            }
        }

        // ==================== 取图 ====================

        public string SourceName
        {
            get
            {
                if (openedCamera == null)
                {
                    return "海康相机（未打开）";
                }
                return "海康相机 " + openedCamera.Model + " SN:" + openedCamera.SerialNumber;
            }
        }

        public bool IsReady
        {
            get { return IsOpen; }
        }

        /// <summary>拍一张：清掉旧图 → 软触发 → 等这一帧 → 转成 HALCON 图像（→ 按需保存）。</summary>
        public HImage Grab(out string imagePath)
        {
            imagePath = null;
            if (device == null)
            {
                throw new InvalidOperationException("相机没有打开");
            }

            device.StreamGrabber.ClearImageBuffer();
            CheckResult(device.Parameters.SetCommandValue("TriggerSoftware"), "软触发");

            IFrameOut frame;
            CheckResult(device.StreamGrabber.GetImageBuffer((uint)TimeoutMs, out frame), "等待图像");
            HImage image;
            try
            {
                image = ToHImage(frame.Image);
            }
            finally
            {
                device.StreamGrabber.FreeImageBuffer(frame);
            }

            if (SaveImages && !string.IsNullOrEmpty(SaveFolder))
            {
                imagePath = SaveImage(image);
            }
            return image;
        }

        /// <summary>
        /// 相机图像转成 HALCON 图像：黑白图直接用；彩色图（Bayer、RGB 等）先用 SDK 转成 RGB8，
        /// 再交给 HALCON 的 GenImageInterleaved（R、G、B 交错排列的数据 → 三通道图像）。两种方式都会复制一份数据。
        /// </summary>
        private HImage ToHImage(IImage source)
        {
            HImage image = new HImage();
            int width = (int)source.Width;
            int height = (int)source.Height;
            if (source.PixelType == MvGvspPixelType.PixelType_Gvsp_Mono8)
            {
                image.GenImage1("byte", width, height, source.PixelDataPtr);
                return image;
            }

            IImage rgb;
            CheckResult(device.PixelTypeConverter.ConvertPixelType(source, out rgb, MvGvspPixelType.PixelType_Gvsp_RGB8_Packed), "转换像素格式");
            image.GenImageInterleaved(rgb.PixelDataPtr, "rgb", width, height, -1, "byte", width, height, 0, 0, -1, 0);
            return image;
        }

        /// <summary>保存到 SaveFolder\日期\时分秒_毫秒.png，返回文件路径。</summary>
        private string SaveImage(HImage image)
        {
            DateTime now = DateTime.Now;
            string folder = Path.Combine(SaveFolder, now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, now.ToString("HHmmss_fff") + ".png");
            image.WriteImage("png", 0, path);
            return path;
        }

        // ==================== 曝光和增益 ====================

        /// <summary>读当前曝光时间（微秒）。</summary>
        public double GetExposureUs()
        {
            return GetFloat("ExposureTime");
        }

        /// <summary>读当前增益（dB）。</summary>
        public double GetGainDb()
        {
            return GetFloat("Gain");
        }

        /// <summary>设置曝光时间（微秒），同时关闭自动曝光。</summary>
        public void SetExposureUs(double value)
        {
            RequireOpen();
            device.Parameters.SetEnumValue("ExposureAuto", 0);
            CheckResult(device.Parameters.SetFloatValue("ExposureTime", (float)value), "设置曝光时间");
        }

        /// <summary>设置增益（dB），同时关闭自动增益。</summary>
        public void SetGainDb(double value)
        {
            RequireOpen();
            device.Parameters.SetEnumValue("GainAuto", 0);
            CheckResult(device.Parameters.SetFloatValue("Gain", (float)value), "设置增益");
        }

        private double GetFloat(string name)
        {
            RequireOpen();
            IFloatValue value;
            CheckResult(device.Parameters.GetFloatValue(name, out value), "读取 " + name);
            return value.CurValue;
        }

        private void RequireOpen()
        {
            if (device == null)
            {
                throw new InvalidOperationException("相机没有打开");
            }
        }

        /// <summary>SDK 的返回值不是 MV_OK 就抛出异常，消息里带上十六进制错误码，方便查 SDK 手册。</summary>
        private static void CheckResult(int result, string action)
        {
            if (result != MvError.MV_OK)
            {
                throw new InvalidOperationException(action + "失败，错误码 0x" + result.ToString("X8"));
            }
        }
    }
}
