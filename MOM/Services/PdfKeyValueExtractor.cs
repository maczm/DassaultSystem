using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using MOM.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MOM.Services
{
    /// <summary>
    /// 证书/表单类 PDF 的“标签 -> 值”提取。
    ///
    /// 做法：先按 X 坐标把文字聚成若干列，再在每列内把同一单元格的换行合并成文本块，
    /// 最后把相邻两列（标签列、值列）的文本块按垂直重叠配对。
    /// 这样即使标签换行、值在单元格内垂直居中，也不会串行或丢字段。
    /// </summary>
    public static class PdfKeyValueExtractor
    {
        private static readonly Regex PageArtifactPattern = new Regex(
            @"^(第\s*\d+\s*页.*|.*共\s*\d+\s*页.*|\d+)$", RegexOptions.Compiled);

        public static IList<PdfKeyValuePair> Extract(PdfReader reader, IList<int> pageNumbers)
        {
            var entries = new List<PairEntry>();
            foreach (int pageNumber in pageNumbers)
            {
                try
                {
                    ExtractPage(reader, pageNumber, entries);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(string.Format(
                        CultureInfo.InvariantCulture, "PDF 第 {0} 页键值对识别失败：{1}", pageNumber, ex.Message));
                }
            }

            return entries
                .Where(entry => entry.Pair.Key.Length > 0 && !PageArtifactPattern.IsMatch(entry.Pair.Key.Trim()))
                .OrderBy(entry => entry.Page)
                .ThenByDescending(entry => entry.RowY)
                .ThenBy(entry => entry.Left)
                .Select(entry => entry.Pair)
                .ToList();
        }

        private static void ExtractPage(PdfReader reader, int pageNumber, List<PairEntry> entries)
        {
            PdfContentLayout layout = PdfContentTextReader.ReadLayout(reader, pageNumber);
            List<PdfTextToken> tokens = CollectTokens(reader, pageNumber, layout, out bool fromContentStream);

            if (tokens.Count == 0)
            {
                return;
            }

            // 内容流兜底解析时，页面里的表格线（矩形）是可靠的单元格边界，直接按单元格识别
            if (fromContentStream && layout != null && CountUsableRects(layout.Rects) >= 8)
            {
                ExtractByCells(tokens, layout.Rects, pageNumber, entries);
                return;
            }

            float medianHeight = Median(tokens.Where(t => t.Height > 0).Select(t => t.Height).ToList());
            if (medianHeight <= 0)
            {
                medianHeight = 10f;
            }

            float columnTolerance = Math.Max(12f, medianHeight * 1.5f);
            List<ColumnCluster> clusters = BuildColumnCenters(tokens, columnTolerance);
            if (clusters.Count < 2)
            {
                // 没有分列，说明不是表单版式
                return;
            }

            // 只保留真正的表格列：标题、水印等零散文字单独成簇，直接忽略
            int minColumnTokens = Math.Max(2, tokens.Count / 30);
            List<float> centers = clusters
                .Where(cluster => cluster.Count >= minColumnTokens)
                .Select(cluster => cluster.Center)
                .ToList();

            if (centers.Count < 2)
            {
                return;
            }

            var columns = new List<PdfTextToken>[centers.Count];
            for (int i = 0; i < columns.Length; i++)
            {
                columns[i] = new List<PdfTextToken>();
            }

            var columnIndex = new Dictionary<PdfTextToken, int>();
            foreach (PdfTextToken token in tokens)
            {
                int index = NearestCenter(centers, token.X0);
                columnIndex[token] = index;
                columns[index].Add(token);
            }

            // 表格顶行以上（标题、页眉）不参与识别
            float tableTop = FindTableTop(tokens, columnIndex, medianHeight);
            if (tableTop > float.MinValue)
            {
                foreach (List<PdfTextToken> column in columns)
                {
                    column.RemoveAll(token => token.Y > tableTop + 1f);
                }
            }

            var blocks = new List<List<Block>>(columns.Length);
            foreach (List<PdfTextToken> column in columns)
            {
                blocks.Add(BuildBlocks(column, medianHeight));
            }

            var pageEntries = new List<PairEntry>();
            var usedValues = new HashSet<Block>();
            for (int i = 0; i + 1 < blocks.Count; i += 2)
            {
                PairColumns(blocks[i], blocks[i + 1], pageNumber, medianHeight, pageEntries, usedValues);
            }

            // 没有对应键列的孤立值（合并表头跨列时常见）：挂到左侧最近一列的文字上
            for (int column = 1; column < blocks.Count; column += 2)
            {
                foreach (Block value in blocks[column])
                {
                    if (usedValues.Contains(value))
                    {
                        continue;
                    }

                    int keyColumn = column - 2;
                    if (keyColumn < 0)
                    {
                        continue;
                    }

                    Block key = FindNearestBlock(blocks[keyColumn], value.CenterY, medianHeight * 2f);
                    if (key == null)
                    {
                        continue;
                    }

                    string keyText = CleanKey(key.Text);
                    if (keyText.Length == 0)
                    {
                        continue;
                    }

                    pageEntries.Add(new PairEntry
                    {
                        Pair = new PdfKeyValuePair { Key = keyText, Value = value.Text.Trim() },
                        Page = pageNumber,
                        RowY = value.CenterY,
                        Left = key.Left
                    });
                    usedValues.Add(value);
                }
            }

            if (blocks.Count % 2 == 1)
            {
                foreach (Block block in blocks[blocks.Count - 1])
                {
                    string key = CleanKey(block.Text);
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    pageEntries.Add(new PairEntry
                    {
                        Pair = new PdfKeyValuePair { Key = key, Value = string.Empty },
                        Page = pageNumber,
                        RowY = block.CenterY,
                        Left = block.Left
                    });
                }
            }

            entries.AddRange(pageEntries);
        }

        private static int CountUsableRects(List<PdfCellRect> rects)
        {
            if (rects == null)
            {
                return 0;
            }

            int count = 0;
            foreach (PdfCellRect rect in rects)
            {
                if (rect.Width > 8 && rect.Height > 5)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 按页面里画出的表格单元格识别：文字按坐标落进单元格，再按行从左到右两两配对，
        /// 跨行合并的单元格（如“声级”）会额外与它覆盖到的上一行首格配对。
        /// </summary>
        private static void ExtractByCells(
            List<PdfTextToken> tokens, List<PdfCellRect> rects, int pageNumber, List<PairEntry> entries)
        {
            var cells = new List<CellBox>();
            foreach (PdfCellRect rect in rects)
            {
                if (rect.Width > 8 && rect.Height > 5)
                {
                    cells.Add(new CellBox(rect));
                }
            }

            if (cells.Count < 4)
            {
                return;
            }

            foreach (PdfTextToken token in tokens)
            {
                CellBox best = null;
                double bestArea = double.MaxValue;

                foreach (CellBox cell in cells)
                {
                    if (!cell.Contains(token))
                    {
                        continue;
                    }

                    double area = (double)cell.Width * cell.Height;
                    if (area < bestArea)
                    {
                        bestArea = area;
                        best = cell;
                    }
                }

                if (best != null)
                {
                    best.Add(token);
                }
            }

            List<IGrouping<double, CellBox>> rows = cells
                .GroupBy(cell => Math.Round(cell.Y, 1))
                .OrderByDescending(group => group.Key)
                .ToList();

            float medianHeight = Median(cells.Select(cell => cell.Height).ToList());
            float tallThreshold = medianHeight * 1.8f;
            var pageEntries = new List<PairEntry>();

            foreach (CellBox tall in cells.Where(cell => cell.Height > tallThreshold))
            {
                float top = tall.Y + tall.Height;
                string key = CleanKey(tall.Text);
                if (key.Length == 0)
                {
                    continue;
                }

                foreach (IGrouping<double, CellBox> group in rows)
                {
                    float rowY = (float)group.Key;
                    if (rowY <= tall.Y + 1f || rowY >= top - 1f)
                    {
                        continue;
                    }

                    CellBox first = group
                        .Where(cell => cell != tall)
                        .OrderBy(cell => cell.X)
                        .FirstOrDefault(cell => CleanKey(cell.Text).Length > 0);

                    if (first == null)
                    {
                        continue;
                    }

                    pageEntries.Add(new PairEntry
                    {
                        Pair = new PdfKeyValuePair { Key = key, Value = CleanKey(first.Text) },
                        Page = pageNumber,
                        RowY = top - 0.1f,
                        Left = tall.X
                    });
                }
            }

            foreach (IGrouping<double, CellBox> group in rows)
            {
                float rowY = (float)group.Key;
                List<CellBox> rowCells = group.OrderBy(cell => cell.X).ToList();

                for (int i = 0; i + 1 < rowCells.Count; i += 2)
                {
                    AddCellPair(rowCells[i], rowCells[i + 1], pageNumber, rowY, tallThreshold, pageEntries);
                }

                if (rowCells.Count >= 3 && rowCells.Count % 2 == 1)
                {
                    CellBox previous = rowCells[rowCells.Count - 2];
                    CellBox last = rowCells[rowCells.Count - 1];
                    if (CleanKey(last.Text).Length > 0)
                    {
                        AddCellPair(previous, last, pageNumber, rowY, tallThreshold, pageEntries);
                    }
                }
                else if (rowCells.Count == 1)
                {
                    AddCellPair(rowCells[0], null, pageNumber, rowY, tallThreshold, pageEntries);
                }
            }

            entries.AddRange(pageEntries);
        }

        private static void AddCellPair(
            CellBox keyCell, CellBox valueCell, int pageNumber, float rowY,
            float tallThreshold, List<PairEntry> output)
        {
            string key = CleanKey(keyCell.Text);
            if (key.Length == 0)
            {
                return;
            }

            output.Add(new PairEntry
            {
                Pair = new PdfKeyValuePair
                {
                    Key = key,
                    Value = valueCell == null ? string.Empty : (valueCell.Text ?? string.Empty).Trim()
                },
                Page = pageNumber,
                RowY = keyCell.Height > tallThreshold ? keyCell.Y + keyCell.Height - 0.1f : rowY,
                Left = keyCell.X
            });
        }

        private sealed class CellBox
        {
            private readonly List<PdfTextToken> _tokens = new List<PdfTextToken>();

            public CellBox(PdfCellRect rect)
            {
                X = rect.X;
                Y = rect.Y;
                Width = rect.Width;
                Height = rect.Height;
            }

            public float X { get; private set; }
            public float Y { get; private set; }
            public float Width { get; private set; }
            public float Height { get; private set; }

            public void Add(PdfTextToken token)
            {
                _tokens.Add(token);
            }

            public bool Contains(PdfTextToken token)
            {
                return token.X0 >= X - 1 && token.X0 <= X + Width + 1 &&
                       token.Y >= Y - 1 && token.Y <= Y + Height + 1;
            }

            public string Text
            {
                get
                {
                    if (_tokens.Count == 0)
                    {
                        return string.Empty;
                    }

                    List<PdfTextToken> ordered = _tokens
                        .OrderByDescending(token => token.Y)
                        .ThenBy(token => token.X0)
                        .ToList();

                    string text = ordered[0].Text;
                    float lastY = ordered[0].Y;

                    for (int i = 1; i < ordered.Count; i++)
                    {
                        text = Math.Abs(lastY - ordered[i].Y) <= 1.5f
                            ? JoinParts(text, ordered[i].Text)
                            : JoinContinuation(text, ordered[i].Text);
                        lastY = ordered[i].Y;
                    }

                    return text;
                }
            }
        }

        private static void PairColumns(
            List<Block> keys, List<Block> values, int pageNumber, float medianHeight,
            List<PairEntry> output, HashSet<Block> usedValues)
        {
            float band = medianHeight * 1.2f;

            foreach (Block key in keys)
            {
                string keyText = CleanKey(key.Text);
                if (keyText.Length == 0)
                {
                    continue;
                }

                // 落在同一行带内的值都算这个键的值（合并单元格会对应多行）
                var matches = new List<Block>();
                foreach (Block value in values)
                {
                    if (usedValues.Contains(value))
                    {
                        continue;
                    }

                    if (Math.Abs(value.CenterY - key.CenterY) <= band)
                    {
                        matches.Add(value);
                    }
                }

                if (matches.Count == 0)
                {
                    output.Add(new PairEntry
                    {
                        Pair = new PdfKeyValuePair { Key = keyText, Value = string.Empty },
                        Page = pageNumber,
                        RowY = key.CenterY,
                        Left = key.Left
                    });
                    continue;
                }

                matches.Sort((left, right) => right.CenterY.CompareTo(left.CenterY));

                // 合并单元格（一个键对应多个值）时，这些键值对按最上面的值排序，
                // 保证输出顺序是“合并项连续、再按行”。
                float rowY = matches[0].CenterY;

                foreach (Block value in matches)
                {
                    usedValues.Add(value);
                    output.Add(new PairEntry
                    {
                        Pair = new PdfKeyValuePair { Key = keyText, Value = value.Text.Trim() },
                        Page = pageNumber,
                        RowY = rowY,
                        Left = key.Left
                    });
                }
            }
        }

        private static Block FindNearestBlock(List<Block> blocks, float centerY, float maxDistance)
        {
            Block best = null;
            float bestDistance = float.MaxValue;

            foreach (Block block in blocks)
            {
                float distance = Math.Abs(block.CenterY - centerY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = block;
                }
            }

            return bestDistance <= maxDistance ? best : null;
        }

        private static List<ColumnCluster> BuildColumnCenters(List<PdfTextToken> tokens, float tolerance)
        {
            List<PdfTextToken> ordered = tokens.OrderBy(t => t.X0).ToList();
            var clusters = new List<ColumnCluster>();
            var group = new List<float>();

            foreach (PdfTextToken token in ordered)
            {
                if (group.Count > 0 && token.X0 - group[group.Count - 1] > tolerance)
                {
                    clusters.Add(new ColumnCluster { Center = Median(group), Count = group.Count });
                    group.Clear();
                }

                group.Add(token.X0);
            }

            if (group.Count > 0)
            {
                clusters.Add(new ColumnCluster { Center = Median(group), Count = group.Count });
            }

            return clusters;
        }

        /// <summary>
        /// 找到表格最上面一行（同一水平带里至少出现两列文字），其上方视为标题/页眉。
        /// </summary>
        private static float FindTableTop(
            List<PdfTextToken> tokens, Dictionary<PdfTextToken, int> columnIndex, float medianHeight)
        {
            float band = Math.Max(2f, medianHeight * 0.6f);
            var bands = new Dictionary<int, HashSet<int>>();

            foreach (PdfTextToken token in tokens)
            {
                int key = (int)Math.Round(token.Y / band);
                HashSet<int> columns;
                if (!bands.TryGetValue(key, out columns))
                {
                    columns = new HashSet<int>();
                    bands[key] = columns;
                }

                columns.Add(columnIndex[token]);
            }

            float top = float.MinValue;
            foreach (PdfTextToken token in tokens)
            {
                int key = (int)Math.Round(token.Y / band);
                if (bands[key].Count >= 2)
                {
                    top = Math.Max(top, token.Y);
                }
            }

            return top;
        }

        private static int NearestCenter(List<float> centers, float x)
        {
            int best = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < centers.Count; i++)
            {
                float distance = Math.Abs(centers[i] - x);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// 同一列内按行距分块：行距明显小于正常行距时，视为同一单元格的换行。
        /// </summary>
        private static List<Block> BuildBlocks(List<PdfTextToken> tokens, float medianHeight)
        {
            var blocks = new List<Block>();
            if (tokens.Count == 0)
            {
                return blocks;
            }

            List<PdfTextToken> ordered = tokens
                .OrderByDescending(t => t.Y)
                .ThenBy(t => t.X0)
                .ToList();

            var gaps = new List<float>();
            for (int i = 1; i < ordered.Count; i++)
            {
                float gap = ordered[i - 1].Y - ordered[i].Y;
                if (gap > 0.1f)
                {
                    gaps.Add(gap);
                }
            }

            float medianGap = Median(gaps);
            float threshold = Math.Max(4f, Math.Min(medianHeight * 1.5f, medianGap > 0 ? medianGap * 0.85f : medianHeight * 1.5f));

            Block current = null;
            float lastY = 0f;

            foreach (PdfTextToken token in ordered)
            {
                if (current == null || lastY - token.Y > threshold)
                {
                    current = new Block();
                    blocks.Add(current);
                }

                current.Add(token);
                lastY = token.Y;
            }

            return blocks;
        }

        private static string CleanKey(string value)
        {
            string key = (value ?? string.Empty).Trim();
            key = key.TrimEnd('：', ':');
            if (key.Length > 200)
            {
                key = key.Substring(0, 200);
            }

            return key;
        }

        private static string JoinParts(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
            {
                return right ?? string.Empty;
            }

            if (string.IsNullOrEmpty(right))
            {
                return left;
            }

            if (IsAsciiWordChar(left[left.Length - 1]) && IsAsciiWordChar(right[0]))
            {
                return left + " " + right;
            }

            return left + right;
        }

        private static string JoinContinuation(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
            {
                return right ?? string.Empty;
            }

            if (string.IsNullOrEmpty(right))
            {
                return left;
            }

            char last = left[left.Length - 1];
            char first = right[0];
            bool englishWordWrap = last < 128 && first < 128 && char.IsLetter(last) && char.IsLetter(first);
            return englishWordWrap ? left + " " + right : left + right;
        }

        private static bool IsAsciiWordChar(char value)
        {
            return value < 128 && char.IsLetterOrDigit(value);
        }

        private static float Median(List<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 1
                ? values[middle]
                : (values[middle - 1] + values[middle]) / 2f;
        }

        private static List<PdfTextToken> CollectTokens(
            PdfReader reader, int pageNumber, PdfContentLayout layout, out bool fromContentStream)
        {
            fromContentStream = false;

            var collector = new TextCollector();
            var parser = new PdfReaderContentParser(reader);
            parser.ProcessContent(pageNumber, collector);

            if (collector.Tokens.Any(token => !string.IsNullOrWhiteSpace(token.Text)))
            {
                return collector.Tokens;
            }

            // iTextSharp 缺少 CJK 字体包时解码不出文字，改用内容流兜底解析
            fromContentStream = true;
            return layout != null ? layout.Tokens : new List<PdfTextToken>();
        }

        private sealed class ColumnCluster
        {
            public float Center;
            public int Count;
        }

        private sealed class PairEntry
        {
            public PdfKeyValuePair Pair;
            public int Page;
            public float RowY;
            public float Left;
        }

        private sealed class Block
        {
            private readonly StringBuilder _text = new StringBuilder();
            private float _lastY = float.MinValue;

            public float Top = float.MinValue;
            public float Bottom = float.MaxValue;
            public float Left = float.MaxValue;

            public string Text
            {
                get { return _text.ToString(); }
            }

            public float CenterY
            {
                get { return (Top + Bottom) / 2f; }
            }

            public void Add(PdfTextToken token)
            {
                string current = Text;

                if (current.Length == 0)
                {
                    _text.Append(token.Text);
                }
                else if (Math.Abs(_lastY - token.Y) <= 1.5f)
                {
                    _text.Length = 0;
                    _text.Append(JoinParts(current, token.Text));
                }
                else
                {
                    _text.Length = 0;
                    _text.Append(JoinContinuation(current, token.Text));
                }

                _lastY = token.Y;
                Top = Math.Max(Top, token.Y);
                Bottom = Math.Min(Bottom, token.Y);
                Left = Math.Min(Left, token.X0);
            }
        }


        private sealed class TextCollector : IRenderListener
        {
            public TextCollector()
            {
                Tokens = new List<PdfTextToken>();
            }

            public List<PdfTextToken> Tokens { get; private set; }

            public void BeginTextBlock()
            {
            }

            public void EndTextBlock()
            {
            }

            public void RenderImage(ImageRenderInfo renderInfo)
            {
            }

            public void RenderText(TextRenderInfo renderInfo)
            {
                string text = renderInfo.GetText();
                if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
                {
                    return;
                }

                try
                {
                    LineSegment baseline = renderInfo.GetBaseline();
                    Vector start = baseline.GetStartPoint();
                    Vector end = baseline.GetEndPoint();

                    float x0 = start[Vector.I1];
                    float y = start[Vector.I2];
                    float x1 = end[Vector.I1];

                    if (Math.Abs(end[Vector.I2] - y) > 0.5f || x1 <= x0)
                    {
                        return;
                    }

                    float height = 0f;
                    try
                    {
                        height = renderInfo.GetAscentLine().GetStartPoint()[Vector.I2] - y;
                    }
                    catch (Exception)
                    {
                        height = 0f;
                    }

                    Tokens.Add(new PdfTextToken
                    {
                        Text = text.Trim(),
                        X0 = x0,
                        X1 = x1,
                        Y = y,
                        Height = height
                    });
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
