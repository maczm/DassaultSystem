using System.Net;

namespace MOM.Common
{
    /// <summary>
    /// 业务状态码：响应体里的 code 用这里的稳定字符串，HTTP 状态码只表达错误类别。
    /// 成功为 OK，失败为对应错误码。
    /// </summary>
    public static class ApiErrorCode
    {
        /// <summary>成功。</summary>
        public const string Ok = "OK";

        /// <summary>参数缺失或格式不对。</summary>
        public const string InvalidArgument = "INVALID_ARGUMENT";

        /// <summary>请求体不是有效的 JSON。</summary>
        public const string InvalidJson = "INVALID_JSON";

        /// <summary>template 不合法（为空、含空字段名等）。</summary>
        public const string TemplateInvalid = "TEMPLATE_INVALID";

        /// <summary>PDF 中找不到 template 要求的字段。</summary>
        public const string TemplateFieldsMissing = "TEMPLATE_FIELDS_MISSING";

        /// <summary>文件不存在。</summary>
        public const string FileNotFound = "FILE_NOT_FOUND";

        /// <summary>没有读取文件的权限。</summary>
        public const string FileAccessDenied = "FILE_ACCESS_DENIED";

        /// <summary>文件超过大小限制。</summary>
        public const string FileTooLarge = "FILE_TOO_LARGE";

        /// <summary>加密 PDF 需要密码。</summary>
        public const string PasswordRequired = "PASSWORD_REQUIRED";

        /// <summary>PDF 密码错误。</summary>
        public const string PasswordInvalid = "PASSWORD_INVALID";

        /// <summary>不是有效的 PDF。</summary>
        public const string InvalidPdf = "INVALID_PDF";

        /// <summary>PDF 使用了暂不支持的特性。</summary>
        public const string UnsupportedPdf = "UNSUPPORTED_PDF";

        /// <summary>下载 PDF 超时。</summary>
        public const string UpstreamTimeout = "UPSTREAM_TIMEOUT";

        /// <summary>下载 PDF 失败（网络、重定向、状态码等）。</summary>
        public const string UpstreamError = "UPSTREAM_ERROR";

        /// <summary>未预期的服务端错误。</summary>
        public const string InternalError = "INTERNAL_ERROR";

        /// <summary>把业务错误码映射为 HTTP 状态码（成功返回 200）。</summary>
        public static HttpStatusCode ToHttpStatus(string code)
        {
            switch (code)
            {
                case Ok:
                    return HttpStatusCode.OK;
                case InvalidArgument:
                case InvalidJson:
                case TemplateInvalid:
                    return HttpStatusCode.BadRequest;
                case FileAccessDenied:
                    return HttpStatusCode.Forbidden;
                case FileNotFound:
                    return HttpStatusCode.NotFound;
                case FileTooLarge:
                    return HttpStatusCode.RequestEntityTooLarge;
                case PasswordRequired:
                case PasswordInvalid:
                case InvalidPdf:
                case UnsupportedPdf:
                case TemplateFieldsMissing:
                    return (HttpStatusCode)422;
                case UpstreamTimeout:
                    return HttpStatusCode.GatewayTimeout;
                case UpstreamError:
                    return HttpStatusCode.BadGateway;
                default:
                    return HttpStatusCode.InternalServerError;
            }
        }
    }
}
