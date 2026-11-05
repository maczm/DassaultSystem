using Newtonsoft.Json;

namespace MOM.Models
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

    /// <summary>
    /// 解析失败时的返回结构。
    /// </summary>
    public class PdfParseError
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
