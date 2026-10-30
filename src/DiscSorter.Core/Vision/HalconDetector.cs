using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using HalconDotNet;

namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 用 HDevEngine 调用 halcon\DiscDefect.hdev 里的过程（过程接口见 docs\上位机\HALCON过程接口约定.md）。
    ///
    /// "渐进调用"：课程写到哪一步，就调用到哪一步——
    ///   有 dd_detect      → 只调用它，得到全部结果；
    ///   没有 dd_detect    → 有 dd_locate_disk 就找圆，再有 dd_segment 就接着分割；
    ///   什么都没有        → 只显示原图。
    /// 在 HDevelop 里改完程序保存后，调用 Load() 重新加载即可，上位机不用重新编译。
    /// </summary>
    public class HalconDetector : IDefectDetector
    {
        public const string DefaultParamsProcedure = "dd_default_params";
        public const string LocateDiskProcedure = "dd_locate_disk";
        public const string SegmentProcedure = "dd_segment";
        public const string DetectProcedure = "dd_detect";

        private HDevProgram program;
        private List<string> procedureNames = new List<string>();
        private HDict parameters = new HDict();

        /// <summary>当前加载的程序路径，没有加载时为 null。</summary>
        public string ProgramPath { get; private set; }

        public bool IsLoaded
        {
            get { return program != null; }
        }

        /// <summary>程序里的本地过程名。</summary>
        public List<string> ProcedureNames
        {
            get { return procedureNames; }
        }

        /// <summary>按已有的过程判断算法能做到哪一步。</summary>
        public DetectionLevel Capability
        {
            get
            {
                if (HasProcedure(DetectProcedure))
                {
                    return DetectionLevel.Full;
                }
                if (HasProcedure(LocateDiskProcedure) && HasProcedure(SegmentProcedure))
                {
                    return DetectionLevel.Segment;
                }
                if (HasProcedure(LocateDiskProcedure))
                {
                    return DetectionLevel.Disk;
                }
                return DetectionLevel.None;
            }
        }

        public bool HasProcedure(string name)
        {
            return procedureNames.Contains(name);
        }

        /// <summary>
        /// 加载（或重新加载）程序，返回 dd_default_params 给出的默认参数字典；
        /// 程序里没有 dd_default_params 时返回空字典。加载失败时抛出异常，原来的程序保持不变。
        /// </summary>
        public HDict Load(string programPath)
        {
            if (!File.Exists(programPath))
            {
                throw new FileNotFoundException("找不到 HALCON 程序文件", programPath);
            }

            HDevProgram newProgram = new HDevProgram(programPath);
            HTuple names = newProgram.GetLocalProcedureNames();

            Unload();
            program = newProgram;
            ProgramPath = programPath;
            for (int i = 0; i < names.Length; i++)
            {
                procedureNames.Add(names[i].S);
            }

            if (!HasProcedure(DefaultParamsProcedure))
            {
                return new HDict();
            }
            HDevProcedureCall call = CreateCall(DefaultParamsProcedure);
            try
            {
                call.Execute();
                HTuple output = call.GetOutputCtrlParamTuple("Params");
                return new HDict(output.H);
            }
            finally
            {
                call.Dispose();
            }
        }

        /// <summary>卸载程序，之后只显示原图。</summary>
        public void Unload()
        {
            if (program != null)
            {
                program.Dispose();
                program = null;
            }
            ProgramPath = null;
            procedureNames.Clear();
        }

        /// <summary>设置传给各过程的参数字典（内部保存一份副本）。</summary>
        public void SetParameters(HDict values)
        {
            HDict copy = values.CopyDict(new HTuple(), new HTuple());
            parameters.Dispose();
            parameters = copy;
        }

        /// <summary>识别一张图。出错时不抛异常，而是在结果的 HasError 和 Message 里说明。</summary>
        public DetectionResult Detect(HImage image, string imagePath)
        {
            DetectionResult result = new DetectionResult();
            result.Image = image.CopyImage();
            result.ImagePath = imagePath;

            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                if (HasProcedure(DetectProcedure))
                {
                    RunDetect(result);
                }
                else
                {
                    RunStages(result);
                }

                if (result.Level == DetectionLevel.None)
                {
                    if (IsLoaded)
                    {
                        result.Message = "程序中还没有 dd_locate_disk，只显示原图";
                    }
                    else
                    {
                        result.Message = "未加载 HALCON 程序，只显示原图";
                    }
                }
            }
            catch (HDevEngineException ex)
            {
                result.HasError = true;
                result.Message = ex.ProcedureName + " 第 " + ex.LineNumber + " 行出错：" + ex.Message;
            }
            catch (HalconException ex)
            {
                result.HasError = true;
                result.Message = "HALCON 出错：" + ex.Message;
            }
            watch.Stop();
            result.ElapsedMs = watch.Elapsed.TotalMilliseconds;
            return result;
        }

        /// <summary>完整识别：只调用 dd_detect。</summary>
        private void RunDetect(DetectionResult result)
        {
            HDevProcedureCall call = CreateCall(DetectProcedure);
            try
            {
                call.SetInputIconicParamObject("Image", result.Image);
                call.SetInputCtrlParamTuple("Params", new HTuple(parameters));
                call.Execute();

                result.Level = DetectionLevel.Full;
                result.Disk = call.GetOutputIconicParamRegion("Disk");
                result.RawRegion = call.GetOutputIconicParamRegion("RawRegion");
                result.Candidates = call.GetOutputIconicParamRegion("Candidates");
                result.Defects = call.GetOutputIconicParamRegion("Defects");
                ReadCircle(call, result);

                HTuple category = call.GetOutputCtrlParamTuple("Category");
                if (category.Length > 0)
                {
                    result.AlgorithmLabel = category.S.Trim().ToUpper();
                }
                else
                {
                    result.AlgorithmLabel = "NONE";
                }

                HTuple dictHandle = call.GetOutputCtrlParamTuple("Result");
                if (dictHandle.Length > 0)
                {
                    HDict dict = new HDict(dictHandle.H);
                    result.Features = ReadFeatures(dict);
                    result.DefectTypes = ReadDefectTypes(result.Features);
                }
            }
            finally
            {
                call.Dispose();
            }
        }

        /// <summary>分步识别：有 dd_locate_disk 就找圆，再有 dd_segment 就分割。</summary>
        private void RunStages(DetectionResult result)
        {
            if (!HasProcedure(LocateDiskProcedure))
            {
                return;
            }

            HDevProcedureCall locate = CreateCall(LocateDiskProcedure);
            try
            {
                locate.SetInputIconicParamObject("Image", result.Image);
                locate.SetInputCtrlParamTuple("Params", new HTuple(parameters));
                locate.Execute();
                result.Level = DetectionLevel.Disk;
                result.Disk = locate.GetOutputIconicParamRegion("Disk");
                ReadCircle(locate, result);
            }
            finally
            {
                locate.Dispose();
            }

            if (!result.Found)
            {
                result.Message = "没有找到圆盘";
                return;
            }
            if (!HasProcedure(SegmentProcedure))
            {
                return;
            }

            HDevProcedureCall segment = CreateCall(SegmentProcedure);
            try
            {
                segment.SetInputIconicParamObject("Image", result.Image);
                segment.SetInputIconicParamObject("Disk", result.Disk);
                segment.SetInputCtrlParamTuple("Params", new HTuple(parameters));
                segment.SetInputCtrlParamTuple("Radius", new HTuple(result.Radius));
                segment.Execute();
                result.Level = DetectionLevel.Segment;
                result.RawRegion = segment.GetOutputIconicParamRegion("RawRegion");
                result.Candidates = segment.GetOutputIconicParamRegion("Candidates");
            }
            finally
            {
                segment.Dispose();
            }
        }

        private HDevProcedureCall CreateCall(string procedureName)
        {
            HDevProcedure procedure = new HDevProcedure(program, procedureName);
            return new HDevProcedureCall(procedure);
        }

        private static void ReadCircle(HDevProcedureCall call, DetectionResult result)
        {
            result.Found = ReadNumber(call.GetOutputCtrlParamTuple("Found")) > 0;
            result.Row = ReadNumber(call.GetOutputCtrlParamTuple("Row"));
            result.Column = ReadNumber(call.GetOutputCtrlParamTuple("Column"));
            result.Radius = ReadNumber(call.GetOutputCtrlParamTuple("Radius"));
        }

        /// <summary>取元组第一个数（整数或实数都行），空元组返回 0。</summary>
        private static double ReadNumber(HTuple tuple)
        {
            if (tuple.Length == 0)
            {
                return 0;
            }
            return tuple.TupleSelect(0).TupleReal().D;
        }

        /// <summary>
        /// 把 Result 字典转成特征表：字典的每个键是一列，第 i 个元素属于第 i 个缺陷。
        /// "type" 列放在最前面，其余列按名称排序。
        /// </summary>
        public static FeatureTable ReadFeatures(HDict dict)
        {
            FeatureTable table = new FeatureTable();
            HTuple keys = dict.GetDictParam("keys", new HTuple());

            List<string> otherColumns = new List<string>();
            bool hasType = false;
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i].S;
                if (key == "type")
                {
                    hasType = true;
                }
                else
                {
                    otherColumns.Add(key);
                }
            }
            otherColumns.Sort(StringComparer.Ordinal);
            if (hasType)
            {
                table.Columns.Add("type");
            }
            table.Columns.AddRange(otherColumns);

            // 先取出每一列的元组，并找出最长的一列作为行数
            List<HTuple> values = new List<HTuple>();
            int rowCount = 0;
            foreach (string column in table.Columns)
            {
                HTuple value = dict.GetDictTuple(column);
                values.Add(value);
                if (value.Length > rowCount)
                {
                    rowCount = value.Length;
                }
            }

            for (int row = 0; row < rowCount; row++)
            {
                List<string> cells = new List<string>();
                foreach (HTuple value in values)
                {
                    if (row < value.Length)
                    {
                        cells.Add(FormatElement(value.TupleSelect(row)));
                    }
                    else
                    {
                        cells.Add("");
                    }
                }
                table.Rows.Add(cells);
            }
            return table;
        }

        /// <summary>把元组的一个元素转成文字：实数保留 3 位小数，整数和字符串原样。</summary>
        public static string FormatElement(HTuple element)
        {
            switch (element.Type)
            {
                case HTupleType.DOUBLE:
                    return element.D.ToString("0.###", CultureInfo.InvariantCulture);
                case HTupleType.INTEGER:
                case HTupleType.LONG:
                    return element.L.ToString(CultureInfo.InvariantCulture);
                case HTupleType.STRING:
                    return element.S;
                default:
                    return element.ToString();
            }
        }

        private static List<string> ReadDefectTypes(FeatureTable table)
        {
            List<string> types = new List<string>();
            int typeColumn = table.Columns.IndexOf("type");
            if (typeColumn < 0)
            {
                return types;
            }
            foreach (List<string> row in table.Rows)
            {
                types.Add(row[typeColumn]);
            }
            return types;
        }
    }
}
