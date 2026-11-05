using iTextSharp.text.exceptions;
using iTextSharp.text.pdf;
using MOM.Models;
using MOM.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace MOM.Controllers
{
    /// <summary>
    /// 把 PDF 解析成“标签 -> 值”的 JSON 数组。
    ///
    /// 两种调用方式：
    ///   GET  api/pdfparse?url=...            url 可以是 http/https 地址，也可以是服务器本地/共享路径
    ///   POST api/pdfparse/upload             以 multipart/form-data 上传，表单字段名 file
    ///
    /// 可选参数 password：加密 PDF 的打开密码。
    /// 返回：[{ "key": "...", "value": "..." }, ...]
    /// </summary>
    [RoutePrefix("api/pdfparse")]
    public class PdfParseController : ApiController
    {
        private const string SettingMaxBytes = "PdfParse.MaxFileSizeBytes";
        private const string SettingTimeoutSeconds = "PdfParse.DownloadTimeoutSeconds";
        private const string SettingAllowPrivateNetwork = "PdfParse.AllowPrivateNetwork";

        private const long DefaultMaxBytes = 50L * 1024 * 1024;
        private const int DefaultTimeoutSeconds = 30;

        /// <summary>
        /// 按地址解析 PDF（远程 URL 或服务器本地/共享路径）。
        /// </summary>
        [HttpGet, Route("")]
        public async Task<HttpResponseMessage> Get(string url = null, string pdfUrl = null, string password = null)
        {
            try
            {
                string location = string.IsNullOrWhiteSpace(url) ? pdfUrl : url;
                if (string.IsNullOrWhiteSpace(location))
                {
                    throw new PdfParseException(PdfErrorCodes.InvalidArgument, "请通过 url（或 pdfUrl）参数提供 PDF 地址。");
                }

                byte[] content = await LoadAsync(location).ConfigureAwait(false);
                return Json(HttpStatusCode.OK, Parse(content, password));
            }
            catch (Exception ex)
            {
                return BuildError(ex);
            }
        }

        /// <summary>
        /// 上传 PDF 并解析。表单字段：file（文件），password（可选）。
        /// </summary>
        [HttpPost, Route("upload")]
        public async Task<HttpResponseMessage> Upload(string password = null)
        {
            try
            {
                if (!Request.Content.IsMimeMultipartContent("form-data"))
                {
                    throw new PdfParseException(
                        PdfErrorCodes.UnsupportedMedia, "请使用 multipart/form-data 上传文件，表单字段名为 file。");
                }

                long? declaredLength = Request.Content.Headers.ContentLength;
                if (declaredLength.HasValue && declaredLength.Value > GetMaxBytes() + 1024L * 1024L)
                {
                    throw TooLarge();
                }

                var provider = await Request.Content.ReadAsMultipartAsync(new MultipartMemoryStreamProvider()).ConfigureAwait(false);
                byte[] content = null;
                string formPassword = null;

                foreach (HttpContent part in provider.Contents)
                {
                    string name = GetDispositionValue(part, true);
                    string fileName = GetDispositionValue(part, false);
                    bool looksLikePdf = part.Headers.ContentType != null &&
                        string.Equals(part.Headers.ContentType.MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);

                    if (content == null && (!string.IsNullOrEmpty(fileName) || looksLikePdf))
                    {
                        content = await part.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                    else if (string.Equals(name, "password", StringComparison.OrdinalIgnoreCase))
                    {
                        formPassword = (await part.ReadAsStringAsync().ConfigureAwait(false)).Trim();
                    }
                }

                if (content == null || content.Length == 0)
                {
                    throw new PdfParseException(
                        PdfErrorCodes.InvalidArgument, "未找到上传的 PDF 文件，请使用表单字段 file 上传。");
                }

                if (content.Length > GetMaxBytes())
                {
                    throw TooLarge();
                }

                return Json(HttpStatusCode.OK, Parse(content, FirstNotEmpty(formPassword, password)));
            }
            catch (Exception ex)
            {
                return BuildError(ex);
            }
        }

        private async Task<byte[]> LoadAsync(string location)
        {
            Uri uri;
            if (Uri.TryCreate(location, UriKind.Absolute, out uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return await PdfSourceLoader.DownloadAsync(
                    location, GetMaxBytes(), GetDownloadTimeout(), GetAllowPrivateNetwork()).ConfigureAwait(false);
            }

            return PdfSourceLoader.ReadLocalFile(location, GetMaxBytes());
        }

        private static IList<PdfKeyValuePair> Parse(byte[] content, string password)
        {
            if (content == null || content.Length == 0)
            {
                throw new PdfParseException(PdfErrorCodes.InvalidArgument, "PDF 内容为空。");
            }

            EnsurePdfHeader(content);

            byte[] passwordBytes = string.IsNullOrEmpty(password) ? null : Encoding.UTF8.GetBytes(password);

            PdfReader reader;
            try
            {
                reader = new PdfReader(content, passwordBytes);
            }
            catch (BadPasswordException)
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new PdfParseException(PdfErrorCodes.PasswordRequired, "该 PDF 已加密，请通过 password 参数提供打开密码。");
                }

                throw new PdfParseException(PdfErrorCodes.PasswordInvalid, "PDF 密码不正确，无法打开文件。");
            }
            catch (UnsupportedPdfException ex)
            {
                throw new PdfParseException(PdfErrorCodes.Unsupported, "该 PDF 使用了暂不支持的特性：" + ex.Message, ex);
            }
            catch (InvalidPdfException ex)
            {
                throw new PdfParseException(PdfErrorCodes.InvalidPdf, "无法解析该 PDF 文件：" + ex.Message, ex);
            }

            try
            {
                var pages = new List<int>(reader.NumberOfPages);
                for (int page = 1; page <= reader.NumberOfPages; page++)
                {
                    pages.Add(page);
                }

                return PdfKeyValueExtractor.Extract(reader, pages);
            }
            finally
            {
                reader.Close();
            }
        }

        private static void EnsurePdfHeader(byte[] content)
        {
            int length = Math.Min(content.Length, 1024);
            string header = Encoding.ASCII.GetString(content, 0, length);
            if (header.IndexOf("%PDF-", StringComparison.Ordinal) < 0)
            {
                throw new PdfParseException(
                    PdfErrorCodes.InvalidPdf, "内容不是有效的 PDF 文件（未找到 %PDF- 文件头）。");
            }
        }

        private HttpResponseMessage BuildError(Exception exception)
        {
            var parseException = exception as PdfParseException;
            string code = parseException != null ? parseException.ErrorCode : "INTERNAL_ERROR";
            string message = parseException != null
                ? parseException.Message
                : "解析 PDF 时发生未预期的错误：" + exception.Message;

            Trace.TraceError(string.Format(
                CultureInfo.InvariantCulture, "PDF 解析失败（{0}）：{1}{2}{3}",
                code, message, Environment.NewLine, exception));

            return Json(MapStatusCode(code), new PdfParseError { Code = code, Message = message });
        }

        private HttpResponseMessage Json(HttpStatusCode statusCode, object value)
        {
            if (Request != null && Configuration != null)
            {
                return Request.CreateResponse(statusCode, value, Configuration.Formatters.JsonFormatter);
            }

            var response = new HttpResponseMessage(statusCode);
            response.Content = new StringContent(
                JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
            return response;
        }

        private static HttpStatusCode MapStatusCode(string errorCode)
        {
            switch (errorCode)
            {
                case PdfErrorCodes.InvalidArgument:
                    return HttpStatusCode.BadRequest;
                case PdfErrorCodes.FileNotFound:
                    return HttpStatusCode.NotFound;
                case PdfErrorCodes.FileAccessDenied:
                    return HttpStatusCode.Forbidden;
                case PdfErrorCodes.UnsupportedMedia:
                    return HttpStatusCode.UnsupportedMediaType;
                case PdfErrorCodes.TooLarge:
                    return HttpStatusCode.RequestEntityTooLarge;
                case PdfErrorCodes.PasswordRequired:
                case PdfErrorCodes.PasswordInvalid:
                case PdfErrorCodes.InvalidPdf:
                case PdfErrorCodes.Unsupported:
                    return (HttpStatusCode)422;
                case PdfErrorCodes.Timeout:
                    return HttpStatusCode.GatewayTimeout;
                case PdfErrorCodes.DownloadFailed:
                    return HttpStatusCode.BadGateway;
                default:
                    return HttpStatusCode.InternalServerError;
            }
        }

        private static string GetDispositionValue(HttpContent part, bool name)
        {
            var disposition = part.Headers.ContentDisposition;
            if (disposition == null)
            {
                return null;
            }

            string value = name ? disposition.Name : disposition.FileName;
            return value == null ? null : value.Trim('"', ' ');
        }

        private static string FirstNotEmpty(string first, string second)
        {
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }

            return string.IsNullOrWhiteSpace(second) ? null : second;
        }

        private PdfParseException TooLarge()
        {
            return new PdfParseException(
                PdfErrorCodes.TooLarge,
                string.Format(
                    CultureInfo.InvariantCulture, "PDF 文件超过大小限制（{0} MB）。", Math.Max(1, GetMaxBytes() / 1024 / 1024)));
        }

        private static long GetMaxBytes()
        {
            long value;
            string configured = System.Configuration.ConfigurationManager.AppSettings[SettingMaxBytes];
            if (!string.IsNullOrWhiteSpace(configured) && long.TryParse(configured, out value) && value > 0)
            {
                return value;
            }

            return DefaultMaxBytes;
        }

        private static TimeSpan GetDownloadTimeout()
        {
            int seconds;
            string configured = System.Configuration.ConfigurationManager.AppSettings[SettingTimeoutSeconds];
            if (!string.IsNullOrWhiteSpace(configured) && int.TryParse(configured, out seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            return TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        }

        private static bool GetAllowPrivateNetwork()
        {
            string configured = System.Configuration.ConfigurationManager.AppSettings[SettingAllowPrivateNetwork];
            bool value;
            return !string.IsNullOrWhiteSpace(configured) && bool.TryParse(configured, out value) && value;
        }
    }
}
