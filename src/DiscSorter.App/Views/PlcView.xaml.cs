using System.Windows;
using System.Windows.Controls;
using DiscSorter.App.ViewModels;

namespace DiscSorter.App.Views
{
    /// <summary>
    /// PLC 通讯页。界面逻辑都在 PlcViewModel 里，这里只处理密码框：
    /// 为了安全，WPF 的 PasswordBox.Password 不能绑定，只能在代码里读写。
    /// </summary>
    public partial class PlcView : UserControl
    {
        private bool loadingPassword;

        public PlcView()
        {
            InitializeComponent();
            Loaded += PlcView_Loaded;
        }

        /// <summary>页面显示时，把保存的密码填进密码框（显示成圆点）。</summary>
        private void PlcView_Loaded(object sender, RoutedEventArgs e)
        {
            PlcViewModel viewModel = DataContext as PlcViewModel;
            if (viewModel != null && viewModel.Password != null)
            {
                loadingPassword = true;
                PasswordInput.Password = viewModel.Password;
                loadingPassword = false;
            }
        }

        /// <summary>用户改了密码：交给 ViewModel。</summary>
        private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (loadingPassword)
            {
                return;
            }
            PlcViewModel viewModel = DataContext as PlcViewModel;
            if (viewModel != null)
            {
                viewModel.Password = PasswordInput.Password;
            }
        }
    }
}
