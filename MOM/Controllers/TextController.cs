using System;
using System.Net.Http;
using System.Web.Http;
using MOM.Common;
using MOM.Utils;
using Newtonsoft.Json.Linq;

namespace MOM.Controllers
{
    /// <summary>
    /// 文本/JSON 工具接口：符号归一化。
    /// </summary>
    [RoutePrefix("api/text")]
    public class TextController : ApiControllerBase
    {
        /// <summary>
        /// 符号归一化：把 JSON 里所有字符串值的中文标点、全角字符转成英文半角，
        /// 连续空格合并、去掉首尾空格；key 与字段顺序保持不变。
        ///
        /// 请求体直接传数据 JSON，如 {"vin":"（Ａ１２３）","vehicleModel":"HQC41800SWBEV2"}；
        /// 也支持嵌套对象、数组或单个字符串。
        /// 返回 data.result 为转换后的同结构 JSON，data.hash 为它的 SHA-256。
        /// </summary>
        [HttpPost, Route("normalize")]
        public HttpResponseMessage Normalize([FromBody] JToken body)
        {
            try
            {
                if (body == null || body.Type == JTokenType.Null || body.Type == JTokenType.Undefined)
                {
                    throw new ApiException(
                        ApiErrorCode.InvalidJson,
                        "请求体为空或不是有效的 JSON，请直接传入数据 JSON，如 {\"vin\":\"…\"}。");
                }

                return Success(JsonValueNormalizer.Normalize(body));
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
        }
    }
}
