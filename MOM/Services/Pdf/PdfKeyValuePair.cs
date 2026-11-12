using Newtonsoft.Json;

namespace MOM.Services.Pdf
{
    /// <summary>
    /// 解析结果中的一组“标签 -> 值”。
    /// </summary>
    public class PdfKeyValuePair
    {
        /// <summary>标签/字段名。</summary>
        [JsonProperty("key")]
        public string Key { get; set; }

        /// <summary>字段值；没有值时为空字符串。</summary>
        [JsonProperty("value")]
        public string Value { get; set; }
    }
}
