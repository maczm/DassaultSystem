using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using iTextSharp.text.pdf;

namespace MOM.Services.Pdf
{
    /// <summary>
    /// 备用文字提取：当 iTextSharp 缺少 CJK 字体包、无法解码页面文字时，
    /// 直接解析内容流里的 Tj/TJ 字符串，按字体编码（UCS2/GBK/Big5…）解码，
    /// 并用 Tm/Td 文本矩阵计算坐标。
    /// </summary>
    internal static class PdfContentTextReader
    {
        public static PdfContentLayout ReadLayout(PdfReader reader, int pageNumber)
        {
            byte[] content;
            try
            {
                content = reader.GetPageContent(pageNumber);
            }
            catch (Exception)
            {
                return null;
            }

            if (content == null || content.Length == 0)
            {
                return null;
            }

            Dictionary<string, FontDecoder> fonts = ReadFonts(reader, pageNumber);
            var layout = new PdfContentLayout();

            try
            {
                new Parser(content, fonts, layout).Parse();
            }
            catch (Exception)
            {
                return null;
            }

            return layout;
        }

        private static Dictionary<string, FontDecoder> ReadFonts(PdfReader reader, int pageNumber)
        {
            var fonts = new Dictionary<string, FontDecoder>(StringComparer.Ordinal);
            try
            {
                PdfDictionary page = reader.GetPageN(pageNumber);
                PdfDictionary resources = page.GetAsDict(PdfName.RESOURCES);
                if (resources == null)
                {
                    return fonts;
                }

                PdfDictionary fontDict = resources.GetAsDict(PdfName.FONT);
                if (fontDict == null)
                {
                    return fonts;
                }

                foreach (PdfName key in fontDict.Keys)
                {
                    PdfDictionary font = fontDict.GetAsDict(key);
                    if (font == null)
                    {
                        continue;
                    }

                    string encoding = null;
                    PdfObject encodingObject = font.Get(PdfName.ENCODING);
                    if (encodingObject != null && encodingObject.IsName())
                    {
                        encoding = encodingObject.ToString();
                    }

                    fonts[key.ToString()] = FontDecoder.Create(encoding);
                }
            }
            catch (Exception)
            {
            }

            return fonts;
        }

        /// <summary>
        /// 根据字体编码名选择解码方式。
        /// </summary>
        private sealed class FontDecoder
        {
            private Encoding _encoding;
            private bool _decodable;

            public static FontDecoder Create(string encodingName)
            {
                string name = (encodingName ?? string.Empty).TrimStart('/').ToUpperInvariant();
                var decoder = new FontDecoder();

                if (name.Contains("UCS2") || name.Contains("UTF16"))
                {
                    decoder._encoding = Encoding.BigEndianUnicode;
                    decoder._decodable = true;
                }
                else if (name.Contains("UTF8"))
                {
                    decoder._encoding = Encoding.UTF8;
                    decoder._decodable = true;
                }
                else if (name.Contains("GBK") || name.Contains("GB-EUC") || name.Contains("GBPC") || name.Contains("GBK2K"))
                {
                    decoder._encoding = SafeEncoding(936);
                    decoder._decodable = decoder._encoding != null;
                }
                else if (name.Contains("B5") || name.Contains("ETEN") || name.Contains("BIG5"))
                {
                    decoder._encoding = SafeEncoding(950);
                    decoder._decodable = decoder._encoding != null;
                }
                else if (name.Contains("KSC") || name.Contains("EUC-K") || name.Contains("UHC"))
                {
                    decoder._encoding = SafeEncoding(949);
                    decoder._decodable = decoder._encoding != null;
                }
                else if (name.Contains("IDENTITY"))
                {
                    // Identity-H 需要 ToUnicode 才能还原，这里无法解码
                    decoder._decodable = false;
                }
                else
                {
                    decoder._encoding = SafeEncoding(1252) ?? SafeEncoding(28591);
                    decoder._decodable = decoder._encoding != null;
                }

                return decoder;
            }

