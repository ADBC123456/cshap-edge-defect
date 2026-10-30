using System;

namespace DiscSorter.Core.Workflow
{
    /// <summary>分拣流程的一条提示或报警（显示在监控页的"流程日志"里）。</summary>
    public class SortingMessageEventArgs : EventArgs
    {
        public SortingMessageEventArgs(string text, bool isAlarm)
        {
            Time = DateTime.Now;
            Text = text;
            IsAlarm = isAlarm;
        }

        public DateTime Time { get; private set; }

        public string Text { get; private set; }

        /// <summary>是否是报警（界面上用红色显示）。</summary>
        public bool IsAlarm { get; private set; }
    }
}
