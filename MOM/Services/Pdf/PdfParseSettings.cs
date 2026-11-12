using System;
using System.Configuration;

namespace MOM.Services.Pdf
{
    /// <summary>
    /// PDF 解析相关的配置读取（Web.config 的 appSettings）。
    /// 读取时带默认值，配置缺失或格式不对也不会让接口挂掉。
    /// </summary>
    public static class PdfParseSettings
    {
        private const string MaxBytesKey = "PdfParse.MaxFileSizeBytes";
        private const string TimeoutSecondsKey = "PdfParse.DownloadTimeoutSeconds";
        private const string AllowPrivateNetworkKey = "PdfParse.AllowPrivateNetwork";
        private const string NormalizePunctuationKey = "PdfParse.NormalizePunctuation";

        private const long DefaultMaxBytes = 50L * 1024 * 1024;
        private const int DefaultTimeoutSeconds = 30;

        /// <summary>单个 PDF 允许的最大字节数。</summary>
        public static long MaxBytes()
        {
            long value;
            string configured = Read(MaxBytesKey);
            if (!string.IsNullOrWhiteSpace(configured) && long.TryParse(configured, out value) && value > 0)
            {
                return value;
            }

            return DefaultMaxBytes;
        }

        /// <summary>下载远程 PDF 的超时时间。</summary>
        public static TimeSpan DownloadTimeout()
        {
            int seconds;
            string configured = Read(TimeoutSecondsKey);
            if (!string.IsNullOrWhiteSpace(configured) && int.TryParse(configured, out seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            return TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        }

        /// <summary>是否允许下载内网/回环地址（SSRF 防护开关）。</summary>
        public static bool AllowPrivateNetwork()
        {
            bool value;
            string configured = Read(AllowPrivateNetworkKey);
            return !string.IsNullOrWhiteSpace(configured) && bool.TryParse(configured, out value) && value;
        }

        /// <summary>是否对解析结果的值做符号归一化（默认开启）。</summary>
        public static bool NormalizePunctuation()
        {
            bool value;
            string configured = Read(NormalizePunctuationKey);
            return string.IsNullOrWhiteSpace(configured) || !bool.TryParse(configured.Trim(), out value) || value;
        }

        private static string Read(string key)
        {
            return ConfigurationManager.AppSettings[key];
        }
    }
}
