using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace DiscSorter.Core.Plc
{
    /// <summary>
    /// 真实 PLC（菲尼克斯 PLCnext，EPC 1502）：通过 OPC UA 读写 PlcTags 里的 5 个变量。
    /// 握手逻辑在 StationPlcBase 里，这里只负责"连接、读、写"。
    ///
    /// 使用前 PLC 工程要做的设置见 plc\PLC程序修改说明.md：
    ///   新增 PC_Category、PC_TargetSlot，并给这 5 个变量勾选 OPC 属性；PLC 要信任上位机的证书。
    ///
    /// 只有点"连接"按钮时才会去连接；读写失败就断开，等再点"连接"。
    /// 这样 PLC 不在线时界面不会反复卡住。
    ///
    /// 关于异步：OPC UA 库只提供异步方法（xxxAsync）。这里的做法是把每次调用放到后台线程
    /// （Task.Run）里执行，界面线程等它做完再继续。所以对程序其余部分来说，读写仍然是普通的同步调用，
    /// 而且不会出现"界面线程等异步方法"导致的死锁。一次读写在局域网里只要几毫秒。
    /// </summary>
    public class OpcUaPlc : StationPlcBase
    {
        private readonly OpcUaOptions options;
        private Session session;

        // 交给后台线程的读写内容（后台线程执行 ReadNodes / WriteNodes 时使用）
        private ReadValueIdCollection nodesToRead;
        private WriteValueCollection nodesToWrite;
        private ushort namespaceIndex;
        private NodeId captureRequestNode;
        private NodeId stepNode;
        private NodeId categoryNode;
        private NodeId targetSlotNode;
        private NodeId matStateNode;

        public OpcUaPlc(OpcUaOptions options)
        {
            this.options = options;
            Online = false;
        }

        public override string Name
        {
            get { return "PLCnext（OPC UA）"; }
        }

        public override bool IsSimulated
        {
            get { return false; }
        }

        /// <summary>连接参数（"PLC 通讯"页修改后，下次连接时生效）。</summary>
        public OpcUaOptions Options
        {
            get { return options; }
        }

        /// <summary>是否已连接。</summary>
        public bool IsConnected
        {
            get { return session != null && session.Connected; }
        }

        /// <summary>最近一次连接或读写失败的原因。</summary>
        public string LastError { get; private set; }

        protected override bool IsRunning
        {
            get { return Online && IsConnected; }
        }

        // ==================== 连接 / 断开 ====================

        /// <summary>连接 PLC。成功返回 true；失败返回 false，原因在 LastError。</summary>
        public bool Connect()
        {
            Disconnect();
            try
            {
                // 在后台线程里建立会话，界面线程等它结束（最多等 Options.TimeoutMs 左右）
                Task<Session> task = Task.Run(new Func<Session>(CreateSession));
                session = task.GetAwaiter().GetResult();

                namespaceIndex = FindNamespaceIndex(session);
                captureRequestNode = new NodeId(PlcTags.CaptureRequest, namespaceIndex);
                stepNode = new NodeId(PlcTags.Step, namespaceIndex);
                categoryNode = new NodeId(PlcTags.Category, namespaceIndex);
                targetSlotNode = new NodeId(PlcTags.TargetSlot, namespaceIndex);
                matStateNode = new NodeId(PlcTags.MatState, namespaceIndex);

                LastError = null;
                AddLog(PlcLogEntry.Host, "连接", options.Endpoint, "已连接，命名空间 ns=" + namespaceIndex);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddLog(PlcLogEntry.Host, "连接", options.Endpoint, "连接失败：" + ex.Message);
                Disconnect();
                return false;
            }
        }

        /// <summary>断开连接。</summary>
        public void Disconnect()
        {
            if (session != null)
            {
                try
                {
                    Task task = Task.Run(new Action(CloseSession));
                    task.GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    // 断开时出错不影响程序继续运行
                }
                session.Dispose();
                session = null;
            }
        }

        // ==================== 以下方法在后台线程里执行 ====================

        /// <summary>准备证书、选择端点、建立会话。</summary>
        private Session CreateSession()
        {
            // OPC UA 库要求传一个"日志上下文"。这里不需要它的日志，配置方法什么也不做
            ITelemetryContext telemetry = DefaultTelemetry.Create(new Action<ILoggingBuilder>(ConfigureNoLogging));

            ApplicationConfiguration configuration = CreateConfiguration();
            configuration.ValidateAsync(ApplicationType.Client, CancellationToken.None).GetAwaiter().GetResult();
            configuration.CertificateValidator.CertificateValidation += CertificateValidator_CertificateValidation;

            // 第一次运行时自动生成上位机自己的证书（存在 CertificateFolder\own 里）
            ApplicationInstance application = new ApplicationInstance(configuration, telemetry);
            application.ApplicationName = "DiscSorter";
            application.ApplicationType = ApplicationType.Client;
            application.CheckApplicationInstanceCertificatesAsync(false, null, CancellationToken.None).GetAwaiter().GetResult();

            EndpointDescription endpointDescription = CoreClientUtils.SelectEndpointAsync(
                configuration, options.Endpoint, options.UseSecurity, options.TimeoutMs, telemetry, CancellationToken.None).GetAwaiter().GetResult();
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(configuration);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            IUserIdentity identity;
            if (string.IsNullOrEmpty(options.User))
            {
                identity = new UserIdentity(new AnonymousIdentityToken());
            }
            else
            {
                byte[] password = Encoding.UTF8.GetBytes(options.Password == null ? "" : options.Password);
                identity = new UserIdentity(options.User, password);
            }

            DefaultSessionFactory factory = new DefaultSessionFactory(telemetry);
            ISession created = factory.CreateAsync(configuration, endpoint, false, "DiscSorter", 60000, identity, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            return (Session)created;
        }

        private static void ConfigureNoLogging(ILoggingBuilder builder)
        {
        }

        private void CloseSession()
        {
            session.CloseAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private DataValueCollection ReadNodes()
        {
            ReadResponse response = session.ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, CancellationToken.None)
                .GetAwaiter().GetResult();
            return response.Results;
        }

        private StatusCodeCollection WriteNodes()
        {
            WriteResponse response = session.WriteAsync(null, nodesToWrite, CancellationToken.None).GetAwaiter().GetResult();
            return response.Results;
        }

        // ==================== 以上方法在后台线程里执行 ====================

        /// <summary>OPC UA 客户端配置：应用名、证书目录、超时。</summary>
        private ApplicationConfiguration CreateConfiguration()
        {
            string folder = options.CertificateFolder;
            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscSorter", "pki");
            }

            ApplicationConfiguration configuration = new ApplicationConfiguration();
            configuration.ApplicationName = "DiscSorter";
            configuration.ApplicationUri = "urn:" + System.Net.Dns.GetHostName() + ":DiscSorter";
            configuration.ApplicationType = ApplicationType.Client;

            SecurityConfiguration security = new SecurityConfiguration();
            security.ApplicationCertificate = new CertificateIdentifier();
            security.ApplicationCertificate.StoreType = CertificateStoreType.Directory;
            security.ApplicationCertificate.StorePath = Path.Combine(folder, "own");
            security.ApplicationCertificate.SubjectName = "CN=DiscSorter, O=Graduation Project";
            security.TrustedPeerCertificates = CreateTrustList(Path.Combine(folder, "trusted"));
            security.TrustedIssuerCertificates = CreateTrustList(Path.Combine(folder, "issuer"));
            security.RejectedCertificateStore = CreateTrustList(Path.Combine(folder, "rejected"));
            // 实验室内网：自动信任 PLC 的证书
            security.AutoAcceptUntrustedCertificates = true;
            configuration.SecurityConfiguration = security;

            configuration.TransportQuotas = new TransportQuotas();
            configuration.TransportQuotas.OperationTimeout = options.TimeoutMs;
            configuration.ClientConfiguration = new ClientConfiguration();
            configuration.ClientConfiguration.DefaultSessionTimeout = 60000;
            return configuration;
        }

        private static CertificateTrustList CreateTrustList(string path)
        {
            CertificateTrustList list = new CertificateTrustList();
            list.StoreType = CertificateStoreType.Directory;
            list.StorePath = path;
            return list;
        }

        /// <summary>PLC 的证书不在信任列表里时：实验室内网直接接受。</summary>
        private void CertificateValidator_CertificateValidation(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = true;
            }
        }

        /// <summary>查 PLCnext 全局数据空间的命名空间号（ns）。</summary>
        private static ushort FindNamespaceIndex(Session session)
        {
            int index = session.NamespaceUris.GetIndex(PlcTags.NamespaceUri);
            if (index >= 0)
            {
                return (ushort)index;
            }
            // 不同固件版本的 URI 大小写可能不同：找名字里带 GlobalDataSpace 的
            string[] uris = session.NamespaceUris.ToArray();
            for (int i = 0; i < uris.Length; i++)
            {
                if (uris[i].IndexOf("GlobalDataSpace", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return (ushort)i;
                }
            }
            throw new InvalidOperationException("PLC 的 OPC UA 服务器里没有 PLCnext 全局数据空间（" + PlcTags.NamespaceUri + "）");
        }

        // ==================== 读写变量 ====================

        protected override bool ReadVariables(DateTime now, PlcVariables target)
        {
            if (!IsConnected)
            {
                return false;
            }
            try
            {
                // 一次读 5 个变量
                nodesToRead = new ReadValueIdCollection();
                nodesToRead.Add(CreateReadId(captureRequestNode));
                nodesToRead.Add(CreateReadId(stepNode));
                nodesToRead.Add(CreateReadId(categoryNode));
                nodesToRead.Add(CreateReadId(targetSlotNode));
                nodesToRead.Add(CreateReadId(matStateNode));
                Task<DataValueCollection> task = Task.Run(new Func<DataValueCollection>(ReadNodes));
                DataValueCollection results = task.GetAwaiter().GetResult();

                target.CaptureRequest = Convert.ToBoolean(GetValue(results[0], PlcTags.CaptureRequest));
                target.Step = Convert.ToInt32(GetValue(results[1], PlcTags.Step));
                target.Category = Convert.ToInt32(GetValue(results[2], PlcTags.Category));
                target.TargetSlot = Convert.ToInt32(GetValue(results[3], PlcTags.TargetSlot));

                // MatState 是 ARRAY[0..12] OF INT，读回来是 short 数组
                short[] matState = (short[])GetValue(results[4], PlcTags.MatState);
                for (int slot = 0; slot <= PlcVariables.SlotCount && slot < matState.Length; slot++)
                {
                    target.MatState[slot] = matState[slot];
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddLog(PlcLogEntry.Host, "读取", "失败", ex.Message + "；已断开，请检查后重新连接");
                Disconnect();
                return false;
            }
        }

        protected override bool WriteInt(string path, int value)
        {
            NodeId node = new NodeId(path, namespaceIndex);
            // PLC 的 INT 是 16 位整数，OPC UA 里对应 Int16，必须用 short 写
            return WriteValue(node, new Variant((short)value), null);
        }

        protected override bool WriteMatState(int slotNo, int value)
        {
            // 只写数组里的一个元素：IndexRange = 下标，值是只有一个元素的数组
            short[] element = new short[1];
            element[0] = (short)value;
            return WriteValue(matStateNode, new Variant(element), slotNo.ToString());
        }

        private bool WriteValue(NodeId node, Variant value, string indexRange)
        {
            if (!IsConnected)
            {
                return false;
            }
            try
            {
                WriteValue item = new WriteValue();
                item.NodeId = node;
                item.AttributeId = Attributes.Value;
                item.IndexRange = indexRange;
                item.Value = new DataValue(value);
                nodesToWrite = new WriteValueCollection();
                nodesToWrite.Add(item);
                Task<StatusCodeCollection> task = Task.Run(new Func<StatusCodeCollection>(WriteNodes));
                StatusCodeCollection results = task.GetAwaiter().GetResult();
                if (StatusCode.IsBad(results[0]))
                {
                    LastError = "写 " + node + " 失败：" + results[0];
                    AddLog(PlcLogEntry.Host, "写入", "失败", LastError);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddLog(PlcLogEntry.Host, "写入", "失败", ex.Message + "；已断开，请检查后重新连接");
                Disconnect();
                return false;
            }
        }

        private static ReadValueId CreateReadId(NodeId node)
        {
            ReadValueId id = new ReadValueId();
            id.NodeId = node;
            id.AttributeId = Attributes.Value;
            return id;
        }

        /// <summary>取读到的值；状态不好（变量不存在、没勾 OPC 等）时抛出异常并说明是哪个变量。</summary>
        private static object GetValue(DataValue dataValue, string path)
        {
            if (StatusCode.IsBad(dataValue.StatusCode))
            {
                throw new InvalidOperationException("读 " + path + " 失败：" + dataValue.StatusCode
                    + "（检查变量名、程序实例名，以及是否勾选了 OPC）");
            }
            return dataValue.Value;
        }

        // ==================== 联机 / 脱机 ====================

        /// <summary>上位机联机：开始响应 PLC 的拍照请求。</summary>
        public override void Start()
        {
            Online = true;
            AddLog(PlcLogEntry.Host, "联机", "", "开始响应 PLC 的拍照请求（PLC 的启停由现场按钮控制）");
        }

        /// <summary>上位机脱机：不再响应拍照请求。</summary>
        public override void Stop()
        {
            Online = false;
            AddLog(PlcLogEntry.Host, "脱机", "", "不再响应拍照请求");
        }

        /// <summary>真实 PLC 没有"单件"模式，和联机一样。</summary>
        public override void RunSingle()
        {
            Start();
        }
    }
}
