using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using MOM.Common;
using MOM.Models;
using MOM.Services.Pdf;

namespace MOM.Controllers
{
    /// <summary>
    /// PDF 解析：按 template 输出英文 key 的 JSON，并返回该 JSON 的 SHA-256。
    /// </summary>
    [RoutePrefix("api/pdf")]
    public class PdfController : ApiControllerBase
    {
        /// <summary>
        /// 解析 PDF 并按 template 输出。
        /// 请求体：{ "url": "…", "password": "…", "template": { "vin": "车辆识别代号", … } }
        /// 返回 data.result 为 template 形状的 JSON，data.hash 为它的 SHA-256。
        /// </summary>
        [HttpPost, Route("parse")]
        public async Task<HttpResponseMessage> Parse(PdfParseRequest request)
        {
            try
            {
                if (request == null)
                {
                    throw new ApiException(ApiErrorCode.InvalidJson, "请求体不是有效的 JSON。");
                }

                return Success(await PdfParseService.ParseAsync(request).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
        }
    }
}
