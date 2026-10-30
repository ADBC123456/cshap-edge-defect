using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Plc;
using DiscSorter.Core.Vision;
using DiscSorter.Core.Workflow;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 监控主画面：PLC 状态和报警、运行控制、最新一件的图像和结果、库位图、今日统计、最近记录和流程日志。
    /// （[ObservableProperty] 和 [RelayCommand] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class MonitorViewModel : ObservableObject
    {
        private readonly IPlcClient plc;
        private readonly SortingCoordinator coordinator;
        private readonly RecordService records;

        /// <summary>设计器预览用的构造函数：填一些示例数据。</summary>
        public MonitorViewModel()
        {
            Warehouse = new WarehouseViewModel();
            RecentRecords = new ObservableCollection<InspectionRecord>();
            Messages = new ObservableCollection<SortingMessageEventArgs>();

            PlcName = "仿真 PLC";
            StartText = "启动";
            StopText = "停止";
            IsConnected = true;
            ConnectionText = "● 已连接";
            ModeText = "自动运行";
            StepText = "10 去取料位";
            JobText = "8";
            SignalText = "工件到位 · 搬运中";
            HeartbeatText = "1024";
            CategorySourceText = "文件名真值（调试）";
            HasAlarm = true;
            AlarmText = "报警 5：无可用库位，需人工取件。请把工件取走，再点“复位 / 取件确认”。";

            HasLatest = true;
            LatestCategoryText = "Q　→　缺角 Q";
            LatestTargetText = "2";
            LatestSourceText = "文件名真值";
            LatestFileText = "Q_2.jpg（任务 6）";
            LatestTimeText = "识别 12.5 ms · 总计 18.3 ms";
            LatestTruthText = "Q  —";

            CountsText = "Q 5　H 2　K 2　W 2　NG 0　共 11";
            AccuracyText = "—";
            TimingText = "平均 9.6 ms · 最大 18.3 ms";

            InspectionRecord sample = new InspectionRecord();
            sample.JobId = 6;
            sample.Time = DateTime.Now;
            sample.ImagePath = "Q_2.jpg";
            sample.Category = DefectCategory.Q;
            sample.Source = CategorySource.FileName;
            sample.ExpectedLabel = "Q";
            sample.TargetSlot = 2;
            sample.TotalMs = 18.3;
            RecentRecords.Add(sample);
            Messages.Add(new SortingMessageEventArgs("任务 6：Q → 缺角 Q，目标库位 2", false));
            Messages.Add(new SortingMessageEventArgs("任务 8：缺角 Q 库区（列 1）已满，请人工取件", true));
        }

        public MonitorViewModel(IPlcClient plc, SortingCoordinator coordinator, RecordService records, WarehouseViewModel warehouse)
        {
            this.plc = plc;
            this.coordinator = coordinator;
            this.records = records;
            Warehouse = warehouse;
            RecentRecords = new ObservableCollection<InspectionRecord>();
            Messages = new ObservableCollection<SortingMessageEventArgs>();
            PlcName = plc.Name;
            if (plc.IsSimulated)
            {
                StartText = "启动";
                StopText = "停止";
            }
            else
            {
                // 真实 PLC 的启停由现场按钮控制，上位机这里是"联机 / 脱机"（是否响应拍照请求）
                StartText = "联机";
                StopText = "脱机";
            }

            coordinator.InspectionFinished += Coordinator_InspectionFinished;
            coordinator.MessageRaised += Coordinator_MessageRaised;

            UpdatePlcStatus(plc.GetStatus());
            RefreshRecordsAndStats();
        }

        /// <summary>仓储页的数据（库位图直接绑定它的 Slots 和 Columns）。</summary>
        public WarehouseViewModel Warehouse { get; private set; }

        /// <summary>最近的检测记录。</summary>
        public ObservableCollection<InspectionRecord> RecentRecords { get; private set; }

        /// <summary>流程日志（新的在前）。</summary>
        public ObservableCollection<SortingMessageEventArgs> Messages { get; private set; }

        // ---------------- PLC 状态 ----------------

        public string PlcName { get; private set; }

        /// <summary>"启动"按钮的文字：仿真 PLC 是"启动"，真实 PLC 是"联机"。</summary>
        public string StartText { get; private set; }

        /// <summary>"停止"按钮的文字：仿真 PLC 是"停止"，真实 PLC 是"脱机"。</summary>
        public string StopText { get; private set; }

        [ObservableProperty]
        private bool isConnected;

        [ObservableProperty]
        private string connectionText;

        [ObservableProperty]
        private string modeText;

        [ObservableProperty]
        private string stepText;

        [ObservableProperty]
        private string jobText;

        [ObservableProperty]
        private string signalText;

        [ObservableProperty]
        private string heartbeatText;

        [ObservableProperty]
        private string categorySourceText;

        [ObservableProperty]
        private bool hasAlarm;

        [ObservableProperty]
        private string alarmText;

        /// <summary>自动运行中（"启动""单件"按钮不可用，"停止"可用）。</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunSingleCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        private bool isRunning;

        // ---------------- 最新一件 ----------------

        /// <summary>最新一件的识别结果（图像控件绑定它）。</summary>
        [ObservableProperty]
        private DetectionResult latestResult;

        /// <summary>是否已经有过一件（没有时图像区显示"等待第一件工件"）。</summary>
        [ObservableProperty]
        private bool hasLatest;

        [ObservableProperty]
        private string latestCategoryText = "—";

        [ObservableProperty]
        private string latestTargetText = "—";

        [ObservableProperty]
        private string latestSourceText = "—";

        [ObservableProperty]
        private string latestFileText = "—";

        [ObservableProperty]
        private string latestTimeText = "—";

        [ObservableProperty]
        private string latestTruthText = "—";

        // ---------------- 今日统计 ----------------

        [ObservableProperty]
        private string countsText = "—";

        [ObservableProperty]
        private string accuracyText = "—";

        [ObservableProperty]
        private string timingText = "—";

        /// <summary>定时器每 50 ms 调用一次，把 PLC 状态显示到界面上。</summary>
        public void UpdatePlcStatus(PlcStatus status)
        {
            IsRunning = status.AutoRunning;
            IsConnected = status.Connected;
            if (status.Connected)
            {
                ConnectionText = "● 已连接";
            }
            else
            {
                ConnectionText = "● 未连接（到“PLC 通讯”页连接）";
            }
            if (status.AutoRunning)
            {
                ModeText = plc.IsSimulated ? "自动运行" : "联机";
            }
            else
            {
                ModeText = plc.IsSimulated ? "停止" : "脱机";
            }
            StepText = status.Step + " " + PlcTags.DescribeStep(status.Step);
            JobText = status.JobId.ToString();
            HeartbeatText = status.Heartbeat.ToString();
            SignalText = BuildSignalText(status);

            if (coordinator.CategorySource == CategorySource.Algorithm)
            {
                CategorySourceText = "HALCON 算法";
            }
            else
            {
                CategorySourceText = "文件名真值（调试）";
            }

            HasAlarm = status.ErrCode != PlcCodes.ErrNone;
            if (!HasAlarm)
            {
                AlarmText = "";
            }
            else if (status.ErrCode == PlcCodes.ErrNoFreeSlot)
            {
                AlarmText = "报警 5：" + PlcCodes.Describe(status.ErrCode) + "。请把工件取走，再点“复位 / 取件确认”。";
            }
            else
            {
                AlarmText = "报警 " + status.ErrCode + "：" + PlcCodes.Describe(status.ErrCode) + "。处理后点“复位 / 取件确认”。";
            }
            ResetCommand.NotifyCanExecuteChanged();
        }

        // ---------------- 按钮命令 ----------------

        [RelayCommand(CanExecute = nameof(CanStart))]
        private void Start()
        {
            plc.Start();
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private void RunSingle()
        {
            plc.RunSingle();
        }

        [RelayCommand(CanExecute = nameof(CanStop))]
        private void Stop()
        {
            plc.Stop();
        }

        [RelayCommand(CanExecute = nameof(CanReset))]
        private void Reset()
        {
            plc.Reset();
        }

        private bool CanStart()
        {
            return !IsRunning;
        }

        private bool CanStop()
        {
            return IsRunning;
        }

        private bool CanReset()
        {
            return HasAlarm;
        }

        // ---------------- 分拣流程的事件 ----------------

        /// <summary>一件工件识别完成：显示图像和结果，刷新记录和统计。</summary>
        private void Coordinator_InspectionFinished(object sender, InspectionFinishedEventArgs e)
        {
            DetectionResult previous = LatestResult;
            LatestResult = e.Result;
            if (previous != null)
            {
                previous.Dispose();   // 新图已经显示，旧图可以释放了
            }

            InspectionRecord record = e.Record;
            HasLatest = true;
            LatestCategoryText = e.Label + "　→　" + DefectCategories.GetName(e.Category);
            LatestTargetText = record.TargetText;
            LatestSourceText = record.SourceText;
            LatestFileText = record.FileName + "（任务 " + e.JobId + "）";
            LatestTimeText = "识别 " + FormatMs(record.DetectMs) + " · 总计 " + FormatMs(record.TotalMs);
            string truth = record.ExpectedLabel;
            if (truth == null)
            {
                truth = "—";
            }
            LatestTruthText = truth + "  " + record.CorrectText;

            RefreshRecordsAndStats();
        }

        private void Coordinator_MessageRaised(object sender, SortingMessageEventArgs e)
        {
            Messages.Insert(0, e);
            while (Messages.Count > 100)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }
        }

        private void RefreshRecordsAndStats()
        {
            RecentRecords.Clear();
            foreach (InspectionRecord record in records.GetRecent(30))
            {
                RecentRecords.Add(record);
            }

            InspectionStats stats = records.GetStats(DateTime.Today);
            string counts = "";
            foreach (DefectCategory category in DefectCategories.All)
            {
                counts = counts + category + " " + stats.Counts[category] + "　";
            }
            CountsText = counts + "共 " + stats.Total;

            if (stats.Judged == 0)
            {
                AccuracyText = "—（需要类别来自算法，且图片文件名带真值）";
            }
            else
            {
                double accuracy = (double)stats.Correct / stats.Judged;
                AccuracyText = accuracy.ToString("P1") + "（" + stats.Correct + "/" + stats.Judged + "）";
            }

            if (stats.AverageMs == null)
            {
                TimingText = "—";
            }
            else
            {
                TimingText = "平均 " + FormatMs(stats.AverageMs) + " · 最大 " + FormatMs(stats.MaxMs);
            }
        }

        private static string BuildSignalText(PlcStatus status)
        {
            string text = "";
            if (status.PartPresent)
            {
                text = text + "工件到位 ";
            }
            if (status.InspectRequest)
            {
                text = text + "请求检测 ";
            }
            if (status.ResultValid)
            {
                text = text + "结果有效 ";
            }
            if (status.SortBusy)
            {
                text = text + "搬运中 ";
            }
            if (status.SortDone)
            {
                text = text + "放置完成 ";
            }
            if (text == "")
            {
                return "—";
            }
            return text.Trim();
        }

        private static string FormatMs(double? milliseconds)
        {
            if (milliseconds == null)
            {
                return "—";
            }
            return milliseconds.Value.ToString("0.0") + " ms";
        }
    }
}
