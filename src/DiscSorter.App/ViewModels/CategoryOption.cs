using System.Collections.Generic;
using DiscSorter.Core.Models;

namespace DiscSorter.App.ViewModels
{
    /// <summary>下拉框里的一个类别选项：Value 是类别，Name 是显示的中文名。</summary>
    public class CategoryOption
    {
        public CategoryOption(DefectCategory value)
        {
            Value = value;
            Name = DefectCategories.GetName(value);
        }

        public DefectCategory Value { get; private set; }

        public string Name { get; private set; }

        /// <summary>全部 5 个类别的选项列表。</summary>
        public static List<CategoryOption> CreateAll()
        {
            List<CategoryOption> options = new List<CategoryOption>();
            foreach (DefectCategory category in DefectCategories.All)
            {
                options.Add(new CategoryOption(category));
            }
            return options;
        }
    }
}
