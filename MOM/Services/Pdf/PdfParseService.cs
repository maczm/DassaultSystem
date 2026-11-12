using MOM.Common;
using MOM.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MOM.Services.Pdf
{
    /// <summary>
    /// PDF 解析编排：校验请求 → 加载来源 → 解析标签值 → 按模板映射。
    /// 接口层只负责 HTTP，解析细节都在这里，便于被其他入口（上传、批处理）复用。
    /// </summary>
    public static class PdfParseService
    {
        /// <summary>
        /// 按请求解析 PDF 并返回模板形状的 JSON。
        /// 模板字段缺失、文件不可读、PDF 损坏等情况以 <see cref="ApiException"/> 抛出。
        /// </summary>
        public static async Task<JObject> ParseAsync(PdfParseRequest request)
        {
            if (request == null)
            {
                throw new ApiException(ApiErrorCode.InvalidJson, "请求体不是有效的 JSON。");
            }

            if (string.IsNullOrWhiteSpace(request.Url))
            {
                throw new ApiException(ApiErrorCode.InvalidArgument, "请提供 url（PDF 地址）。");
            }

            if (request.Template == null || request.Template.Count == 0)
            {
                throw new ApiException(
                    ApiErrorCode.TemplateInvalid, "请提供 template（英文 key -> 中文字段名的映射）。");
            }

            byte[] content = await LoadAsync(request.Url).ConfigureAwait(false);
            IList<PdfKeyValuePair> pairs = PdfDocumentParser.Parse(content, request.Password);

            bool normalize = request.Normalize.HasValue
                ? request.Normalize.Value
                : PdfParseSettings.NormalizePunctuation();

            JObject result = PdfTemplateMapper.Map(
                pairs, request.Template, request.Extend, normalize, out List<string> missing);

            if (missing.Count > 0)
            {
                throw new ApiException(
                    ApiErrorCode.TemplateFieldsMissing,
                    PdfTemplateMapper.BuildMissingFieldsMessage(missing));
            }

            return result;
        }

        private static async Task<byte[]> LoadAsync(string location)
        {
            Uri uri;
            if (Uri.TryCreate(location, UriKind.Absolute, out uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return await PdfSourceLoader.DownloadAsync(
                    location,
                    PdfParseSettings.MaxBytes(),
                    PdfParseSettings.DownloadTimeout(),
                    PdfParseSettings.AllowPrivateNetwork()).ConfigureAwait(false);
            }

            return PdfSourceLoader.ReadLocalFile(location, PdfParseSettings.MaxBytes());
        }
    }
}
