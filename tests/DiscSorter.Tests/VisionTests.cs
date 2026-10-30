using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using DiscSorter.Core.Common;
using DiscSorter.Core.Vision;
using HalconDotNet;
using Xunit;

namespace DiscSorter.Tests
{
    /// <summary>HALCON 相关测试，需要本机装有 HALCON 运行库。用测试桩 contract_stub.hdev，不依赖课程进度。</summary>
    [Trait("Category", "Halcon")]
    public class VisionTests : IDisposable
    {
        private static readonly string StubPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "contract_stub.hdev");
        private static readonly string SamplePath = Path.Combine(ProjectPaths.FindRoot(AppContext.BaseDirectory), "data", "samples", "Q_1.jpg");

        private readonly string folder = Path.Combine(Path.GetTempPath(), "DiscSorterVisionTests", Guid.NewGuid().ToString("N"));
        private readonly HalconDetector detector = new HalconDetector();

        public VisionTests()
        {
            Directory.CreateDirectory(folder);
        }

        public void Dispose()
        {
            detector.Unload();
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void StubProgramListsContractProcedures()
        {
            detector.Load(StubPath);

            Assert.True(detector.HasProcedure("dd_default_params"));
            Assert.True(detector.HasProcedure("dd_locate_disk"));
            Assert.True(detector.HasProcedure("dd_segment"));
            Assert.True(detector.HasProcedure("dd_detect"));
            Assert.False(detector.HasProcedure("main"));
            Assert.Equal(DetectionLevel.Full, detector.Capability);
        }

        [Fact]
        public void FullProgramGivesCategoryLayersAndFeatureTable()
        {
            HDict defaults = detector.Load(StubPath);
            detector.SetParameters(defaults);

            using (HImage image = new HImage(SamplePath))
            using (DetectionResult result = detector.Detect(image, SamplePath))
            {
                Assert.Equal(DetectionLevel.Full, result.Level);
                Assert.False(result.HasError, result.Message);
                Assert.True(result.Found);
                Assert.Equal(225.0, result.Row, 3);
                Assert.Equal("H", result.AlgorithmLabel);
                Assert.Equal(new List<string> { "type", "area", "column", "row" }, result.Features.Columns);
                Assert.Equal(result.DefectCount, result.Features.Rows.Count);
                Assert.True(result.DefectCount > 0);
                foreach (string type in result.DefectTypes)
                {
                    Assert.Equal("H", type);
                }
            }
        }

        [Theory]
        [InlineData(new string[] { "dd_default_params", "dd_locate_disk" }, DetectionLevel.Disk)]
        [InlineData(new string[] { "dd_default_params", "dd_locate_disk", "dd_segment" }, DetectionLevel.Segment)]
        [InlineData(new string[0], DetectionLevel.None)]
        public void PartialProgramsAreCalledStepByStep(string[] procedures, DetectionLevel expected)
        {
            HDict defaults = detector.Load(WriteStubVariant(procedures));
            detector.SetParameters(defaults);

            using (HImage image = new HImage(SamplePath))
            using (DetectionResult result = detector.Detect(image, SamplePath))
            {
                Assert.Equal(expected, detector.Capability);
                Assert.Equal(expected, result.Level);
                Assert.Null(result.AlgorithmLabel);
                Assert.Equal(expected != DetectionLevel.None, result.Found);
                Assert.Equal(expected == DetectionLevel.Segment, result.CandidateCount > 0);
            }
        }

        [Fact]
        public void WithoutProgramOnlyTheImageIsShown()
        {
            using (HImage image = new HImage(SamplePath))
            using (DetectionResult result = detector.Detect(image, SamplePath))
            {
                Assert.Equal(DetectionLevel.None, result.Level);
                Assert.Contains("未加载", result.Message);
                Assert.True(result.Image.IsInitialized());
            }
        }

        [Fact]
        public void ProcedureErrorIsReportedInsteadOfThrown()
        {
            detector.Load(StubPath);
            detector.SetParameters(new HDict());   // 空参数：dd_segment 读不到 stub_threshold 会出错

            using (HImage image = new HImage(SamplePath))
            using (DetectionResult result = detector.Detect(image, SamplePath))
            {
                Assert.True(result.HasError);
                Assert.Contains("dd_segment", result.Message);
            }
        }

        [Fact]
        public void ParametersMergeSavedValuesAndRoundTripThroughJson()
        {
            HDict defaults = detector.Load(StubPath);
            Dictionary<string, JsonElement> saved = new Dictionary<string, JsonElement>();
            saved["stub_threshold"] = JsonSerializer.SerializeToElement(95);
            saved["min_component_area"] = JsonSerializer.SerializeToElement("不是数字");
            saved["unknown_key"] = JsonSerializer.SerializeToElement(1);
            Dictionary<string, ParameterMeta> metas = new Dictionary<string, ParameterMeta>();
            ParameterMeta meta = new ParameterMeta();
            meta.Label = "测试阈值";
            meta.Min = 0;
            meta.Max = 255;
            metas["stub_threshold"] = meta;

            List<DetectorParameter> parameters = ParameterStore.Merge(defaults, saved, metas);

            DetectorParameter threshold = parameters.Find(delegate (DetectorParameter p) { return p.Key == "stub_threshold"; });
            Assert.Equal("95", threshold.Value);
            Assert.Equal("80", threshold.DefaultValue);
            Assert.Equal(ParameterKind.Integer, threshold.Kind);
            Assert.Equal("测试阈值", threshold.Label);
            DetectorParameter area = parameters.Find(delegate (DetectorParameter p) { return p.Key == "min_component_area"; });
            Assert.Equal("20", area.Value);   // 保存的值类型不对，用默认值
            Assert.Equal(2, parameters.Count);

            HDict dict = ParameterStore.BuildDict(defaults, parameters);
            Assert.Equal(95, dict.GetDictTuple("stub_threshold").I);

            string path = Path.Combine(folder, ParameterStore.OverridesFileName);
            ParameterStore.SaveValues(path, parameters);
            List<DetectorParameter> reloaded = ParameterStore.Merge(defaults, ParameterStore.LoadSavedValues(path), metas);
            Assert.Equal("95", reloaded[0].Key == "stub_threshold" ? reloaded[0].Value : reloaded[1].Value);
        }

        [Fact]
        public void InvalidParameterTextIsRejected()
        {
            DetectorParameter parameter = new DetectorParameter();
            parameter.Key = "dyn_threshold_offset";
            parameter.Kind = ParameterKind.Integer;
            parameter.Value = "2.5";
            parameter.Label = "动态阈值偏移";

            FormatException error = Assert.Throws<FormatException>(() => ParameterStore.Parse(parameter));
            Assert.Contains("动态阈值偏移", error.Message);
        }

        [Fact]
        public void ProjectMetaFileIsValid()
        {
            string path = Path.Combine(ProjectPaths.FindRoot(AppContext.BaseDirectory), "halcon", ParameterStore.MetaFileName);

            Dictionary<string, ParameterMeta> metas = ParameterStore.LoadMetas(path);

            Assert.True(metas.ContainsKey("dyn_threshold_offset"));
            foreach (ParameterMeta meta in metas.Values)
            {
                // 文字参数（例如 darkness_mode）没有范围，min、max 为空
                if (meta.Min == null || meta.Max == null)
                {
                    continue;
                }
                Assert.True(meta.Min < meta.Max);
            }
        }

        [Fact]
        public void ImageFolderCyclesInNameOrder()
        {
            File.WriteAllBytes(Path.Combine(folder, "b.png"), new byte[0]);
            File.WriteAllBytes(Path.Combine(folder, "a.jpg"), new byte[0]);
            File.WriteAllBytes(Path.Combine(folder, "c.txt"), new byte[0]);
            File.WriteAllBytes(Path.Combine(folder, "C.BMP"), new byte[0]);
            ImageFolder images = new ImageFolder();

            images.OpenFile(Path.Combine(folder, "b.png"));

            Assert.Equal(3, images.Files.Count);
            Assert.Equal("b.png · 2 / 3", images.Description);
            Assert.Equal("b.png", Path.GetFileName(images.TakeNext()));
            images.MoveNext();
            Assert.Equal("a.jpg", Path.GetFileName(images.CurrentPath));
            images.MovePrevious();
            Assert.Equal("C.BMP", Path.GetFileName(images.CurrentPath));
        }

        /// <summary>从测试桩生成只含部分过程的 .hdev，模拟课程进行到一半的状态。</summary>
        private string WriteStubVariant(string[] keepProcedures)
        {
            XDocument document = XDocument.Load(StubPath);
            List<XElement> procedures = new List<XElement>(document.Root.Elements("procedure"));
            foreach (XElement procedure in procedures)
            {
                string name = (string)procedure.Attribute("name");
                if (name == "main")
                {
                    procedure.Element("body").Elements("l").Remove();
                }
                else if (Array.IndexOf(keepProcedures, name) < 0)
                {
                    procedure.Remove();
                }
            }
            string path = Path.Combine(folder, "variant_" + keepProcedures.Length + ".hdev");
            document.Save(path);
            return path;
        }
    }
}
