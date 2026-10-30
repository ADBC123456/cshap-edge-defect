using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscSorter.Core.Models;

namespace DiscSorter.App.ViewModels
{
    /// <summary>
    /// 一列的显示数据：列号、已保存的类别、下拉框里正在选的类别、空闲数。
    /// （[ObservableProperty] 和 [RelayCommand] 的说明见 SlotViewModel.cs 开头。）
    /// </summary>
    public partial class ColumnViewModel : ObservableObject
    {
        private readonly WarehouseViewModel owner;

        /// <summary>设计器预览用的构造函数。</summary>
        public ColumnViewModel()
        {
            Column = 1;
            savedCategory = DefectCategory.Q;
            selectedCategory = DefectCategory.Q;
            freeCount = 1;
        }

        public ColumnViewModel(WarehouseViewModel owner, int column)
        {
            this.owner = owner;
            Column = column;
        }

        public int Column { get; private set; }

        /// <summary>例如 "列 1"。</summary>
        public string Title
        {
            get { return "列 " + Column; }
        }

        /// <summary>数据库里已保存的类别。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDirty))]
        [NotifyPropertyChangedFor(nameof(HeaderText))]
        private DefectCategory savedCategory;

        /// <summary>下拉框里正在选的类别，点"保存映射"之前可能和 SavedCategory 不同。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDirty))]
        private DefectCategory selectedCategory;

        /// <summary>这一列还有几个空闲库位。</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FreeText))]
        private int freeCount;

        /// <summary>下拉框改了但还没保存。</summary>
        public bool IsDirty
        {
            get { return SelectedCategory != SavedCategory; }
        }

        /// <summary>监控页库位图的列标题，例如 "列 1 · Q"。</summary>
        public string HeaderText
        {
            get { return "列 " + Column + " · " + SavedCategory; }
        }

        public string FreeText
        {
            get { return "空闲 " + FreeCount + "/3"; }
        }

        /// <summary>"整列出库"按钮。</summary>
        [RelayCommand]
        private void ClearColumn()
        {
            if (owner != null)
            {
                owner.ClearColumn(this);
            }
        }
    }
}
