using System;
using System.Collections.Generic;
using System.Text;
using iTextSharp.text.exceptions;
using iTextSharp.text.pdf;
using MOM.Common;

namespace MOM.Services.Pdf
{
    /// <summary>
    /// 把 PDF 字节内容解析成“标签 -> 值”列表。
    /// 负责文件头校验、密码处理、iText 异常到业务错误码的翻译，不涉及 HTTP 与配置。
    /// </summary>
    public static class PdfDocumentParser
    {
        public static IList<PdfKeyValuePair> Parse(byte[] content, string password)
        {
            if (content == null || content.Length == 0)
            {
                throw new ApiException(ApiErrorCode.InvalidArgument, "PDF 内容为空。");
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
                    throw new ApiException(
                        ApiErrorCode.PasswordRequired, "该 PDF 已加密，请通过 password 提供打开密码。");
                }

                throw new ApiException(ApiErrorCode.PasswordInvalid, "PDF 密码不正确，无法打开文件。");
            }
            catch (UnsupportedPdfException ex)
            {
                throw new ApiException(
                    ApiErrorCode.UnsupportedPdf, "该 PDF 使用了暂不支持的特性：" + ex.Message, ex);
            }
            catch (InvalidPdfException ex)
            {
                throw new ApiException(ApiErrorCode.InvalidPdf, "无法解析该 PDF 文件：" + ex.Message, ex);
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
                throw new ApiException(
                    ApiErrorCode.InvalidPdf, "内容不是有效的 PDF 文件（未找到 %PDF- 文件头）。");
            }
        }
    }
}
