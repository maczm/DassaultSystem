using System;

namespace MOM.Common
{
    /// <summary>
    /// 可预期的业务异常：带稳定的业务码，控制器据此生成统一错误响应。
    /// </summary>
    public class ApiException : Exception
    {
        public ApiException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public ApiException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }

        /// <summary>业务错误码，取值见 <see cref="ApiErrorCode"/>。</summary>
        public string Code { get; private set; }
    }
}
