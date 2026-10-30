namespace DiscSorter.Core.Plc
{
    /// <summary>连接真实 PLC（OPC UA）需要的参数。</summary>
    public class OpcUaOptions
    {
        public OpcUaOptions()
        {
            Endpoint = "opc.tcp://192.168.3.99:4840";
            UseSecurity = true;
            TimeoutMs = 3000;
        }

        /// <summary>OPC UA 服务器地址，PLCnext 默认端口 4840。</summary>
        public string Endpoint { get; set; }

        /// <summary>PLC 用户名（WBM 里的用户）。为空表示匿名登录。</summary>
        public string User { get; set; }

        /// <summary>PLC 密码。只在内存里用，保存到数据库时是加密的（见 SecretProtector）。</summary>
        public string Password { get; set; }

        /// <summary>是否用加密连接（PLCnext 默认要求加密）。</summary>
        public bool UseSecurity { get; set; }

        /// <summary>连接和每次读写的超时（毫秒）。</summary>
        public int TimeoutMs { get; set; }

        /// <summary>证书存放目录（第一次连接时自动生成上位机自己的证书）。</summary>
        public string CertificateFolder { get; set; }
    }
}
