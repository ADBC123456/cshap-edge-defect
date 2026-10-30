namespace DiscSorter.Core.Plc
{
    /// <summary>
    /// PLC → 上位机的状态（对应总计划里的 PLC_IF 结构）。
    /// 仿真 PLC 和以后的 OPC UA 客户端都给出这个类型，分拣流程不关心状态是从哪里来的。
    /// </summary>
    public class PlcStatus
    {
        /// <summary>是否连接上 PLC。</summary>
        public bool Connected { get; set; }

        /// <summary>是否在自动运行。</summary>
        public bool AutoRunning { get; set; }

        /// <summary>PLC 自动流程的步号。</summary>
        public int Step { get; set; }

        /// <summary>报警码，0 表示正常，含义见 PlcCodes。</summary>
        public int ErrCode { get; set; }

        /// <summary>工件到位（检测工位上有工件）。</summary>
        public bool PartPresent { get; set; }

        /// <summary>请求检测：PLC 置 1 后，上位机去拍照识别。</summary>
        public bool InspectRequest { get; set; }

        /// <summary>任务号，每来一件加 1。</summary>
        public long JobId { get; set; }

        /// <summary>上位机写入的"结果有效"标志（回读显示用）。</summary>
        public bool ResultValid { get; set; }

        /// <summary>正在搬运。</summary>
        public bool SortBusy { get; set; }

        /// <summary>放置完成：PLC 置 1 后，上位机确认入库。</summary>
        public bool SortDone { get; set; }

        /// <summary>实际放入的库位。</summary>
        public int DoneSlot { get; set; }

        /// <summary>完成的是哪个任务。</summary>
        public long DoneJobId { get; set; }

        /// <summary>心跳，PLC 每个周期加 1，用来判断 PLC 是否还活着。</summary>
        public int Heartbeat { get; set; }

        /// <summary>复制一份。分拣流程要比较"上一次"和"这一次"的状态，所以需要副本。</summary>
        public PlcStatus Clone()
        {
            return (PlcStatus)MemberwiseClone();
        }
    }
}
