namespace DiscSorter.App.Helpers
{
    /// <summary>
    /// 命令行参数（平时直接运行不用加任何参数）：
    ///   --data-dir 目录     用另一个数据目录，演示时不影响正式数据
    ///   --design            用设计器示例数据显示界面（不连数据库和 HALCON）
    ///   --autostart         启动后自动开始仿真运行
    ///   --screenshot 文件   启动后截图保存并退出（写文档用）
    ///   --page 序号         截图前切换到第几个页签（0 监控，1 视觉调试，2 仓储管理，3 设置）
    ///   --wait 毫秒         截图前多等一会儿
    /// </summary>
    public class StartupOptions
    {
        public string DataFolder { get; set; }

        public bool DesignPreview { get; set; }

        public bool AutoStart { get; set; }

        public string ScreenshotPath { get; set; }

        public int PageIndex { get; set; }

        public int WaitMs { get; set; }

        public static StartupOptions Parse(string[] args)
        {
            StartupOptions options = new StartupOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string next = null;
                if (i + 1 < args.Length)
                {
                    next = args[i + 1];
                }

                switch (args[i])
                {
                    case "--data-dir":
                        options.DataFolder = next;
                        break;
                    case "--design":
                        options.DesignPreview = true;
                        break;
                    case "--autostart":
                        options.AutoStart = true;
                        break;
                    case "--screenshot":
                        options.ScreenshotPath = next;
                        break;
                    case "--page":
                        int page;
                        if (int.TryParse(next, out page))
                        {
                            options.PageIndex = page;
                        }
                        break;
                    case "--wait":
                        int wait;
                        if (int.TryParse(next, out wait))
                        {
                            options.WaitMs = wait;
                        }
                        break;
                }
            }
            return options;
        }
    }
}
