using System.Text;

namespace MOM.Utils
{
    /// <summary>
    /// 文本归一化：把值里的中文标点、全角字符转成英文半角，
    /// 连续空格合并为一个并去掉首尾空格。字段名（key）不做处理。
    /// </summary>
    public static class TextNormalizer
    {
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            string text = value.Replace("——", "-").Replace("…", "...");
            var builder = new StringBuilder(text.Length);

            foreach (char ch in text)
            {
                builder.Append(MapChar(ch));
            }

            // 合并连续空格并去掉首尾空格
            var result = new StringBuilder(builder.Length);
            bool lastWasSpace = false;

            foreach (char ch in builder.ToString())
            {
                if (ch == ' ' || ch == '\u3000' || ch == '\u00A0')
                {
                    if (lastWasSpace)
                    {
                        continue;
                    }

                    lastWasSpace = true;
                    result.Append(' ');
                    continue;
                }

                lastWasSpace = false;
                result.Append(ch);
            }

            return result.ToString().Trim();
        }

        private static char MapChar(char ch)
        {
            // 全角数字/字母 → 半角
            if (ch >= '０' && ch <= '９')
            {
                return (char)(ch - 0xFF10 + '0');
            }

            if (ch >= 'Ａ' && ch <= 'Ｚ')
            {
                return (char)(ch - 0xFF21 + 'A');
            }

            if (ch >= 'ａ' && ch <= 'ｚ')
            {
                return (char)(ch - 0xFF41 + 'a');
            }

            switch (ch)
            {
                case '，':
                    return ',';
                case '、':
                    return ',';
                case '；':
                    return ';';
                case '：':
                    return ':';
                case '。':
                case '．':
                case '｡':
                    return '.';
                case '—':
                case '–':
                case '―':
                    return '-';
                case '！':
                    return '!';
                case '？':
                    return '?';
                case '（':
                    return '(';
                case '）':
                    return ')';
                case '【':
                    return '[';
                case '】':
                    return ']';
                case '《':
                    return '<';
                case '》':
                    return '>';
                case '“':
                case '”':
                    return '"';
                case '‘':
                case '’':
                case '＇':
                    return '\'';
                case '～':
                    return '~';
                case '％':
                    return '%';
                case '＃':
                    return '#';
                case '＠':
                    return '@';
                case '＆':
                    return '&';
                case '＊':
                    return '*';
                case '＋':
                    return '+';
                case '＝':
                    return '=';
                case '－':
                    return '-';
                case '｜':
                    return '|';
                case '／':
                    return '/';
                case '＼':
                    return '\\';
                case '＜':
                    return '<';
                case '＞':
                    return '>';
                case '［':
                    return '[';
                case '］':
                    return ']';
                case '｛':
                    return '{';
                case '｝':
                    return '}';
                default:
                    return ch;
            }
        }
    }
}