            public string Decode(byte[] bytes)
            {
                if (!_decodable || bytes == null || bytes.Length == 0)
                {
                    return string.Empty;
                }

                try
                {
                    return _encoding.GetString(bytes);
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }

            private static Encoding SafeEncoding(int codePage)
            {
                try
                {
                    return Encoding.GetEncoding(codePage);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private sealed class Operand
        {
            public int Kind; // 0=bytes, 1=number, 2=name, 3=array
            public byte[] Bytes;
            public double Number;
            public string Name;
            public List<Operand> Array;
        }

        private sealed class Parser
        {
            private readonly byte[] _data;
            private readonly Dictionary<string, FontDecoder> _fonts;
            private readonly List<PdfTextToken> _tokens;
            private readonly List<PdfCellRect> _rects;

            private double _a = 1;
            private double _b;
            private double _c;
            private double _d = 1;
            private double _e;
            private double _f;
            private double _leading;
            private string _fontName;
            private double _fontSize;

            public Parser(byte[] data, Dictionary<string, FontDecoder> fonts, PdfContentLayout layout)
            {
                _data = data;
                _fonts = fonts;
                _tokens = layout.Tokens;
                _rects = layout.Rects;
            }

            public void Parse()
            {
                var stack = new List<Operand>();
                int i = 0;

                while (i < _data.Length)
                {
                    byte current = _data[i];

                    if (IsWhite(current))
                    {
                        i++;
                        continue;
                    }

                    if (current == (byte)'%')
                    {
                        while (i < _data.Length && _data[i] != 10 && _data[i] != 13)
                        {
                            i++;
                        }

                        continue;
                    }

                    if (current == (byte)'(')
                    {
                        byte[] bytes;
                        i = ReadLiteralString(i, out bytes);
                        stack.Add(new Operand { Kind = 0, Bytes = bytes });
                        continue;
                    }

                    if (current == (byte)'<')
                    {
                        if (i + 1 < _data.Length && _data[i + 1] == (byte)'<')
                        {
                            i = SkipDictionary(i);
                            continue;
                        }

                        byte[] bytes;
                        i = ReadHexString(i, out bytes);
                        stack.Add(new Operand { Kind = 0, Bytes = bytes });
                        continue;
                    }

                    if (current == (byte)'[')
                    {
                        i++;
                        stack.Add(new Operand { Kind = 3, Array = ReadArray(ref i) });
                        continue;
                    }

                    if (current == (byte)']')
                    {
                        i++;
                        continue;
                    }

                    if (current == (byte)'/')
                    {
                        string name;
                        i = ReadName(i, out name);
                        stack.Add(new Operand { Kind = 2, Name = name });
                        continue;
                    }

                    if (IsNumberStart(current))
                    {
                        double number;
                        i = ReadNumber(i, out number);
                        stack.Add(new Operand { Kind = 1, Number = number });
                        continue;
                    }

                    string op = ReadOperator(ref i);
                    if (op.Length == 0)
                    {
                        i++;
                        continue;
                    }

                    Execute(op, stack);
                    stack.Clear();
                }
            }

            private List<Operand> ReadArray(ref int i)
            {
                var list = new List<Operand>();

                while (i < _data.Length)
                {
                    byte current = _data[i];

                    if (IsWhite(current))
                    {
                        i++;
                        continue;
                    }

                    if (current == (byte)']')
                    {
                        i++;
                        break;
                    }

                    if (current == (byte)'(')
                    {
                        byte[] bytes;
                        i = ReadLiteralString(i, out bytes);
                        list.Add(new Operand { Kind = 0, Bytes = bytes });
                        continue;
                    }

                    if (current == (byte)'<')
                    {
                        byte[] bytes;
                        i = ReadHexString(i, out bytes);
                        list.Add(new Operand { Kind = 0, Bytes = bytes });
                        continue;
                    }

                    if (IsNumberStart(current))
                    {
                        double number;
                        i = ReadNumber(i, out number);
                        list.Add(new Operand { Kind = 1, Number = number });
                        continue;
                    }

                    if (current == (byte)'/')
                    {
                        string name;
                        i = ReadName(i, out name);
                        list.Add(new Operand { Kind = 2, Name = name });
                        continue;
                    }

                    i++;
                }

                return list;
            }

            private void Execute(string op, List<Operand> stack)
            {
                switch (op)
                {
                    case "BT":
                        _a = 1;
                        _b = 0;
                        _c = 0;
                        _d = 1;
                        _e = 0;
                        _f = 0;
                        _fontName = null;
                        _fontSize = 0;
                        break;

                    case "Tm":
                        if (stack.Count >= 6)
                        {
                            _a = Number(stack[stack.Count - 6]);
                            _b = Number(stack[stack.Count - 5]);
                            _c = Number(stack[stack.Count - 4]);
                            _d = Number(stack[stack.Count - 3]);
                            _e = Number(stack[stack.Count - 2]);
                            _f = Number(stack[stack.Count - 1]);
                        }

                        break;

                    case "Td":
                        if (stack.Count >= 2)
                        {
                            Translate(Number(stack[stack.Count - 2]), Number(stack[stack.Count - 1]));
                        }

                        break;

                    case "TD":
                        if (stack.Count >= 2)
                        {
                            double tx = Number(stack[stack.Count - 2]);
                            double ty = Number(stack[stack.Count - 1]);
                            Translate(tx, ty);
                            _leading = -ty;
                        }

                        break;

                    case "T*":
                        Translate(0, -_leading);
                        break;

                    case "TL":
                        if (stack.Count >= 1)
                        {
                            _leading = Number(stack[stack.Count - 1]);
                        }

                        break;

                    case "re":
                        if (stack.Count >= 4)
                        {
                            double x = Number(stack[stack.Count - 4]);
                            double y = Number(stack[stack.Count - 3]);
                            double width = Number(stack[stack.Count - 2]);
                            double height = Number(stack[stack.Count - 1]);
                            if (width > 0 && height > 0)
                            {
                                _rects.Add(new PdfCellRect
                                {
                                    X = (float)x,
                                    Y = (float)y,
                                    Width = (float)width,
                                    Height = (float)height
                                });
                            }
                        }

                        break;

                    case "Tf":
                        if (stack.Count >= 2)
                        {
                            _fontName = stack[stack.Count - 2].Name;
                            _fontSize = Number(stack[stack.Count - 1]);
                        }

                        break;

                    case "Tj":
                        if (stack.Count >= 1 && stack[stack.Count - 1].Kind == 0)
                        {
                            Show(stack[stack.Count - 1].Bytes);
                        }

                        break;

                    case "TJ":
                        if (stack.Count >= 1 && stack[stack.Count - 1].Kind == 3)
                        {
                            foreach (Operand operand in stack[stack.Count - 1].Array)
                            {
                                if (operand.Kind == 0)
                                {
                                    Show(operand.Bytes);
                                }
                            }
                        }

                        break;

                    case "'":
                        Translate(0, -_leading);
                        if (stack.Count >= 1 && stack[stack.Count - 1].Kind == 0)
                        {
                            Show(stack[stack.Count - 1].Bytes);
                        }

                        break;

                    case "\"":
                        if (stack.Count >= 3 && stack[stack.Count - 1].Kind == 0)
                        {
                            Show(stack[stack.Count - 1].Bytes);
                        }

                        break;
                }
            }

            private void Translate(double tx, double ty)
            {
                double e = tx * _a + ty * _c + _e;
                double f = tx * _b + ty * _d + _f;
                _e = e;
                _f = f;
            }

            private void Show(byte[] bytes)
            {
                if (bytes == null || bytes.Length == 0 || _fontName == null)
                {
                    return;
                }

                FontDecoder decoder;
                if (!_fonts.TryGetValue(_fontName, out decoder))
                {
                    return;
                }

                string text = decoder.Decode(bytes);
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                text = text.Replace("\0", string.Empty).Trim();
                if (text.Length == 0)
                {
                    return;
                }

                float x = (float)_e;
                float y = (float)_f;
                float size = (float)Math.Max(1.0, _fontSize);
                float width = Math.Max(1f, (float)(text.Length * size * 0.6));

                _tokens.Add(new PdfTextToken
                {
                    Text = text,
                    X0 = x,
                    X1 = x + width,
                    Y = y,
                    Height = size
                });

                _e += width;
            }

            private double Number(Operand operand)
            {
                return operand != null && operand.Kind == 1 ? operand.Number : 0.0;
            }

            private static bool IsWhite(byte value)
            {
                return value == 0 || value == 9 || value == 10 || value == 12 || value == 13 || value == 32;
            }

            private static bool IsNumberStart(byte value)
            {
                return (value >= (byte)'0' && value <= (byte)'9') || value == (byte)'-' || value == (byte)'+' || value == (byte)'.';
            }

            private static bool IsDelimiter(byte value)
            {
                return IsWhite(value) || value == (byte)'(' || value == (byte)')' || value == (byte)'<' ||
                       value == (byte)'>' || value == (byte)'[' || value == (byte)']' || value == (byte)'/' ||
                       value == (byte)'%' || value == (byte)'{' || value == (byte)'}';
            }

            private int ReadName(int i, out string name)
            {
                i++; // skip '/'
                var builder = new StringBuilder();

                while (i < _data.Length && !IsDelimiter(_data[i]))
                {
                    builder.Append((char)_data[i]);
                    i++;
                }

                name = "/" + builder;
                return i;
            }

            private int ReadNumber(int i, out double number)
            {
                int start = i;
                if (_data[i] == (byte)'+' || _data[i] == (byte)'-')
                {
                    i++;
                }

                while (i < _data.Length && ((_data[i] >= (byte)'0' && _data[i] <= (byte)'9') || _data[i] == (byte)'.'))
                {
                    i++;
                }

                if (i < _data.Length && (_data[i] == (byte)'e' || _data[i] == (byte)'E'))
                {
                    i++;
                    if (i < _data.Length && (_data[i] == (byte)'+' || _data[i] == (byte)'-'))
                    {
                        i++;
                    }

                    while (i < _data.Length && _data[i] >= (byte)'0' && _data[i] <= (byte)'9')
                    {
                        i++;
                    }
                }

                string text = Encoding.ASCII.GetString(_data, start, i - start);
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                return i;
            }

            private string ReadOperator(ref int i)
            {
                int start = i;

                while (i < _data.Length && !IsDelimiter(_data[i]))
                {
                    char ch = (char)_data[i];
                    if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == '*' || ch == '\'' || ch == '"')
                    {
                        i++;
                        continue;
                    }

                    break;
                }

                if (i == start)
                {
                    i++;
                    return string.Empty;
                }

                return Encoding.ASCII.GetString(_data, start, i - start);
            }

            private int ReadLiteralString(int i, out byte[] bytes)
            {
                var buffer = new List<byte>();
                i++; // skip '('
                int depth = 1;

                while (i < _data.Length)
                {
                    byte current = _data[i];

                    if (current == (byte)'\\')
                    {
                        i++;
                        if (i >= _data.Length)
                        {
                            break;
                        }

                        byte escaped = _data[i];
                        switch ((char)escaped)
                        {
                            case 'n':
                                buffer.Add(10);
                                i++;
                                break;
                            case 'r':
                                buffer.Add(13);
                                i++;
                                break;
                            case 't':
                                buffer.Add(9);
                                i++;
                                break;
                            case 'b':
                                buffer.Add(8);
                                i++;
                                break;
                            case 'f':
                                buffer.Add(12);
                                i++;
                                break;
                            case '(':
                            case ')':
                            case '\\':
                                buffer.Add(escaped);
                                i++;
                                break;
                            case '\r':
                                i++;
                                if (i < _data.Length && _data[i] == (byte)'\n')
                                {
                                    i++;
                                }

                                break;
                            case '\n':
                                i++;
                                break;
                            default:
                                if (escaped >= (byte)'0' && escaped <= (byte)'7')
                                {
                                    int value = 0;
                                    int digits = 0;
                                    while (i < _data.Length && digits < 3 && _data[i] >= (byte)'0' && _data[i] <= (byte)'7')
                                    {
                                        value = value * 8 + (_data[i] - (byte)'0');
                                        i++;
                                        digits++;
                                    }

                                    buffer.Add((byte)(value & 0xFF));
                                }
                                else
                                {
                                    buffer.Add(escaped);
                                    i++;
                                }

                                break;
                        }

                        continue;
                    }

                    if (current == (byte)'(')
                    {
                        depth++;
                        buffer.Add(current);
                        i++;
                        continue;
                    }

                    if (current == (byte)')')
                    {
                        depth--;
                        i++;
                        if (depth == 0)
                        {
                            break;
                        }

                        buffer.Add(current);
                        continue;
                    }

                    buffer.Add(current);
                    i++;
                }

                bytes = buffer.ToArray();
                return i;
            }

            private int ReadHexString(int i, out byte[] bytes)
            {
                var buffer = new List<byte>();
                i++; // skip '<'
                var hex = new StringBuilder();

                while (i < _data.Length && _data[i] != (byte)'>')
                {
                    char ch = (char)_data[i];
                    if (IsHex(ch))
                    {
                        hex.Append(ch);
                    }

                    i++;
                }

                if (i < _data.Length)
                {
                    i++; // skip '>'
                }

                if (hex.Length % 2 == 1)
                {
                    hex.Append('0');
                }

                for (int index = 0; index < hex.Length; index += 2)
                {
                    buffer.Add(Convert.ToByte(hex.ToString(index, 2), 16));
                }

                bytes = buffer.ToArray();
                return i;
            }

            private bool IsHex(char ch)
            {
                return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
            }

            private int SkipDictionary(int i)
            {
                int depth = 0;
                i += 2;

                while (i < _data.Length)
                {
                    if (_data[i] == (byte)'<' && i + 1 < _data.Length && _data[i + 1] == (byte)'<')
                    {
                        depth++;
                        i += 2;
                        continue;
                    }

                    if (_data[i] == (byte)'>' && i + 1 < _data.Length && _data[i + 1] == (byte)'>')
                    {
                        if (depth == 0)
                        {
                            i += 2;
                            break;
                        }

                        depth--;
                        i += 2;
                        continue;
                    }

                    i++;
                }

                return i;
            }
        }
    }
}
