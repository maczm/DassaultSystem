using Newtonsoft.Json.Linq;

namespace MOM.Utils
{
    /// <summary>
    /// JSON 值转换工具：递归把字符串值里中文标点/全角字符转成英文半角，
    /// key、字段顺序、数字/布尔/null 都不动。
    /// </summary>
    public static class JsonValueNormalizer
    {
        public static JToken Normalize(JToken token)
        {
            if (token == null)
            {
                return JValue.CreateNull();
            }

            var obj = token as JObject;
            if (obj != null)
            {
                var result = new JObject();
                foreach (JProperty property in obj.Properties())
                {
                    result[property.Name] = Normalize(property.Value);
                }

                return result;
            }

            var array = token as JArray;
            if (array != null)
            {
                var result = new JArray();
                foreach (JToken item in array)
                {
                    result.Add(Normalize(item));
                }

                return result;
            }

            var value = token as JValue;
            if (value != null && value.Type == JTokenType.String)
            {
                return new JValue(TextNormalizer.Normalize((string)value.Value));
            }

            return token.DeepClone();
        }
    }
}
