using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using HalconDotNet;

namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 算法参数的来源和保存：
    ///   默认值   —— .hdev 里的 dd_default_params 过程给出；
    ///   调整后的值 —— 保存在 .hdev 同目录的 detector.json；
    ///   中文名和范围 —— 放在 .hdev 同目录的 detector_meta.json。
    /// </summary>
    public static class ParameterStore
    {
        public const string OverridesFileName = "detector.json";
        public const string MetaFileName = "detector_meta.json";

        public static string GetOverridesPath(string programPath)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(programPath)), OverridesFileName);
        }

        public static string GetMetaPath(string programPath)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(programPath)), MetaFileName);
        }

        /// <summary>
        /// 合并出界面上的参数列表：以默认参数为准，用 detector.json 里保存的值覆盖，再配上中文名和范围。
        /// 只列出"单个数字"或"单个字符串"的参数；元组等复杂值不显示，但仍会原样传给过程。
        /// </summary>
        public static List<DetectorParameter> Merge(HDict defaults, Dictionary<string, JsonElement> savedValues, Dictionary<string, ParameterMeta> metas)
        {
            List<DetectorParameter> result = new List<DetectorParameter>();
            HTuple keys = defaults.GetDictParam("keys", new HTuple());
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i].S;
                HTuple value = defaults.GetDictTuple(key);
                if (value.Length != 1)
                {
                    continue;
                }

                ParameterKind kind;
                if (value.Type == HTupleType.INTEGER || value.Type == HTupleType.LONG)
                {
                    kind = ParameterKind.Integer;
                }
                else if (value.Type == HTupleType.DOUBLE)
                {
                    kind = ParameterKind.Real;
                }
                else if (value.Type == HTupleType.STRING)
                {
                    kind = ParameterKind.Text;
                }
                else
                {
                    continue;
                }

                DetectorParameter parameter = new DetectorParameter();
                parameter.Key = key;
                parameter.Kind = kind;
                parameter.DefaultValue = HalconDetector.FormatElement(value);
                parameter.Value = parameter.DefaultValue;

                // 用保存过的值覆盖默认值（类型对不上就不用）
                if (savedValues.ContainsKey(key))
                {
                    string savedText = ReadJsonValue(savedValues[key], kind);
                    if (savedText != null)
                    {
                        parameter.Value = savedText;
                    }
                }

                if (metas.ContainsKey(key))
                {
                    ParameterMeta meta = metas[key];
                    parameter.Label = meta.Label;
                    parameter.Min = meta.Min;
                    parameter.Max = meta.Max;
                    parameter.Step = meta.Step;
                    parameter.Description = meta.Description;
                }
                result.Add(parameter);
            }
            return result;
        }

        /// <summary>
        /// 生成传给过程的参数字典：复制一份默认字典，再写入各参数的当前值。
        /// 某个值不是合法数字时抛出 FormatException。
        /// </summary>
        public static HDict BuildDict(HDict defaults, List<DetectorParameter> parameters)
        {
            HDict dict = defaults.CopyDict(new HTuple(), new HTuple());
            foreach (DetectorParameter parameter in parameters)
            {
                dict.SetDictTuple(parameter.Key, Parse(parameter));
            }
            return dict;
        }

        /// <summary>把参数的文字值按类型转成 HALCON 元组。</summary>
        public static HTuple Parse(DetectorParameter parameter)
        {
            string text = parameter.Value.Trim();
            string name = parameter.Label;
            if (name == null)
            {
                name = parameter.Key;
            }

            if (parameter.Kind == ParameterKind.Integer)
            {
                long integer;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                {
                    return new HTuple(integer);
                }
                throw new FormatException("参数“" + name + "”必须是整数：" + parameter.Value);
            }

            if (parameter.Kind == ParameterKind.Real)
            {
                double real;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out real))
                {
                    return new HTuple(real);
                }
                throw new FormatException("参数“" + name + "”必须是数字：" + parameter.Value);
            }

            return new HTuple(parameter.Value);
        }

        /// <summary>读取 detector.json。文件不存在时返回空字典。</summary>
        public static Dictionary<string, JsonElement> LoadSavedValues(string path)
        {
            Dictionary<string, JsonElement> values = new Dictionary<string, JsonElement>();
            if (!File.Exists(path))
            {
                return values;
            }
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    values[property.Name] = property.Value.Clone();
                }
            }
            return values;
        }

        /// <summary>
        /// 把全部参数的当前值写入 detector.json（不只是改过的），
        /// 这样 HDevelop 里的 main 也能直接读这个文件。
        /// </summary>
        public static void SaveValues(string path, List<DetectorParameter> parameters)
        {
            Dictionary<string, object> values = new Dictionary<string, object>();
            foreach (DetectorParameter parameter in parameters)
            {
                HTuple tuple = Parse(parameter);
                if (parameter.Kind == ParameterKind.Integer)
                {
                    values[parameter.Key] = tuple.L;
                }
                else if (parameter.Kind == ParameterKind.Real)
                {
                    values[parameter.Key] = tuple.D;
                }
                else
                {
                    values[parameter.Key] = tuple.S;
                }
            }

            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = true;
            options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;   // 中文不转义成 \uXXXX
            File.WriteAllText(path, JsonSerializer.Serialize(values, options));
        }

        /// <summary>读取 detector_meta.json。文件不存在时返回空字典。</summary>
        public static Dictionary<string, ParameterMeta> LoadMetas(string path)
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, ParameterMeta>();
            }
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;   // JSON 里是小写 label，C# 属性是 Label
            Dictionary<string, ParameterMeta> metas =
                JsonSerializer.Deserialize<Dictionary<string, ParameterMeta>>(File.ReadAllText(path), options);
            if (metas == null)
            {
                return new Dictionary<string, ParameterMeta>();
            }
            return metas;
        }

        /// <summary>把 JSON 里保存的值转成文字。类型对不上时返回 null。</summary>
        private static string ReadJsonValue(JsonElement element, ParameterKind kind)
        {
            if (kind == ParameterKind.Integer && element.ValueKind == JsonValueKind.Number)
            {
                long integer;
                if (element.TryGetInt64(out integer))
                {
                    return integer.ToString(CultureInfo.InvariantCulture);
                }
                return null;
            }
            if (kind == ParameterKind.Real && element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDouble().ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (kind == ParameterKind.Text && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
            return null;
        }
    }
}
