using System.Collections.Generic;

namespace MOM.Services.Pdf
{
    /// <summary>
    /// 一页的文字与单元格布局。
    /// </summary>
    internal sealed class PdfContentLayout
    {
        public List<PdfTextToken> Tokens = new List<PdfTextToken>();
        public List<PdfCellRect> Rects = new List<PdfCellRect>();
    }
}
