using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Plc;
using DiscSorter.Core.Workflow;
using Microsoft.Win32;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 设置页：类别来源、自动运行的图片文件夹、仿真 PLC 节拍和故障注入、文件路径。
    /// 每个设置改了以后立刻生效，并保存到数据库，下次启动还在。
    /// （[ObservableProperty] 和 [RelayCommand] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly SettingsService settings;
        private readonly SimulatedPlc plc;
        private readonly SortingCoordinator coordinator;

        /// <summary>设计器预览用的构造函数。</summary>
        public SettingsViewModel()
        {
            CreateFaultOptions();
            useAlgorithm = true;
            runImageFolder = @"E:\woker\c#edge-defect\data\station";
            runImageText = "12 张图片";
            arrivalMs = 1500;
            sortMs = 1500;
            selectedFault = SimulatedFault.None;
            DatabasePath = @"C:\Users\...\AppData\Local\DiscSorter\Data\discsorter.db";
            ProgramPath = @"E:\woker\c#edge-defect\halcon\DiscDefect.hdev";
        }

        public SettingsViewModel(SettingsService settings, SimulatedPlc plc, SortingCoordinator coordinator,
            string databasePath, string programPath, string defaultImageFolder)
        {
            this.settings = settings;
            this.plc = plc;
            this.coordinator = coordinator;
            CreateFaultOptions();
            DatabasePath = databasePath;
            ProgramPath = programPath;

            // 读出保存的设置并立刻应用（给属性赋值会触发下面的 OnXxxChanged 方法）
            UseAlgorithm = settings.Get(SettingKeys.CategorySource, "Algorithm") == "Algorithm";
            ArrivalMs = settings.GetInt(SettingKeys.SimArrivalMs, 1500);
            SortMs = settings.GetInt(SettingKeys.SimSortMs, 1500);
            SelectedFault = SimulatedFault.None;
            OpenRunImageFolder(settings.Get(SettingKeys.RunImageFolder, defaultImageFolder));
        }

        public List<FaultOption> FaultOptions { get; private set; }

        public string DatabasePath { get; private set; }

        public string ProgramPath { get; private set; }

        /// <summary>true：类别来自 HALCON 算法；false：来自图片文件名（调试用）。</summary>
        [ObservableProperty]
        private bool useAlgorithm = true;

        [ObservableProperty]
        private string runImageFolder;

        [ObservableProperty]
        private string runImageText;

        /// <summary>仿真 PLC：两件之间的间隔（毫秒）。</summary>
        [ObservableProperty]
        private int arrivalMs;

        /// <summary>仿真 PLC：搬运耗时（毫秒）。</summary>
        [ObservableProperty]
        private int sortMs;

        [ObservableProperty]
        private SimulatedFault selectedFault;

        [RelayCommand]
        private void ChooseRunImageFolder()
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            dialog.Title = "选择自动运行时回放的图片文件夹";
            if (dialog.ShowDialog() != true)
            {
                return;
            }
            OpenRunImageFolder(dialog.FolderName);
            settings.Set(SettingKeys.RunImageFolder, dialog.FolderName);
        }

        private void OpenRunImageFolder(string folder)
        {
            RunImageFolder = folder;
            if (!Directory.Exists(folder))
            {
                RunImageText = "文件夹不存在";
                return;
            }
            coordinator.Images.OpenFolder(folder);
            RunImageText = coordinator.Images.Files.Count + " 张图片，按文件名顺序循环";
        }

        private void CreateFaultOptions()
        {
            FaultOptions = new List<FaultOption>();
            FaultOptions.Add(new FaultOption(SimulatedFault.None, "无（正常运行）"));
            FaultOptions.Add(new FaultOption(SimulatedFault.NoSortDone, "搬运途中卡住（不放料）"));
            FaultOptions.Add(new FaultOption(SimulatedFault.WrongDoneSlot, "放错库位"));
        }

        // 下面这些方法由 [ObservableProperty] 自动调用：属性值改变后执行。
        // 方法名的规则是 "On" + 属性名 + "Changed"。

        partial void OnUseAlgorithmChanged(bool value)
        {
            if (settings == null)
            {
                return;   // 设计器预览时没有 settings
            }
            if (value)
            {
                coordinator.CategorySource = CategorySource.Algorithm;
                settings.Set(SettingKeys.CategorySource, "Algorithm");
            }
            else
            {
                coordinator.CategorySource = CategorySource.FileName;
                settings.Set(SettingKeys.CategorySource, "FileName");
            }
        }

        partial void OnArrivalMsChanged(int value)
        {
            if (settings == null)
            {
                return;
            }
            plc.ArrivalMs = value;
            settings.Set(SettingKeys.SimArrivalMs, value.ToString());
        }

        partial void OnSortMsChanged(int value)
        {
            if (settings == null)
            {
                return;
            }
            plc.SortMs = value;
            settings.Set(SettingKeys.SimSortMs, value.ToString());
        }

        partial void OnSelectedFaultChanged(SimulatedFault value)
        {
            if (settings == null)
            {
                return;
            }
            plc.Fault = value;
        }
    }
}
