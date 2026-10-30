using System;
using System.Collections.Generic;
using System.IO;
using DiscSorter.Core.Common;
using DiscSorter.Core.Models;
using DiscSorter.Core.Vision;
using HalconDotNet;
using Xunit;

namespace DiscSorter.Tests
{
    /// <summary>
    /// 真实算法回归测试：用上位机同样的方式加载 halcon\DiscDefect.hdev，
    /// 逐张识别 data\station 里的工位图，类别必须等于文件名前缀（例如 Q_1.png 应识别为 Q）。
    /// 改了 .hdev 或参数后跑一遍这个测试，就知道有没有把原来对的图改错。
    /// </summary>
    [Trait("Category", "Halcon")]
    public class AlgorithmTests : IDisposable
    {
        private static readonly string Root = ProjectPaths.FindRoot(AppContext.BaseDirectory);
        private static readonly string ProgramPath = Path.Combine(Root, "halcon", "DiscDefect.hdev");
        private static readonly string StationFolder = Path.Combine(Root, "data", "station");

        private readonly HalconDetector detector = new HalconDetector();

        public void Dispose()
        {
            detector.Unload();
        }

        [Fact]
        public void ProgramHasAllContractProcedures()
        {
            detector.Load(ProgramPath);

            Assert.Equal(DetectionLevel.Full, detector.Capability);
            Assert.True(detector.HasProcedure("dd_locate_disk"));
            Assert.True(detector.HasProcedure("dd_segment"));
        }

        [Fact]
        public void EveryStationImageIsRecognizedCorrectly()
        {
            HDict defaults = detector.Load(ProgramPath);
            detector.SetParameters(defaults);

            ImageFolder images = new ImageFolder();
            images.OpenFolder(StationFolder);
            Assert.Equal(12, images.Files.Count);

            // 把识别错的图都收集起来，最后一次性报出来，方便一眼看出是哪几张
            List<string> failures = new List<string>();
            foreach (string path in images.Files)
            {
                string expected = DefectCategories.GetLabelFromFileName(path);
                using (HImage image = ImageFolder.ReadImage(path))
                using (DetectionResult result = detector.Detect(image, path))
                {
                    if (result.HasError)
                    {
                        failures.Add(Path.GetFileName(path) + "：出错 " + result.Message);
                    }
                    else if (!result.Found)
                    {
                        failures.Add(Path.GetFileName(path) + "：没找到圆");
                    }
                    else if (result.AlgorithmLabel != expected)
                    {
                        failures.Add(Path.GetFileName(path) + "：应为 " + expected + "，识别为 " + result.AlgorithmLabel);
                    }
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void ResultTableHasOneRowPerDefect()
        {
            HDict defaults = detector.Load(ProgramPath);
            detector.SetParameters(defaults);
            string path = Path.Combine(StationFolder, "K_1.png");

            using (HImage image = ImageFolder.ReadImage(path))
            using (DetectionResult result = detector.Detect(image, path))
            {
                Assert.Equal("type", result.Features.Columns[0]);
                Assert.Contains("hole_ratio", result.Features.Columns);
                Assert.Equal(result.DefectCount, result.Features.Rows.Count);
                Assert.Contains("K", result.DefectTypes);
            }
        }
    }
}
