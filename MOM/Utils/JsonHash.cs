using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MOM.Utils
{
    /// <summary>
    /// JSON 规范化与哈希（纯函数工具）。
    ///
    /// 规范化：对象 key 按码点（Ordinal）升序排列、紧凑格式（无空格）、UTF-8、非 ASCII 不转义；
    /// 哈希：SHA-256(规范化 JSON 的 UTF-8 字节)，小写十六进制。
    /// 因为先排序再序列化，字段顺序、缩进、空格都不影响 hash，跨语言可复现。
    /// </summary>
    public static class JsonHash
    {
        /// <summary>对 JSON 的规范化形式做 SHA-256，返回小写十六进制。</summary>
        public static string Sha256(JToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Canonicalize(token));

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);

                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        /// <summary>返回规范化后的紧凑 JSON 文本。</summary>
        public static string Canonicalize(JToken token)
        {
            return Sort(token).ToString(Formatting.None);
        }

        private static JToken Sort(JToken token)
        {
            if (token == null)
            {
                return JValue.CreateNull();
            }

            var obj = token as JObject;
            if (obj != null)
            {
                var sorted = new JObject();
                foreach (JProperty property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    sorted[property.Name] = Sort(property.Value);
                }

                return sorted;
            }

            var array = token as JArray;
            if (array != null)
            {
                var items = new JArray();
                foreach (JToken item in array)
                {
                    items.Add(Sort(item));
                }

                return items;
            }

            return token.DeepClone();
        }
    }
}
