using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MOM.Models
{
    /// <summary>
    /// PDF 解析请求：
    /// {
    ///   "url": "PDF 地址（http/https 或服务器本地/共享路径）",
    ///   "password": "加密 PDF 的密码，可选",
    ///   "template": { "英文 key": "中文字段名", ... },
    ///   "extend": { "英文 key": "直接给的值", ... },
    ///   "normalize": true
    /// }
    ///
    /// template 决定返回 JSON 的字段名与顺序；PDF 中找不到模板字段即返回 TEMPLATE_FIELDS_MISSING。
    /// extend 按 template 的 key 提供值，用于 PDF 里没有的字段（也用于覆盖/补空），
    /// 这些值同样参与 data.result 与 data.hash。
    /// normalize 不传时使用 Web.config 里的 PdfParse.NormalizePunctuation。
    /// </summary>
    public class PdfParseRequest
    {
        /// <summary>PDF 地址：http/https，或服务器上的本地/共享路径。</summary>
        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>加密 PDF 的打开密码。</summary>
        [JsonProperty("password")]
        public string Password { get; set; }

        /// <summary>英文 key -> 中文字段名，顺序即返回 JSON 的字段顺序。</summary>
        [JsonProperty("template")]
        public JObject Template { get; set; }

        /// <summary>
        /// 英文 key -> 直接给的值。key 必须是 template 里已有的字段；
        /// 给了值就不再查 PDF，用于 PDF 里没有的字段（也用于覆盖或补空）。
        /// </summary>
        [JsonProperty("extend")]
        public JObject Extend { get; set; }

        /// <summary>是否对值做符号归一化，不传则用配置默认值。</summary>
        [JsonProperty("normalize")]
        public bool? Normalize { get; set; }
    }
}
