namespace DiscSorter.Core.Plc
{
    /// <summary>PLC 报警码，与总计划第 5.3 节的编码约定一致。</summary>
    public static class PlcCodes
    {
        public const int ErrNone = 0;
        public const int ErrHostOffline = 1;
        public const int ErrResultTimeout = 2;
        public const int ErrJobMismatch = 3;
        public const int ErrInvalidSlot = 4;
        public const int ErrNoFreeSlot = 5;
        public const int ErrServo = 6;
        public const int ErrStepTimeout = 7;
        public const int ErrEmergencyStop = 8;

        /// <summary>报警码的中文含义。</summary>
        public static string Describe(int errCode)
        {
            switch (errCode)
            {
                case ErrNone:
                    return "正常";
                case ErrHostOffline:
                    return "上位机离线";
                case ErrResultTimeout:
                    return "等识别结果超时";
                case ErrJobMismatch:
                    return "任务号不符";
                case ErrInvalidSlot:
                    return "目标库位非法或已占用";
                case ErrNoFreeSlot:
                    return "无可用库位，需人工取件";
                case ErrServo:
                    return "伺服故障";
                case ErrStepTimeout:
                    return "步超时";
                case ErrEmergencyStop:
                    return "急停";
                default:
                    return "未知报警 " + errCode;
            }
        }
    }
}
