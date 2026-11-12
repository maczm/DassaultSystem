using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using MOM.Common;
using MOM.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MOM.Controllers
{
    /// <summary>
    /// 统一响应（成功/失败同一个信封，最外层只有 code、msg、data）：
    /// { "code": "OK", "msg": "success", "data": { "hash": "…", "result": { … } } }
    /// 失败时 code 为业务错误码、data.hash 与 data.result 均为 null。
    /// data.hash 为 data.result 规范化 JSON 的 SHA-256。
    /// </summary>
    public abstract class ApiControllerBase : ApiController
    {
        /// <summary>成功响应。</summary>
        protected HttpResponseMessage Success(JToken result)
        {
            return Respond(HttpStatusCode.OK, ApiErrorCode.Ok, "success", result);
        }

        /// <summary>失败响应：业务异常用其错误码，其他异常统一按 INTERNAL_ERROR 处理。</summary>
        protected HttpResponseMessage Failure(Exception exception)
        {
            var apiException = exception as ApiException;
            string code = apiException != null ? apiException.Code : ApiErrorCode.InternalError;
            string message = apiException != null
                ? apiException.Message
                : "服务内部错误：" + exception.Message;

            Trace.TraceError(string.Format(
                CultureInfo.InvariantCulture, "接口调用失败（{0}）：{1}{2}{3}",
                code, message, Environment.NewLine, exception));

            return Respond(ApiErrorCode.ToHttpStatus(code), code, message, null);
        }

        private HttpResponseMessage Respond(
            HttpStatusCode statusCode, string code, string message, JToken result)
        {
            var data = new JObject
            {
                { "hash", result == null ? JValue.CreateNull() : new JValue(JsonHash.Sha256(result)) },
                { "result", result == null ? (JToken)JValue.CreateNull() : result.DeepClone() }
            };

            var envelope = new JObject
            {
                { "code", code },
                { "msg", message },
                { "data", data }
            };

            return Json(statusCode, envelope);
        }

        private HttpResponseMessage Json(HttpStatusCode statusCode, JToken value)
        {
            if (Request != null && Configuration != null)
            {
                return Request.CreateResponse(statusCode, value, Configuration.Formatters.JsonFormatter);
            }

            var response = new HttpResponseMessage(statusCode);
            response.Content = new StringContent(
                value.ToString(Formatting.None), Encoding.UTF8, "application/json");
            return response;
        }
    }
}
