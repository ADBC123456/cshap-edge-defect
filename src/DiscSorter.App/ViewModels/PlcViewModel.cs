using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscSorter.Core.Common;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Plc;
using DiscSorter.Core.Workflow;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// "PLC 通讯"页：
    ///   1. 连接方式（仿真 / 真实 PLC）和真实 PLC 的连接参数；
    ///   2. 变量表：和 PLC 交换的 5 个变量的当前值；
    ///   3. 库位对照：上位机数据库和 PLC 的 MatState 是否一致；
    ///   4. 通讯日志：每一次变量变化和每一次写入，按时间倒序。
    /// （[ObservableProperty] 和 [RelayCommand] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class PlcViewModel : ObservableObject
    {
        /// <summary>日志表最多显示的条数。</summary>
        private const int MaxLogRows = 500;

        private readonly IPlcClient plc;
        private readonly OpcUaPlc opcUaPlc;
        private readonly SettingsService settings;
        private readonly SortingCoordinator coordinator;
        private readonly WarehouseService warehouse;

        // 数据库里各库位的状态（仓储变化时重新读，不必每 50 ms 查一次数据库）
        private List<StorageSlot> hostSlots = new List<StorageSlot>();

        /// <summary>设计器预览用的构造函数：填一些示例数据。</summary>
        public PlcViewModel()
        {
            CreateRows();
            useOpcUa = false;
            modeText = "当前使用：仿真 PLC（按修改后的 CHAIN.st 步序模拟）";
            endpoint = "opc.tcp://192.168.3.99:4840";
            user = "admin";
            useSecurity = true;
            connectionText = "仿真 PLC 不需要连接";
            Variables[0].Value = "1";
            Variables[1].Value = "7（等待识别结果）";
            Variables[2].Value = "0";
            Variables[3].Value = "0";
            Variables[4].Value = "[1]=1 [4]=4";
            SlotRows[0].HostText = "已占用 Q";
            SlotRows[0].PlcText = "1（有料）";
            SlotRows[0].ResultText = "一致";
            SlotRows[1].HostText = "空闲";
            SlotRows[1].PlcText = "2（有料）";
            SlotRows[1].ResultText = "不一致";
            SlotRows[1].IsMismatch = true;
            mismatchText = "1 个库位不一致";

            PlcLogEntry entry = new PlcLogEntry();
            entry.Time = DateTime.Now;
            entry.Direction = PlcLogEntry.ToPlc;
            entry.Variable = "PC_Category";
            entry.Value = "2";
            entry.Note = "类别码（最后写，相当于\"结果有效\"）";
            Log.Add(entry);
        }

        public PlcViewModel(IPlcClient plc, OpcUaPlc opcUaPlc, SettingsService settings, SortingCoordinator coordinator, WarehouseService warehouse)
        {
            this.plc = plc;
            this.opcUaPlc = opcUaPlc;
            this.settings = settings;
            this.coordinator = coordinator;
            this.warehouse = warehouse;
            CreateRows();

            useOpcUa = settings.Get(SettingKeys.PlcMode, "Simulated") == "OpcUa";
            if (plc.IsSimulated)
            {
                modeText = "当前使用：仿真 PLC（按修改后的 CHAIN.st 步序模拟，变量和真机一样）";
                connectionText = "仿真 PLC 不需要连接";
            }
            else
            {
                modeText = "当前使用：真实 PLC（PLCnext，OPC UA）";
                connectionText = "未连接";
            }
            endpoint = settings.Get(SettingKeys.PlcEndpoint, "opc.tcp://192.168.3.99:4840");
            user = settings.Get(SettingKeys.PlcUser, "");   // 空 = 匿名（实验室这台 EPC 只开放匿名登录）
            Password = SecretProtector.Unprotect(settings.Get(SettingKeys.PlcPasswordProtected, ""));
            useSecurity = settings.Get(SettingKeys.PlcUseSecurity, "True") == "True";

            foreach (PlcLogEntry entry in plc.GetLog())
            {
                Log.Insert(0, entry);
            }
            plc.LogAdded += Plc_LogAdded;
            warehouse.Changed += Warehouse_Changed;
            hostSlots = warehouse.GetSlots();
        }

        /// <summary>变量表的 5 行。</summary>
        public ObservableCollection<PlcVariableRow> Variables { get; private set; }

        /// <summary>库位对照表的 12 行。</summary>
        public ObservableCollection<SlotCompareRow> SlotRows { get; private set; }

        /// <summary>通讯日志（新的在前）。</summary>
        public ObservableCollection<PlcLogEntry> Log { get; private set; }

        /// <summary>
        /// 密码。PasswordBox 不能直接绑定（为了安全，WPF 不允许），
        /// 所以由 PlcView.xaml.cs 在密码框内容改变时写进来。
        /// </summary>
        public string Password { get; set; }

        /// <summary>选择的 PLC 类型：true 真实 PLC（OPC UA），false 仿真。重启程序后生效。</summary>
        [ObservableProperty]
        private bool useOpcUa;

        [ObservableProperty]
        private string modeText;

        [ObservableProperty]
        private string endpoint;

        [ObservableProperty]
        private string user;

        [ObservableProperty]
        private bool useSecurity;

        [ObservableProperty]
        private string connectionText;

        [ObservableProperty]
        private string mismatchText = "";

        [ObservableProperty]
        private string message = "";

        /// <summary>是否是真实 PLC（连接、断开按钮只对真实 PLC 有效）。</summary>
        public bool IsRealPlc
        {
            get { return opcUaPlc != null; }
        }

        // ---------------- 按钮命令 ----------------

        /// <summary>保存连接参数（密码加密保存），然后连接。</summary>
        [RelayCommand]
        private void Connect()
        {
            SaveConnectionSettings();
            if (opcUaPlc == null)
            {
                Message = "现在用的是仿真 PLC。要连接真实 PLC：选“真实 PLC”，然后重启程序。";
                return;
            }
            opcUaPlc.Options.Endpoint = Endpoint;
            opcUaPlc.Options.User = User;
            opcUaPlc.Options.Password = Password;
            opcUaPlc.Options.UseSecurity = UseSecurity;

            ConnectionText = "正在连接 " + Endpoint + " …";
            if (opcUaPlc.Connect())
            {
                ConnectionText = "已连接 " + Endpoint;
                Message = "连接成功。到“监控”页点“联机”后，上位机开始响应 PLC 的拍照请求。";
            }
            else
            {
                ConnectionText = "连接失败";
                Message = "连接失败：" + opcUaPlc.LastError;
            }
        }

        [RelayCommand]
        private void Disconnect()
        {
            if (opcUaPlc == null)
            {
                return;
            }
            opcUaPlc.Disconnect();
            ConnectionText = "已断开";
        }

        /// <summary>以上位机数据库为准，把库位状态写给 PLC（空闲写 0，已占用写库位号）。</summary>
        [RelayCommand]
        private void SyncSlots()
        {
            int count = coordinator.SyncSlotsToPlc();
            if (!plc.GetStatus().Connected)
            {
                Message = "PLC 没有连接，无法同步";
            }
            else
            {
                Message = "已同步 " + count + " 个库位到 PLC";
            }
        }

        [RelayCommand]
        private void ClearLog()
        {
            plc.ClearLog();
            Log.Clear();
        }

        // ---------------- 定时刷新 ----------------

        /// <summary>定时器每 50 ms 调用一次：刷新变量表和库位对照。</summary>
        public void Refresh(PlcStatus status)
        {
            if (opcUaPlc != null && !status.Connected && ConnectionText.StartsWith("已连接"))
            {
                ConnectionText = "已断开：" + opcUaPlc.LastError;
            }
            if (!status.Connected)
            {
                foreach (PlcVariableRow row in Variables)
                {
                    row.Value = "—";
                }
                return;
            }

            PlcVariables variables = plc.GetVariables();
            Variables[0].Value = variables.CaptureRequest ? "1（有工件）" : "0";
            Variables[1].Value = variables.Step + "（" + PlcTags.DescribeStep(variables.Step) + "）";
            Variables[2].Value = DescribeCategory(variables.Category);
            Variables[3].Value = variables.TargetSlot.ToString();
            Variables[4].Value = DescribeMatState(variables.MatState);
            UpdateSlotRows(variables);
        }

        private void UpdateSlotRows(PlcVariables variables)
        {
            int mismatches = 0;
            foreach (StorageSlot slot in hostSlots)
            {
                SlotCompareRow row = SlotRows[slot.SlotNo - 1];
                int plcValue = variables.MatState[slot.SlotNo];
                row.HostText = DescribeHostSlot(slot);
                if (plcValue == slot.SlotNo)
                {
                    row.PlcText = plcValue + "（有料）";
                }
                else
                {
                    row.PlcText = plcValue + "（空）";
                }

                bool plcOccupied = plcValue == slot.SlotNo;
                if (slot.State == SlotState.Reserved)
                {
                    row.ResultText = "执行中";
                    row.IsMismatch = false;
                }
                else if (slot.State == SlotState.NeedsCheck)
                {
                    row.ResultText = "待人工确认";
                    row.IsMismatch = false;
                }
                else if ((slot.State == SlotState.Occupied) == plcOccupied)
                {
                    row.ResultText = "一致";
                    row.IsMismatch = false;
                }
                else
                {
                    row.ResultText = "不一致";
                    row.IsMismatch = true;
                    mismatches++;
                }
            }
            if (mismatches == 0)
            {
                MismatchText = "全部一致";
            }
            else
            {
                MismatchText = mismatches + " 个库位不一致，确认现场后可点“以上位机为准同步到 PLC”";
            }
        }

        // ---------------- 事件 ----------------

        private void Plc_LogAdded(object sender, PlcLogEntry e)
        {
            Log.Insert(0, e);
            while (Log.Count > MaxLogRows)
            {
                Log.RemoveAt(Log.Count - 1);
            }
        }

        private void Warehouse_Changed(object sender, EventArgs e)
        {
            hostSlots = warehouse.GetSlots();
        }

        // ---------------- 内部方法 ----------------

        private void CreateRows()
        {
            Variables = new ObservableCollection<PlcVariableRow>();
            foreach (PlcTagInfo tag in PlcTags.GetAll())
            {
                Variables.Add(new PlcVariableRow(tag));
            }
            SlotRows = new ObservableCollection<SlotCompareRow>();
            for (int slotNo = 1; slotNo <= PlcVariables.SlotCount; slotNo++)
            {
                SlotRows.Add(new SlotCompareRow(slotNo));
            }
            Log = new ObservableCollection<PlcLogEntry>();
        }

        private void SaveConnectionSettings()
        {
            settings.Set(SettingKeys.PlcEndpoint, Endpoint);
            settings.Set(SettingKeys.PlcUser, User);
            settings.Set(SettingKeys.PlcPasswordProtected, SecretProtector.Protect(Password));
            settings.Set(SettingKeys.PlcUseSecurity, UseSecurity ? "True" : "False");
        }

        private static string DescribeCategory(int code)
        {
            if (code == 0)
            {
                return "0（还没有结果）";
            }
            if (code >= 1 && code <= 5)
            {
                return code + "（" + DefectCategories.GetName((DefectCategory)code) + "）";
            }
            return code.ToString();
        }

        /// <summary>例如 "[1]=1 [4]=4"，只列出有料的库位。</summary>
        private static string DescribeMatState(int[] matState)
        {
            string text = "";
            for (int slot = 1; slot <= PlcVariables.SlotCount; slot++)
            {
                if (matState[slot] != 0)
                {
                    text = text + "[" + slot + "]=" + matState[slot] + " ";
                }
            }
            if (text == "")
            {
                return "全部为 0（空）";
            }
            return text.Trim();
        }

        private static string DescribeHostSlot(StorageSlot slot)
        {
            string category = "";
            if (slot.Category != null)
            {
                category = " " + slot.Category.Value;
            }
            switch (slot.State)
            {
                case SlotState.Empty:
                    return "空闲";
                case SlotState.Reserved:
                    return "执行中" + category;
                case SlotState.Occupied:
                    return "已占用" + category;
                case SlotState.NeedsCheck:
                    return "待确认" + category;
                default:
                    return slot.State.ToString();
            }
        }

        // 下面这个方法由 [ObservableProperty] 自动调用：属性值改变后执行。

        partial void OnUseOpcUaChanged(bool value)
        {
            if (settings == null)
            {
                return;   // 设计器预览
            }
            settings.Set(SettingKeys.PlcMode, value ? "OpcUa" : "Simulated");
            Message = "PLC 类型已保存，重启程序后生效";
        }
    }
}
