using HalconDotNet;

namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 图像来源：分拣流程每来一件工件就调用一次 Grab() 取一张图。
    /// 有两个实现：ImageFolder（图片文件夹回放，调试用）和 HikCamera（海康相机）。
    /// </summary>
    public interface IImageSource
    {
        /// <summary>显示用的名称，例如"图片文件夹 data\station"、"海康相机 MV-CS050-10GC"。</summary>
        string SourceName { get; }

        /// <summary>现在能不能取图（文件夹里有图片 / 相机已打开）。</summary>
        bool IsReady { get; }

        /// <summary>
        /// 取一张图。imagePath 返回图片文件的路径：文件夹回放时是原图路径，
        /// 相机取图时是保存后的路径（没有保存则为 null）。取不到时抛出异常。
        /// </summary>
        HImage Grab(out string imagePath);
    }
}
