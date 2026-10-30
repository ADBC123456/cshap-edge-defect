namespace DiscSorter.Core.Data
{
    /// <summary>AppSettings 表里用到的键。</summary>
    public static class SettingKeys
    {
        /// <summary>HALCON 程序路径，默认 工程根目录\halcon\DiscDefect.hdev。</summary>
        public const string ProgramPath = "ProgramPath";

        /// <summary>视觉调试页上次打开的图片文件夹。</summary>
        public const string ImageFolder = "ImageFolder";

        /// <summary>自动运行时回放的图片文件夹（代替相机）。</summary>
        public const string RunImageFolder = "RunImageFolder";

        /// <summary>入库类别来源：Algorithm 或 FileName。</summary>
        public const string CategorySource = "CategorySource";

        /// <summary>仿真 PLC：两件工件之间的间隔（毫秒）。</summary>
        public const string SimArrivalMs = "SimArrivalMs";

        /// <summary>仿真 PLC：取件、搬运、放置的耗时（毫秒）。</summary>
        public const string SimSortMs = "SimSortMs";

        /// <summary>PLC 类型：Simulated（仿真）或 OpcUa（真实 PLC）。重启程序后生效。</summary>
        public const string PlcMode = "PlcMode";

        /// <summary>真实 PLC 的 OPC UA 地址，例如 opc.tcp://192.168.3.99:4840。</summary>
        public const string PlcEndpoint = "PlcEndpoint";

        /// <summary>真实 PLC 的用户名。</summary>
        public const string PlcUser = "PlcUser";

        /// <summary>真实 PLC 的密码（DPAPI 加密后的文本，见 SecretProtector）。</summary>
        public const string PlcPasswordProtected = "PlcPasswordProtected";

        /// <summary>是否用加密连接：True / False。</summary>
        public const string PlcUseSecurity = "PlcUseSecurity";

        /// <summary>自动运行的图像来源：Folder（图片文件夹）或 Camera（海康相机）。</summary>
        public const string ImageSourceKind = "ImageSourceKind";

        /// <summary>上次打开的相机序列号，启动时按它自动打开。</summary>
        public const string CameraSerial = "CameraSerial";

        /// <summary>是否保存相机采到的图片：True / False。</summary>
        public const string CameraSaveImages = "CameraSaveImages";

        /// <summary>相机图片保存目录。</summary>
        public const string CameraSaveFolder = "CameraSaveFolder";
    }
}
