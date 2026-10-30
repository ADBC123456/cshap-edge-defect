using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DiscSorter.App.Helpers;
using DiscSorter.App.ViewModels;
using DiscSorter.Core.Common;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Plc;
using DiscSorter.Core.Vision;
using DiscSorter.Core.Workflow;

namespace DiscSorter.App
{
    /// <summary>
    /// 程序入口：建数据库 → 创建各个服务和设备 → 创建各页面的 ViewModel → 显示主窗口 → 启动定时器。
    ///
    /// 定时器每 50 ms 触发一次，依次做三件事：
    ///   1. PLC 走一步：仿真 PLC 模拟一个扫描周期；真实 PLC 通过 OPC UA 读一次变量（plc.Tick）
    ///   2. 分拣协调器检查 PLC 信号的变化并处理（coordinator.Tick）
    ///   3. 把 PLC 状态显示到界面上
    /// 所有代码都在界面线程上运行，不需要考虑多线程。
    /// </summary>
    public partial class App : Application
    {
        private DispatcherTimer timer;
        private IPlcClient plc;
        private OpcUaPlc opcUaPlc;
        private HikCamera camera;
        private SortingCoordinator coordinator;
        private HalconDetector detector;
        private MainViewModel mainViewModel;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            StartupOptions options = StartupOptions.Parse(e.Args);

            // 数据库默认放在 %LOCALAPPDATA%\DiscSorter\Data；命令行 --data-dir 可以换一个目录（演示用）
            string dataFolder = options.DataFolder;
            if (dataFolder == null)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dataFolder = Path.Combine(localAppData, "DiscSorter", "Data");
            }
            string databasePath = Path.Combine(dataFolder, "discsorter.db");

            try
            {
                if (options.DesignPreview)
                {
                    // 用设计器的示例数据显示窗口（检查界面布局用，不连数据库、HALCON 和设备）
                    mainViewModel = new MainViewModel();
                }
                else
                {
                    mainViewModel = CreateMainViewModel(dataFolder, databasePath);
                }

                MainWindow window = new MainWindow();
                window.DataContext = mainViewModel;
                MainWindow = window;
                window.Show();

                if (!options.DesignPreview)
                {
                    timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(50);
                    timer.Tick += Timer_Tick;
                    timer.Start();
                    if (options.AutoStart)
                    {
                        plc.Start();
                    }
                }

                if (options.ScreenshotPath != null)
                {
                    WindowScreenshot.SaveLater(window, options.ScreenshotPath, options.PageIndex, options.WaitMs);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败\n数据文件：" + databasePath + "\n\n" + ex.Message,
                    "DiscSorter", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        /// <summary>创建全部服务、设备和页面。</summary>
        private MainViewModel CreateMainViewModel(string dataFolder, string databasePath)
        {
            // 1. 数据库
            Directory.CreateDirectory(dataFolder);
            Database database = new Database(databasePath);
            DatabaseInitializer initializer = new DatabaseInitializer(database);
            initializer.Initialize();
            initializer.Recover();

            WarehouseService warehouse = new WarehouseService(database);
            RecordService records = new RecordService(database);
            SettingsService settings = new SettingsService(database);

            // 2. 默认文件夹：工程根目录下的 halcon\ 和 data\station\（工位图）。发布到别处运行时用程序所在目录
            string root = ProjectPaths.FindRoot();
            if (root == null)
            {
                root = AppContext.BaseDirectory;
            }
            string defaultProgramPath = Path.Combine(root, "halcon", "DiscDefect.hdev");
            string stationFolder = Path.Combine(root, "data", "station");
            string cameraImageFolder = Path.Combine(Path.GetDirectoryName(dataFolder), "Images");

            // 3. PLC：按"PLC 通讯"页保存的类型选择仿真 PLC 或真实 PLC（OPC UA）
            SimulatedPlc simulatedPlc = new SimulatedPlc();
            if (settings.Get(SettingKeys.PlcMode, "Simulated") == "OpcUa")
            {
                opcUaPlc = new OpcUaPlc(LoadOpcUaOptions(settings));
                plc = opcUaPlc;
            }
            else
            {
                plc = simulatedPlc;
                simulatedPlc.LoadRetainedSlots(GetOccupiedSlots(warehouse));
            }

            // 4. 识别器、相机、分拣协调器
            detector = new HalconDetector();
            camera = new HikCamera();
            coordinator = new SortingCoordinator(plc, detector, warehouse, records);

            // 5. 各页面的 ViewModel
            WarehouseViewModel warehousePage = new WarehouseViewModel(warehouse);
            warehousePage.Coordinator = coordinator;
            VisionViewModel visionPage = new VisionViewModel(detector, settings, defaultProgramPath, stationFolder, camera);
            MonitorViewModel monitorPage = new MonitorViewModel(plc, coordinator, records, warehousePage);
            SettingsViewModel settingsPage = new SettingsViewModel(settings, simulatedPlc, coordinator,
                databasePath, visionPage.ProgramPath, stationFolder);
            CameraViewModel cameraPage = new CameraViewModel(camera, detector, settings, coordinator, cameraImageFolder);
            PlcViewModel plcPage = new PlcViewModel(plc, opcUaPlc, settings, coordinator, warehouse);

            // 6. 真实 PLC：启动时自动连接一次（PLC 不在线时会等几秒，然后显示"连接失败"）
            if (opcUaPlc != null)
            {
                plcPage.ConnectCommand.Execute(null);
            }

            return new MainViewModel(monitorPage, visionPage, warehousePage, cameraPage, plcPage, settingsPage);
        }

        /// <summary>数据库里"已占用"的库位号。</summary>
        private static List<int> GetOccupiedSlots(WarehouseService warehouse)
        {
            List<int> occupied = new List<int>();
            foreach (StorageSlot slot in warehouse.GetSlots())
            {
                if (slot.State == SlotState.Occupied)
                {
                    occupied.Add(slot.SlotNo);
                }
            }
            return occupied;
        }

        /// <summary>从设置里读出真实 PLC 的连接参数（密码是加密保存的，这里解密）。</summary>
        private static OpcUaOptions LoadOpcUaOptions(SettingsService settings)
        {
            OpcUaOptions options = new OpcUaOptions();
            options.Endpoint = settings.Get(SettingKeys.PlcEndpoint, options.Endpoint);
            options.User = settings.Get(SettingKeys.PlcUser, "");   // 空 = 匿名（实验室这台 EPC 只开放匿名登录）
            options.Password = SecretProtector.Unprotect(settings.Get(SettingKeys.PlcPasswordProtected, ""));
            options.UseSecurity = settings.Get(SettingKeys.PlcUseSecurity, "True") == "True";
            return options;
        }

        /// <summary>每 50 ms 一次：PLC 走一步 → 协调器处理信号 → 刷新界面。</summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            plc.Tick(now);
            coordinator.Tick(now);
            mainViewModel.UpdatePlcStatus(plc.GetStatus());
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (timer != null)
            {
                timer.Stop();
            }
            if (opcUaPlc != null)
            {
                opcUaPlc.Disconnect();
            }
            if (camera != null)
            {
                camera.Close();
                HikCamera.FinalizeSdk();
            }
            if (detector != null)
            {
                detector.Unload();
            }
            base.OnExit(e);
        }
    }
}
