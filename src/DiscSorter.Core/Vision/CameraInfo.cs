namespace DiscSorter.Core.Vision
{
    /// <summary>枚举到的一台相机（显示在"相机"页的下拉框里）。</summary>
    public class CameraInfo
    {
        /// <summary>接口类型："GigE"（网口）或 "USB"。</summary>
        public string Transport { get; set; }

        /// <summary>厂家，例如 "Hikrobot"。</summary>
        public string Manufacturer { get; set; }

        /// <summary>型号，例如 "MV-CS050-10GC"。</summary>
        public string Model { get; set; }

        /// <summary>序列号，用它来区分同型号的多台相机，下次启动按序列号自动打开。</summary>
        public string SerialNumber { get; set; }

        /// <summary>用户在 MVS 里给相机起的名字（可能为空）。</summary>
        public string UserDefinedName { get; set; }

        /// <summary>相机 IP（只有网口相机有）。</summary>
        public string IpAddress { get; set; }

        /// <summary>本机连接这台相机的网卡 IP（只有网口相机有）。相机 IP 要和它在同一网段。</summary>
        public string HostIpAddress { get; set; }

        /// <summary>下拉框里显示的文字，例如 "GigE  MV-CS050-10GC  SN:DA1234567  192.168.3.20"。</summary>
        public string DisplayText
        {
            get
            {
                string text = Transport + "  " + Model;
                if (!string.IsNullOrEmpty(UserDefinedName))
                {
                    text = text + "（" + UserDefinedName + "）";
                }
                text = text + "  SN:" + SerialNumber;
                if (!string.IsNullOrEmpty(IpAddress))
                {
                    text = text + "  " + IpAddress;
                }
                return text;
            }
        }
    }
}
