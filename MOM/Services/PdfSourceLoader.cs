using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MOM.Services
{
    /// <summary>
    /// PDF 内容来源：http/https 下载，或服务器本地/共享路径读取。
    /// </summary>
    public static class PdfSourceLoader
    {
        private const int MaxRedirects = 5;
        private const int ReadBufferSize = 81920;

        private static readonly HttpClient Client = CreateClient();

        static PdfSourceLoader()
        {
            // .NET 4.5 默认不启用 TLS 1.2，而多数 PDF 站点只支持 TLS 1.2+，
            // 这里显式打开（已在更高版本框架上默认启用，不产生影响）。
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
                // 系统不支持 TLS 1.2 时保持默认设置。
            }
        }

        /// <summary>
        /// 读取服务器本地或共享路径上的 PDF。
        /// </summary>
        public static byte[] ReadLocalFile(string path, long maxBytes)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                throw new PdfParseException(PdfErrorCodes.InvalidArgument, "文件路径格式不正确：" + path, ex);
            }

            if (!File.Exists(fullPath))
            {
                // File.Exists 对“没有权限”也会返回 false，这里再打开一次，区分两种原因
                try
                {
                    using (File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new PdfParseException(
                        PdfErrorCodes.FileAccessDenied,
                        "没有权限读取该文件，请给 IIS 应用程序池账号授予目录读取权限：" + fullPath,
                        ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw new PdfParseException(
                        PdfErrorCodes.FileNotFound,
                        "文件所在目录不存在：" + fullPath + "（url 里的本地路径必须是 API 服务器上的文件；调用方电脑上的文件请改用上传接口）",
                        ex);
                }
                catch (IOException)
                {
                    // 其他 IO 原因，按不存在处理
                }

                throw new PdfParseException(
                    PdfErrorCodes.FileNotFound,
                    "文件不存在：" + fullPath + "（url 里的本地路径必须是 API 服务器上的文件；调用方电脑上的文件请改用上传接口）");
            }

            var file = new FileInfo(fullPath);
            if (file.Length > maxBytes)
            {
                throw TooLarge(maxBytes);
            }

            byte[] content;
            try
            {
                content = File.ReadAllBytes(fullPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new PdfParseException(
                    PdfErrorCodes.FileAccessDenied,
                    "没有权限读取该文件，请给 IIS 应用程序池账号授予目录读取权限：" + fullPath,
                    ex);
            }
            catch (IOException ex)
            {
                throw new PdfParseException(
                    PdfErrorCodes.FileAccessDenied, "读取文件失败（文件可能被占用或权限不足）：" + fullPath, ex);
            }
            if (content.Length == 0)
            {
                throw new PdfParseException(PdfErrorCodes.InvalidPdf, "文件内容为空：" + fullPath);
            }

            return content;
        }

        public static async Task<byte[]> DownloadAsync(string url, long maxBytes, TimeSpan timeout, bool allowPrivateNetwork)
        {
            Uri current;
            if (!Uri.TryCreate(url, UriKind.Absolute, out current))
            {
                throw new PdfParseException(PdfErrorCodes.InvalidArgument, "PDF 地址格式不正确。");
            }

            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                EnsureAllowedAddress(current, allowPrivateNetwork);

                using (var cancellation = new CancellationTokenSource(timeout))
                using (var request = new HttpRequestMessage(HttpMethod.Get, current))
                {
                    request.Headers.UserAgent.ParseAdd("MOM.PdfParse/1.0");

                    HttpResponseMessage response;
                    try
                    {
                        response = await Client.SendAsync(
                            request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex)
                    {
                        throw new PdfParseException(
                            PdfErrorCodes.Timeout,
                            string.Format(CultureInfo.InvariantCulture, "下载 PDF 超时（{0:0} 秒）。", timeout.TotalSeconds),
                            ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new PdfParseException(PdfErrorCodes.DownloadFailed, "下载 PDF 失败：" + ex.Message, ex);
                    }

                    using (response)
                    {
                        if (IsRedirect(response.StatusCode))
                        {
                            Uri location = ResolveRedirectLocation(response, current);
                            if (location == null)
                            {
                                throw new PdfParseException(PdfErrorCodes.DownloadFailed, "远程服务器返回了无效的重定向地址。");
                            }

                            current = location;
                            continue;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            throw new PdfParseException(
                                PdfErrorCodes.DownloadFailed,
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "下载 PDF 失败，远程服务器返回 {0} {1}。",
                                    (int)response.StatusCode,
                                    response.ReasonPhrase));
                        }

                        long? contentLength = response.Content.Headers.ContentLength;
                        if (contentLength.HasValue && contentLength.Value > maxBytes)
                        {
                            throw TooLarge(maxBytes);
                        }

                        using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var buffer = new MemoryStream(contentLength.HasValue ? (int)Math.Min(contentLength.Value, maxBytes) : 0))
                        {
                            byte[] chunk = new byte[ReadBufferSize];
                            long total = 0;
                            int read;
                            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellation.Token).ConfigureAwait(false)) > 0)
                            {
                                total += read;
                                if (total > maxBytes)
                                {
                                    throw TooLarge(maxBytes);
                                }

                                buffer.Write(chunk, 0, read);
                            }

                            if (buffer.Length == 0)
                            {
                                throw new PdfParseException(PdfErrorCodes.InvalidPdf, "远程地址返回了空内容。");
                            }

                            return buffer.ToArray();
                        }
                    }
                }
            }

            throw new PdfParseException(PdfErrorCodes.DownloadFailed, "PDF 地址重定向次数过多。");
        }

        /// <summary>
        /// 校验地址协议与目标主机，避免服务端请求伪造（SSRF）。
        /// </summary>
        private static void EnsureAllowedAddress(Uri uri, bool allowPrivateNetwork)
        {
            bool isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
            bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            if (!isHttp && !isHttps)
            {
                throw new PdfParseException(PdfErrorCodes.InvalidArgument, "只支持 http 或 https 协议的 PDF 地址。");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new PdfParseException(PdfErrorCodes.InvalidArgument, "PDF 地址中不能包含用户名或密码。");
            }

            if (allowPrivateNetwork)
            {
                return;
            }

            IPAddress[] addresses;
            IPAddress literal;
            if (IPAddress.TryParse(uri.DnsSafeHost, out literal))
            {
                addresses = new[] { literal };
            }
            else
            {
                try
                {
                    addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
                }
                catch (SocketException ex)
                {
                    throw new PdfParseException(
                        PdfErrorCodes.DownloadFailed, "无法解析 PDF 地址的主机名：" + uri.Host, ex);
                }
            }

            if (addresses.Length == 0)
            {
                throw new PdfParseException(PdfErrorCodes.DownloadFailed, "PDF 地址的主机名没有解析到任何 IP 地址。");
            }

            foreach (IPAddress address in addresses)
            {
                if (IsPrivateAddress(address))
                {
                    throw new PdfParseException(
                        PdfErrorCodes.InvalidArgument,
                        "出于安全考虑，不允许下载内网或回环地址上的 PDF：" + uri.Host);
                }
            }
        }

        private static bool IsPrivateAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                {
                    return true;
                }

                if (address.IsIPv4MappedToIPv6)
                {
                    return IsPrivateAddress(address.MapToIPv4());
                }

                byte[] v6 = address.GetAddressBytes();
                // fc00::/7，唯一本地地址
                return (v6[0] & 0xFE) == 0xFC;
            }

            byte[] bytes = address.GetAddressBytes();
            if (bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0)
            {
                return true;
            }

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 169.254.0.0/16，链路本地（含云环境的元数据地址）
            return bytes[0] == 169 && bytes[1] == 254;
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
        }

        private static Uri ResolveRedirectLocation(HttpResponseMessage response, Uri requestUri)
        {
            Uri location = response.Headers.Location;
            if (location == null)
            {
                return null;
            }

            return location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        }

        private static PdfParseException TooLarge(long maxBytes)
        {
            return new PdfParseException(
                PdfErrorCodes.TooLarge,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "PDF 文件超过大小限制（{0} MB）。",
                    Math.Max(1, maxBytes / 1024 / 1024)));
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler, true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/pdf,*/*;q=0.8");
            return client;
        }
    }
}
