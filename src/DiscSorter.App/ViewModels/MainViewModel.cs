using CommunityToolkit.Mvvm.ComponentModel;
using DiscSorter.Core.Plc;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 主窗口：包含 6 个页面的 ViewModel，以及顶栏显示的运行状态。
    /// （[ObservableProperty] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        /// <summary>设计器预览用的构造函数：每个页面都用示例数据。</summary>
        public MainViewModel()
        {
            Monitor = new MonitorViewModel();
            Warehouse = Monitor.Warehouse;
            Vision = new VisionViewModel();
            Camera = new CameraViewModel();
            Plc = new PlcViewModel();
            Settings = new SettingsViewModel();
            runStateText = "自动运行";
        }

        public MainViewModel(MonitorViewModel monitor, VisionViewModel vision, WarehouseViewModel warehouse,
            CameraViewModel camera, PlcViewModel plc, SettingsViewModel settings)
        {
            Monitor = monitor;
            Vision = vision;
            Warehouse = warehouse;
            Camera = camera;
            Plc = plc;
            Settings = settings;
        }

        public MonitorViewModel Monitor { get; private set; }

        public VisionViewModel Vision { get; private set; }

        public WarehouseViewModel Warehouse { get; private set; }

        public CameraViewModel Camera { get; private set; }

        public PlcViewModel Plc { get; private set; }

        public SettingsViewModel Settings { get; private set; }

        /// <summary>顶栏的运行状态，例如"自动运行"、"停止"或"报警 5：无可用库位，需人工取件"。</summary>
        [ObservableProperty]
        private string runStateText = "停止";

        [ObservableProperty]
        private bool hasAlarm;

        /// <summary>定时器每 50 ms 调用一次：刷新顶栏、监控页、PLC 通讯页，自动运行时锁定视觉调试页和相机页。</summary>
        public void UpdatePlcStatus(PlcStatus status)
        {
            HasAlarm = status.ErrCode != PlcCodes.ErrNone;
            if (HasAlarm)
            {
                RunStateText = "报警 " + status.ErrCode + "：" + PlcCodes.Describe(status.ErrCode);
            }
            else if (!status.Connected)
            {
                RunStateText = "PLC 未连接";
            }
            else if (status.AutoRunning)
            {
                RunStateText = "自动运行";
            }
            else
            {
                RunStateText = "停止";
            }

            Monitor.UpdatePlcStatus(status);
            Plc.Refresh(status);
            Vision.IsLocked = status.AutoRunning;
            Camera.IsLocked = status.AutoRunning;
        }
    }
}
