namespace MOM.Services.Pdf
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
}
