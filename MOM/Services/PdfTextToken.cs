using System.Collections.Generic;

namespace MOM.Services
{
    /// <summary>
    /// 一段带位置信息的文字，供键值对识别使用。
    /// </summary>
    internal sealed class PdfTextToken
    {
        public string Text;
        public float X0;
        public float X1;
        public float Y;
        public float Height;
    }

    /// <summary>
    /// PDF 里绘制出的一个矩形（表格单元格）。
    /// </summary>
    internal sealed class PdfCellRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    /// <summary>
    /// 一页的文字与单元格布局。
    /// </summary>
    internal sealed class PdfContentLayout
    {
        public List<PdfTextToken> Tokens = new List<PdfTextToken>();
        public List<PdfCellRect> Rects = new List<PdfCellRect>();
    }
}
