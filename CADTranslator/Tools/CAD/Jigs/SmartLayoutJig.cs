// 文件路径: CADTranslator/AutoCAD/Jigs/SmartLayoutJig.cs
// ！！！请用下面的全部代码，完整替换掉原有的文件内容 ！！！

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CADTranslator.Models;
using CADTranslator.Models.CAD;

namespace CADTranslator.Tools.CAD.Jigs
    {
    public class SmartLayoutJig : DrawJig
        {
        private static readonly HashSet<char> ForbiddenStartChars = new HashSet<char>
        {
            '!', ',', '.', ':', ';', '?', '}', ']', ')', '】', '）', '』', '》', '”', '’',
            '，', '。', '；', '：', '！', '？'
        };

        public List<Tuple<string, bool, bool, int, Point3d>> FinalLineInfo { get; private set; }
        public double FinalIndent { get; private set; }

        private readonly List<ParagraphInfo> _paragraphInfos;
        private readonly string _lineSpacing;
        private readonly Point3d _basePoint;
        private Point3d _currentPoint;

        // ▼▼▼【性能优化】为Jig实例缓存分词结果，避免在拖动中反复计算 ▼▼▼
        private Dictionary<int, List<string>> _tokenCache = new Dictionary<int, List<string>>();

        public SmartLayoutJig(List<ParagraphInfo> paragraphInfos, Point3d basePoint, string lineSpacing)
            {
            _paragraphInfos = paragraphInfos;
            _basePoint = basePoint;
            _currentPoint = basePoint;
            _lineSpacing = lineSpacing;
            FinalLineInfo = new List<Tuple<string, bool, bool, int, Point3d>>();

            // 在Jig初始化时，就对所有段落进行一次分词，并缓存结果
            var tokenizer = new Regex(@"[\u4e00-\u9fa5]|([a-zA-Z0-9.-]+)|\s+|[^\s\u4e00-\u9fa5a-zA-Z0-9.-]", RegexOptions.Compiled);
            for (int i = 0; i < _paragraphInfos.Count; i++)
                {
                var text = _paragraphInfos[i].Text;
                if (!string.IsNullOrEmpty(text))
                    {
                    var tokens = tokenizer.Matches(text).Cast<Match>().Select(m => m.Value).ToList();
                    _tokenCache[i] = tokens;
                    }
                }
            }

        protected override SamplerStatus Sampler(JigPrompts prompts)
            {
            var options = new JigPromptPointOptions("\n请拖动鼠标确定排版宽度，然后点击确认:")
                {
                BasePoint = _basePoint,
                UseBasePoint = true,
                Cursor = CursorType.Crosshair
                };
            var result = prompts.AcquirePoint(options);
            if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;

            if (_currentPoint.IsEqualTo(result.Value, new Tolerance(1e-4, 1e-4)))
                {
                return SamplerStatus.NoChange;
                }
            _currentPoint = result.Value;
            return SamplerStatus.OK;
            }

        protected override bool WorldDraw(WorldDraw draw)
            {
            double currentWidth = Math.Abs(_currentPoint.X - _basePoint.X);
            if (currentWidth < 1.0) currentWidth = 1.0;

            if (FinalIndent <= 0 && _paragraphInfos.Any())
                {
                using (var tempDbText = new DBText { TextString = "WW", Height = _paragraphInfos[0].Height, TextStyleId = _paragraphInfos[0].TextStyleId, WidthFactor = _paragraphInfos[0].WidthFactor })
                    {
                    var extents = tempDbText.GeometricExtents;
                    if (extents != null && !extents.MinPoint.IsEqualTo(extents.MaxPoint))
                        FinalIndent = extents.MaxPoint.X - extents.MinPoint.X;
                    }
                }

            FinalLineInfo.Clear();
            Point3d currentDrawingPosition = _basePoint;
            bool useCustomSpacing = double.TryParse(_lineSpacing, out double customSpacingValue);

            // ▼▼▼【核心修改】在这里新增一个变量，用于跟踪上一个段落的GroupKey ▼▼▼
            string previousGroupKey = null;

            for (int p_idx = 0; p_idx < _paragraphInfos.Count; p_idx++)
                {
                var paraInfo = _paragraphInfos[p_idx];
                if (string.IsNullOrEmpty(paraInfo.Text)) continue;

                var tokens = _tokenCache.ContainsKey(p_idx) ? _tokenCache[p_idx] : new List<string>();
                var linesInParagraph = GetWrappedLines(tokens, currentWidth, paraInfo.Height, paraInfo.WidthFactor, paraInfo.TextStyleId);

                // --- 【核心修改】在这里判断当前段落是否是上一个段落的延续 ---
                // 关键逻辑：如果当前段落的 GroupKey 不为空，并且和上一个段落的 GroupKey 相同，那么它就是续行。
                bool isContinuation = !string.IsNullOrEmpty(paraInfo.GroupKey) && paraInfo.GroupKey == previousGroupKey;


                for (int i = 0; i < linesInParagraph.Count; i++)
                    {
                    string lineText = linesInParagraph[i];

                    // --- 【核心修正】在这里定义一个绝对正确的“是否为逻辑第一行”的标志位 ---
                    // 关键逻辑：只有当一个段落不是续行（isContinuation为false），且我们正在处理它的第一行（i == 0）时，
                    // 它才被认为是“逻辑第一行”。
                    bool isFirstLogicalLine = (i == 0 && !isContinuation);

                    // --- 【核心修正】用这个新的标志位来决定是否缩进 ---
                    // 只有“逻辑第一行”才不缩进（x偏移为0），其他所有行（包括自动换行和续行）都要缩进。
                    double xOffset = isFirstLogicalLine ? 0 : FinalIndent;

                    Point3d textPosition = currentDrawingPosition + new Vector3d(xOffset, 0, 0);

                    using (var previewText = new DBText())
                        {
                        previewText.TextString = lineText;
                        previewText.Height = paraInfo.Height;
                        previewText.WidthFactor = paraInfo.WidthFactor;
                        previewText.TextStyleId = paraInfo.TextStyleId;
                        previewText.Position = textPosition;
                        previewText.HorizontalMode = TextHorizontalMode.TextLeft;
                        previewText.VerticalMode = TextVerticalMode.TextTop;
                        previewText.AlignmentPoint = textPosition;
                        draw.Geometry.Draw(previewText);
                        }

                    // --- 【核心修正】将正确的标志位存入Tuple中 ---
                    bool applyIndent = !isFirstLogicalLine; // applyIndent现在和缩进逻辑完全一致
                    FinalLineInfo.Add(new Tuple<string, bool, bool, int, Point3d>(lineText, applyIndent, isFirstLogicalLine, p_idx, textPosition));

                    double lineHeight = useCustomSpacing ? paraInfo.Height + customSpacingValue : paraInfo.Height * 1.5;
                    currentDrawingPosition += new Vector3d(0, -lineHeight, 0);
                    }

                // ▼▼▼【核心修改】在处理完一个段落的所有行后，更新 previousGroupKey ▼▼▼
                previousGroupKey = paraInfo.GroupKey;
                }
            return true;
            }

        // ▼▼▼【最终重构】这个方法现在接收“词块”列表，而不是原始文本 ▼▼▼
        public static List<string> GetWrappedLines(List<string> tokens, double maxWidth, double textHeight, double widthFactor, ObjectId textStyleId)
            {
            var lines = new List<string>();
            if (tokens == null || !tokens.Any()) return lines;

            using (var tempText = new DBText { Height = textHeight, WidthFactor = widthFactor, TextStyleId = textStyleId })
                {
                int currentTokenIndex = 0;
                while (currentTokenIndex < tokens.Count)
                    {
                    int low = 1;
                    int high = tokens.Count - currentTokenIndex;
                    int fitCount = 0;

                    while (low <= high)
                        {
                        int mid = low + (high - low) / 2;
                        string testLine = string.Concat(tokens.Skip(currentTokenIndex).Take(mid)).Trim();

                        if (string.IsNullOrEmpty(testLine))
                            {
                            fitCount = mid;
                            low = mid + 1;
                            continue;
                            }

                        tempText.TextString = testLine;
                        var extents = tempText.GeometricExtents;

                        if (extents != null && !extents.MinPoint.IsEqualTo(extents.MaxPoint) && (extents.MaxPoint.X - extents.MinPoint.X) <= maxWidth)
                            {
                            fitCount = mid;
                            low = mid + 1;
                            }
                        else
                            {
                            high = mid - 1;
                            }
                        }

                    if (fitCount == 0 && currentTokenIndex < tokens.Count)
                        {
                        fitCount = 1;
                        }

                    string finalLine = string.Concat(tokens.Skip(currentTokenIndex).Take(fitCount)).Trim();
                    lines.Add(finalLine);
                    currentTokenIndex += fitCount;
                    }
                }

            for (int i = 1; i < lines.Count; i++)
                {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                lines[i] = lines[i].TrimStart();
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                char firstChar = lines[i][0];
                if (ForbiddenStartChars.Contains(firstChar) && lines[i - 1].Length > 0)
                    {
                    lines[i - 1] += firstChar;
                    lines[i] = lines[i].Length > 1 ? lines[i].Substring(1).TrimStart() : "";
                    }
                }

            return lines;
            }
        }
    }