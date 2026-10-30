using System;

namespace DiscSorter.Core.Plc
{
    /// <summary>
    /// 通讯日志的一条：什么时间、谁写给谁、哪个变量、变成了什么值、是什么意思。
    /// 例如：10:21:03.150  PLC → 上位机  tSTEP  6→7  等待识别结果
    /// </summary>
    public class PlcLogEntry
    {
        public const string FromPlc = "PLC → 上位机";
        public const string ToPlc = "上位机 → PLC";
        public const string Host = "上位机";

        public DateTime Time { get; set; }

        /// <summary>方向：FromPlc、ToPlc 或 Host（上位机内部的判断）。</summary>
        public string Direction { get; set; }

        public string Variable { get; set; }

        public string Value { get; set; }

        public string Note { get; set; }

        /// <summary>显示用的时间，精确到毫秒。</summary>
        public string TimeText
        {
            get { return Time.ToString("HH:mm:ss.fff"); }
        }
    }
}
