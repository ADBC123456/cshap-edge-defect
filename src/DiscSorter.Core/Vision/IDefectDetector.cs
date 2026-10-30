using HalconDotNet;

namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 识别接口。分拣流程只依赖这个接口，不关心背后是谁在识别：
    /// 正式运行时是 HalconDetector，单元测试里换成一个"假"的识别器。
    /// </summary>
    public interface IDefectDetector
    {
        DetectionResult Detect(HImage image, string imagePath);
    }
}
