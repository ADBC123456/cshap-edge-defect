using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscSorter.Core.Models;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 一个库位格子的显示数据（SlotCard / SlotCell 控件绑定它）。
    ///
    /// 关于 CommunityToolkit.Mvvm 的两个特性：
    ///   [ObservableProperty] private SlotState state;
    ///       编译时自动生成公开属性 State（首字母大写）。给 State 赋新值时会自动通知界面刷新。
    ///   [NotifyPropertyChangedFor(nameof(StateText))]
    ///       State 变化时，顺便通知界面 StateText 也变了（StateText 是根据 State 算出来的）。
    ///   [RelayCommand] private void Clear()
    ///       编译时自动生成命令 ClearCommand，XAML 里按钮写 Command="{Binding ClearCommand}"。
    /// </summary>
    public partial class SlotViewModel : ObservableObject
    {
        private readonly WarehouseViewModel owner;

        /// <summary>设计器预览用的构造函数：显示一个已占用的 Q 库位。</summary>
        public SlotViewModel()
        {
            SlotNo = 1;
            state = SlotState.Occupied;
            category = DefectCategory.Q;
            updatedAt = DateTime.Now;
        }

        /// <param name="owner">所在的仓储页，按钮点击后交给它处理</param>
        /// <param name="slotNo">库位号 1～12</param>
        public SlotViewModel(WarehouseViewModel owner, int slotNo)
        {
            this.owner = owner;
            SlotNo = slotNo;
            state = SlotState.Empty;
        }

        public int SlotNo { get; private set; }

        /// <summary>例如 "库位 1 · 行 1"。</summary>
        public string Title
        {
            get { return "库位 " + SlotNo + " · 行 " + SlotNumbers.GetRow(SlotNo); }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText))]
        [NotifyPropertyChangedFor(nameof(CanClear))]
        [NotifyPropertyChangedFor(nameof(IsNeedsCheck))]
        private SlotState state;

        /// <summary>库位里工件的类别，空闲时为 null。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CategoryText))]
        private DefectCategory? category;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UpdatedText))]
        private DateTime? updatedAt;

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case SlotState.Empty:
                        return "空闲";
                    case SlotState.Reserved:
                        return "执行中";
                    case SlotState.Occupied:
                        return "已占用";
                    case SlotState.NeedsCheck:
                        return "待确认";
                    default:
                        return State.ToString();
                }
            }
        }

        public string CategoryText
        {
            get
            {
                if (Category == null)
                {
                    return "—";
                }
                return DefectCategories.GetName(Category.Value);
            }
        }

        public string UpdatedText
        {
            get
            {
                if (UpdatedAt == null)
                {
                    return "";
                }
                return UpdatedAt.Value.ToString("MM-dd HH:mm");
            }
        }

        /// <summary>已占用的库位才能出库。</summary>
        public bool CanClear
        {
            get { return State == SlotState.Occupied; }
        }

        /// <summary>待确认的库位显示"已入库 / 未入库"按钮。</summary>
        public bool IsNeedsCheck
        {
            get { return State == SlotState.NeedsCheck; }
        }

        /// <summary>用数据库读出来的库位刷新显示。</summary>
        public void Update(StorageSlot slot)
        {
            State = slot.State;
            Category = slot.Category;
            UpdatedAt = slot.UpdatedAt;
        }

        /// <summary>"出库"按钮。</summary>
        [RelayCommand]
        private void Clear()
        {
            if (owner != null)
            {
                owner.ClearSlot(this);
            }
        }

        /// <summary>"已入库"按钮（待确认时显示）。</summary>
        [RelayCommand]
        private void ConfirmPlaced()
        {
            if (owner != null)
            {
                owner.ResolveSlot(this, true);
            }
        }

        /// <summary>"未入库"按钮（待确认时显示）。</summary>
        [RelayCommand]
        private void ConfirmEmpty()
        {
            if (owner != null)
            {
                owner.ResolveSlot(this, false);
            }
        }
    }
}
