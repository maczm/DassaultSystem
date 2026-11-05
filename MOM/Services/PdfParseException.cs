using System;

namespace MOM.Services
{
    /// <summary>
    /// PDF 解析相关的错误码，控制器据此映射 HTTP 状态码。
    /// </summary>
    public static class PdfErrorCodes
    {
        public const string InvalidArgument = "INVALID_ARGUMENT";
        public const string InvalidPdf = "INVALID_PDF";
        public const string PasswordRequired = "PASSWORD_REQUIRED";
        public const string PasswordInvalid = "PASSWORD_INVALID";
        public const string Unsupported = "UNSUPPORTED_PDF";
        public const string TooLarge = "TOO_LARGE";
        public const string DownloadFailed = "DOWNLOAD_FAILED";
        public const string Timeout = "TIMEOUT";
        public const string UnsupportedMedia = "UNSUPPORTED_MEDIA_TYPE";
        public const string FileNotFound = "FILE_NOT_FOUND";
        public const string FileAccessDenied = "FILE_ACCESS_DENIED";
    }

    /// <summary>
    /// 可预期的 PDF 解析异常，带错误码，便于控制器转换为对应状态码。
    /// </summary>
    public class PdfParseException : Exception
    {
        public PdfParseException(string errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public PdfParseException(string errorCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; private set; }
    }
}
