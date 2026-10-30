using System;
using System.Collections.Generic;
using DiscSorter.Core.Vision;
using Xunit;

namespace DiscSorter.Tests
{
    /// <summary>
    /// 海康相机测试，需要本机装有 MVS 客户端或 Runtime。
    /// 没接相机时枚举结果为 0 台，这也是正常的：这里只验证 SDK 能正常加载、枚举不出错。
    /// </summary>
    [Trait("Category", "Hardware")]
    public class CameraTests
    {
        [Fact]
        public void EnumerateDoesNotThrow()
        {
            List<CameraInfo> cameras = HikCamera.Enumerate();

            Assert.NotNull(cameras);
            foreach (CameraInfo camera in cameras)
            {
                Assert.False(string.IsNullOrEmpty(camera.SerialNumber));
                Assert.Contains(camera.SerialNumber, camera.DisplayText);
            }
        }

        [Fact]
        public void GrabWithoutOpeningThrowsClearError()
        {
            HikCamera camera = new HikCamera();
            string path;
            string message = null;
            try
            {
                camera.Grab(out path);
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }

            Assert.NotNull(message);
            Assert.Contains("没有打开", message);
            Assert.False(camera.IsReady);
        }
    }
}
