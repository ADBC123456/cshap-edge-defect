using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscSorter.Core.Data;
using DiscSorter.Core.Models;
using DiscSorter.Core.Workflow;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 仓储管理页：12 个库位、4 列的类别映射、出库、待确认处理、手动入库测试、任务和日志。
    /// 监控页的库位图也用这里的 Slots 和 Columns，所以两边显示总是一致的。
    /// （[ObservableProperty] 和 [RelayCommand] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class WarehouseViewModel : ObservableObject
    {
        private readonly WarehouseService warehouse;

        /// <summary>设计器预览用的构造函数：填一些示例数据。</summary>
        public WarehouseViewModel()
        {
            CreateSlotsAndColumns();
            DefectCategory[] mapping = { DefectCategory.Q, DefectCategory.H, DefectCategory.K, DefectCategory.W };
            for (int i = 0; i < 4; i++)
            {
                Columns[i].SavedCategory = mapping[i];
                Columns[i].SelectedCategory = mapping[i];
                Columns[i].FreeCount = 3;
            }
            Slots[0].State = SlotState.Occupied;
            Slots[0].Category = DefectCategory.Q;
            Slots[0].UpdatedAt = DateTime.Now;
            Slots[1].State = SlotState.Occupied;
            Slots[1].Category = DefectCategory.Q;
            Slots[1].UpdatedAt = DateTime.Now;
            Slots[3].State = SlotState.Reserved;
            Slots[3].Category = DefectCategory.H;
            Slots[9].State = SlotState.NeedsCheck;
            Slots[9].Category = DefectCategory.W;
            Columns[0].FreeCount = 1;
            Columns[1].FreeCount = 2;
            Columns[3].FreeCount = 2;
            UnmappedText = "未分配库区：异常 NG（来件下发库位 0，报警后人工取件）";
            EmptyCount = 8;
            ReservedCount = 1;
            OccupiedCount = 2;
            NeedsCheckCount = 1;
            Message = "列映射已保存";
        }

        public WarehouseViewModel(WarehouseService warehouse)
        {
            this.warehouse = warehouse;
            CreateSlotsAndColumns();
            warehouse.Changed += Warehouse_Changed;
            Refresh();
        }

        /// <summary>分拣协调器（程序启动时由 App 设置），人工出库后用它把库位状态同步给 PLC。</summary>
        public SortingCoordinator Coordinator { get; set; }

        /// <summary>12 个库位，下标 = 库位号 - 1。XAML 里用 Slots[0] 绑定库位 1。</summary>
        public List<SlotViewModel> Slots { get; private set; }

        /// <summary>4 列，下标 = 列号 - 1。</summary>
        public List<ColumnViewModel> Columns { get; private set; }

        /// <summary>类别下拉框的选项。</summary>
        public List<CategoryOption> CategoryOptions { get; private set; }

        /// <summary>最近的入库任务。</summary>
        public ObservableCollection<StorageTask> RecentTasks { get; private set; }

        /// <summary>最近的操作日志。</summary>
        public ObservableCollection<OperationLog> RecentLogs { get; private set; }

        /// <summary>页面底部的提示文字。</summary>
        [ObservableProperty]
        private string message;

        /// <summary>提示是不是错误（错误用红色显示）。</summary>
        [ObservableProperty]
        private bool messageIsError;

        /// <summary>没有分到列的类别，例如"未分配库区：异常 NG（……）"。</summary>
        [ObservableProperty]
        private string unmappedText;

        [ObservableProperty]
        private int emptyCount;

        [ObservableProperty]
        private int reservedCount;

        [ObservableProperty]
        private int occupiedCount;

        [ObservableProperty]
        private int needsCheckCount;

        /// <summary>"手动入库测试"选的类别。</summary>
        [ObservableProperty]
        private DefectCategory testCategory = DefectCategory.Q;

        /// <summary>从数据库重新读取并刷新整个页面。</summary>
        public void Refresh()
        {
            // 库位
            List<StorageSlot> slots = warehouse.GetSlots();
            EmptyCount = 0;
            ReservedCount = 0;
            OccupiedCount = 0;
            NeedsCheckCount = 0;
            foreach (StorageSlot slot in slots)
            {
                Slots[slot.SlotNo - 1].Update(slot);
                if (slot.State == SlotState.Empty)
                {
                    EmptyCount++;
                }
                else if (slot.State == SlotState.Reserved)
                {
                    ReservedCount++;
                }
                else if (slot.State == SlotState.Occupied)
                {
                    OccupiedCount++;
                }
                else
                {
                    NeedsCheckCount++;
                }
            }

            // 列映射和每列空闲数
            List<DefectCategory> mappedCategories = new List<DefectCategory>();
            foreach (ColumnMapping mapping in warehouse.GetMappings())
            {
                ColumnViewModel column = Columns[mapping.Column - 1];
                bool editing = column.IsDirty;   // 正在改下拉框时不覆盖用户的选择
                column.SavedCategory = mapping.Category;
                if (!editing)
                {
                    column.SelectedCategory = mapping.Category;
                }
                column.FreeCount = CountFreeSlots(slots, mapping.Column);
                mappedCategories.Add(mapping.Category);
            }

            // 没有分到列的类别
            string unmapped = "";
            foreach (DefectCategory category in DefectCategories.All)
            {
                if (!mappedCategories.Contains(category))
                {
                    if (unmapped != "")
                    {
                        unmapped = unmapped + "、";
                    }
                    unmapped = unmapped + DefectCategories.GetName(category);
                }
            }
            if (unmapped == "")
            {
                UnmappedText = "";
            }
            else
            {
                UnmappedText = "未分配库区：" + unmapped + "（来件下发库位 0，报警后人工取件）";
            }

            // 任务和日志
            RecentTasks.Clear();
            foreach (StorageTask task in warehouse.GetRecentTasks(30))
            {
                RecentTasks.Add(task);
            }
            RecentLogs.Clear();
            foreach (OperationLog log in warehouse.GetRecentLogs(50))
            {
                RecentLogs.Add(log);
            }
        }

        // ---------------- 按钮命令 ----------------

        /// <summary>"保存映射"按钮。</summary>
        [RelayCommand]
        private void SaveMappings()
        {
            List<ColumnMapping> mappings = new List<ColumnMapping>();
            foreach (ColumnViewModel column in Columns)
            {
                mappings.Add(new ColumnMapping(column.Column, column.SelectedCategory));
            }
            try
            {
                warehouse.SaveMappings(mappings);
                ShowMessage("列映射已保存", false);
            }
            catch (ArgumentException ex)
            {
                ShowMessage(ex.Message, true);
            }
            catch (InvalidOperationException ex)
            {
                ShowMessage(ex.Message, true);
                Refresh();
            }
        }

        /// <summary>"撤销修改"按钮：下拉框恢复成已保存的类别。</summary>
        [RelayCommand]
        private void RevertMappings()
        {
            foreach (ColumnViewModel column in Columns)
            {
                column.SelectedCategory = column.SavedCategory;
            }
            ShowMessage("已撤销未保存的修改", false);
        }

        /// <summary>"全部出库"按钮。</summary>
        [RelayCommand]
        private void ClearAll()
        {
            if (!Confirm("全部出库", "确认已经取走仓库中的全部工件？\n执行中和待确认的库位不受影响。"))
            {
                return;
            }
            int count = warehouse.ClearAll();
            ShowMessage("全部出库：释放 " + count + " 个库位" + SyncToPlc(), false);
        }

        /// <summary>"手动入库测试"的"入库"按钮：不经过 PLC，直接分配并记为已入库。</summary>
        [RelayCommand]
        private void TestStore()
        {
            AllocationResult result = warehouse.Reserve(TestCategory, null);
            if (!result.Success)
            {
                ShowMessage("目标库位 0：" + result.Alarm, true);
                return;
            }
            warehouse.Complete(result.TaskId, result.TargetSlot);
            ShowMessage(DefectCategories.GetName(TestCategory) + " 已入库：" + SlotNumbers.Describe(result.TargetSlot), false);
        }

        // ---------------- 库位卡片、列上的按钮会调用这些方法 ----------------

        public void ClearSlot(SlotViewModel slot)
        {
            if (!Confirm("出库", "确认已经从库位 " + slot.SlotNo + " 取走工件？"))
            {
                return;
            }
            warehouse.ClearSlot(slot.SlotNo);
            ShowMessage("库位 " + slot.SlotNo + " 已出库" + SyncToPlc(), false);
        }

        public void ClearColumn(ColumnViewModel column)
        {
            if (!Confirm("整列出库", "确认已经取走列 " + column.Column + " 的全部工件？\n执行中和待确认的库位不受影响。"))
            {
                return;
            }
            int count = warehouse.ClearColumn(column.Column);
            ShowMessage("列 " + column.Column + " 已出库 " + count + " 个库位" + SyncToPlc(), false);
        }

        public void ResolveSlot(SlotViewModel slot, bool placed)
        {
            string question;
            if (placed)
            {
                question = "确认库位 " + slot.SlotNo + " 上确实有工件，记为已占用？";
            }
            else
            {
                question = "确认库位 " + slot.SlotNo + " 上没有工件，释放该库位？";
            }
            if (!Confirm("处理待确认库位", question))
            {
                return;
            }

            try
            {
                warehouse.ResolveNeedsCheck(slot.SlotNo, placed);
                ShowMessage("库位 " + slot.SlotNo + " 已处理" + SyncToPlc(), false);
            }
            catch (InvalidOperationException ex)
            {
                ShowMessage(ex.Message, true);
            }
        }

        // ---------------- 内部方法 ----------------

        /// <summary>
        /// 人工出库、确认之后，以数据库为准把库位状态写给 PLC 的 MatState，
        /// 否则 PLC 还以为库位有料，下次放到这里时会报警。返回追加到提示里的文字。
        /// </summary>
        private string SyncToPlc()
        {
            if (Coordinator == null)
            {
                return "";
            }
            int count = Coordinator.SyncSlotsToPlc();
            if (count == 0)
            {
                return "";
            }
            return "，已同步 " + count + " 个库位到 PLC";
        }

        private void CreateSlotsAndColumns()
        {
            Slots = new List<SlotViewModel>();
            for (int slotNo = 1; slotNo <= SlotNumbers.SlotCount; slotNo++)
            {
                Slots.Add(new SlotViewModel(this, slotNo));
            }
            Columns = new List<ColumnViewModel>();
            for (int column = 1; column <= SlotNumbers.ColumnCount; column++)
            {
                Columns.Add(new ColumnViewModel(this, column));
            }
            CategoryOptions = CategoryOption.CreateAll();
            RecentTasks = new ObservableCollection<StorageTask>();
            RecentLogs = new ObservableCollection<OperationLog>();
        }

        /// <summary>仓储数据变了（不管是哪个页面改的），重新读取。</summary>
        private void Warehouse_Changed(object sender, EventArgs e)
        {
            Refresh();
        }

        private static int CountFreeSlots(List<StorageSlot> slots, int column)
        {
            int count = 0;
            foreach (StorageSlot slot in slots)
            {
                if (slot.Column == column && slot.State == SlotState.Empty)
                {
                    count++;
                }
            }
            return count;
        }

        private void ShowMessage(string text, bool isError)
        {
            Message = text;
            MessageIsError = isError;
        }

        private static bool Confirm(string title, string text)
        {
            MessageBoxResult answer = MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return answer == MessageBoxResult.Yes;
        }
    }
}
