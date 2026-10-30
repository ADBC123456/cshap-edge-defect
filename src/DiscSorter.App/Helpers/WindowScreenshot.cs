using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DiscSorter.App.Helpers
{
    /// <summary>
    /// 启动后截图并退出（命令行 --screenshot 用，写文档时截界面图）。
    /// </summary>
    public class WindowScreenshot
    {
        private readonly Window window;
        private readonly string path;
        private readonly DispatcherTimer timer = new DispatcherTimer();

        private WindowScreenshot(Window window, string path)
        {
            this.window = window;
            this.path = path;
        }

        /// <summary>切到第 pageIndex 个页签，等 waitMs 毫秒（再加 1 秒让界面画完）后截图，然后退出程序。</summary>
        public static void SaveLater(Window window, string path, int pageIndex, int waitMs)
        {
            TabControl tabs = FindTabControl(window);
            if (tabs != null && pageIndex >= 0 && pageIndex < tabs.Items.Count)
            {
                tabs.SelectedIndex = pageIndex;
            }

            WindowScreenshot screenshot = new WindowScreenshot(window, path);
            screenshot.timer.Interval = TimeSpan.FromMilliseconds(waitMs + 1000);
            screenshot.timer.Tick += screenshot.Timer_Tick;
            screenshot.timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            timer.Stop();
            FrameworkElement content = (FrameworkElement)window.Content;
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
            Application.Current.Shutdown(0);
        }

        private static TabControl FindTabControl(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                TabControl tabs = child as TabControl;
                if (tabs != null)
                {
                    return tabs;
                }
                TabControl found = FindTabControl(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
