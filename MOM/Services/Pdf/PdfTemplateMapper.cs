using System;
using System.Collections.Generic;
using System.Text;
using MOM.Common;
using MOM.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MOM.Services.Pdf
{
    /// <summary>
    /// 按模板把“标签 -> 值”列表映射成英文 key 的 JSON：
    /// key 顺序保持 template 的先后顺序，模板以外的字段丢弃；
    /// extend 里给了值的字段直接使用该值，不再查 PDF；
    /// 模板要求的字段在 PDF 中找不到、且 extend 也没给时，通过 missing 返回。
    /// </summary>
    public static class PdfTemplateMapper
    {
        /// <summary>
        /// 执行映射。支持 "父-子" 形式的字段名（如 "声级-加速行驶车外噪声(dB(A))"），
        /// 用于父标签下挂了多个子标签的合并单元格。
        /// extend 的 key 必须存在于 template，否则抛 TEMPLATE_INVALID。
        /// </summary>
        public static JObject Map(
            IList<PdfKeyValuePair> pairs,
            JObject template,
            JObject extend,
            bool normalize,
            out List<string> missing)
        {
            Dictionary<string, string> lookup = BuildLookup(pairs);
            var result = new JObject();
            var emptyLabels = new List<string>();
            missing = new List<string>();

            foreach (JProperty field in template.Properties())
            {
                string direct;
                if (TryGetExtendValue(extend, field.Name, out direct))
                {
                    result[field.Name] = NormalizeValue(direct, normalize);
                    continue;
                }

                string label = field.Value == null ? null : field.Value.ToString().Trim();
                if (string.IsNullOrEmpty(label))
                {
                    emptyLabels.Add(field.Name);
                    continue;
                }

                string value;
                if (!lookup.TryGetValue(label, out value))
                {
                    missing.Add(field.Name + "（" + label + "）");
                    continue;
                }

                result[field.Name] = NormalizeValue(value, normalize);
            }

            string unknownKeys = FindUnknownExtendKeys(extend, template);
            if (unknownKeys != null)
            {
                throw new ApiException(
                    ApiErrorCode.TemplateInvalid,
                    "extend 中存在 template 没有的 key：" + unknownKeys + "。");
            }

            if (emptyLabels.Count > 0)
            {
                throw new ApiException(
                    ApiErrorCode.TemplateInvalid,
                    "template 中存在没有中文字段名的 key：" + string.Join("、", emptyLabels.ToArray()) + "。");
            }

            return result;
        }

        /// <summary>
        /// 读取 extend 里某个字段的直接值；没有该 key 时返回 false。
        /// 值支持字符串、数字、布尔、null（null 视为空串）。
        /// </summary>
        private static bool TryGetExtendValue(JObject extend, string key, out string value)
        {
            value = null;
            if (extend == null)
            {
                return false;
            }

            JProperty property = extend.Property(key);
            if (property == null)
            {
                return false;
            }

            JToken token = property.Value;
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                value = string.Empty;
                return true;
            }

            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
            {
                throw new ApiException(
                    ApiErrorCode.InvalidArgument, "extend." + key + " 的值必须是字符串、数字、布尔或 null。");
            }

            // 字符串原样取，数字/布尔按 JSON 字面量转（true 而不是 True）
            value = token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString(Formatting.None);
            return true;
        }

        /// <summary>找出 extend 里 template 不存在的 key，没有则返回 null。</summary>
        private static string FindUnknownExtendKeys(JObject extend, JObject template)
        {
            if (extend == null || extend.Count == 0)
            {
                return null;
            }

            var unknown = new List<string>();
            foreach (JProperty property in extend.Properties())
            {
                if (template.Property(property.Name) == null)
                {
                    unknown.Add(property.Name);
                }
            }

            return unknown.Count == 0 ? null : string.Join("、", unknown.ToArray());
        }

        /// <summary>
        /// 拼装“模板字段缺失”的错误信息，最多列出前 10 个，其余只报数量。
        /// </summary>
        public static string BuildMissingFieldsMessage(List<string> missing)
        {
            const int maxShown = 10;
            var builder = new StringBuilder();

            builder.Append("PDF 与模板比对失败：模板要求的 ");
            builder.Append(missing.Count);
            builder.Append(" 个字段在 PDF 中未找到 —— ");

            int shown = Math.Min(missing.Count, maxShown);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    builder.Append('、');
                }

                builder.Append(missing[i]);
            }

            if (missing.Count > maxShown)
            {
                builder.Append(" 等共 ").Append(missing.Count).Append(" 个");
            }

            builder.Append("。请检查 template 与 PDF 内容是否一致。");
            return builder.ToString();
        }

        /// <summary>
        /// 建立 “标签 -> 值” 查询表，并为父标签下的子标签生成 "父-子" 键。
        /// </summary>
        private static Dictionary<string, string> BuildLookup(IList<PdfKeyValuePair> pairs)
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (PdfKeyValuePair pair in pairs)
            {
                List<string> values;
                if (!groups.TryGetValue(pair.Key, out values))
                {
                    values = new List<string>();
                    groups[pair.Key] = values;
                    order.Add(pair.Key);
                }

                values.Add((pair.Value ?? string.Empty).Trim());
            }

            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string key in order)
            {
                List<string> values = groups[key];
                if (values.Count == 1)
                {
                    lookup[key] = values[0];
                    continue;
                }

                lookup[key] = values[0];

                foreach (string child in values)
                {
                    List<string> childValues;
                    if (string.IsNullOrEmpty(child) ||
                        !groups.TryGetValue(child, out childValues) ||
                        childValues.Count != 1)
                    {
                        continue;
                    }

                    string combined = key + "-" + child;
                    if (!lookup.ContainsKey(combined))
                    {
                        lookup[combined] = childValues[0];
                    }
                }
            }

            return lookup;
        }

        private static string NormalizeValue(string value, bool enabled)
        {
            string text = (value ?? string.Empty).Trim();
            return enabled ? TextNormalizer.Normalize(text) : text;
        }
    }
}
