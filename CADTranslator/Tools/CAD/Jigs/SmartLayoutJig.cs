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
        public double Rotation => _rotation;
        private readonly List<ParagraphInfo> _paragraphInfos;
        private readonly string _lineSpacing;
        private readonly Point3d _basePoint;
        private Point3d _currentPoint;
        private readonly double _rotation;
        private readonly Matrix3d _ucsToWcs;
        private readonly Matrix3d _wcsToUcs;
        // ▼▼▼【性能优化】为Jig实例缓存分词结果，避免在拖动中反复计算 ▼▼▼
        private Dictionary<int, List<string>> _tokenCache = new Dictionary<int, List<string>>();

        public SmartLayoutJig(List<ParagraphInfo> paragraphInfos, Point3d basePoint, string lineSpacing, double rotation)
            {
            _paragraphInfos = paragraphInfos;
            _basePoint = basePoint;
            _currentPoint = basePoint;
            _lineSpacing = lineSpacing;
            _rotation = rotation; // 记忆旋转角度
            FinalLineInfo = new List<Tuple<string, bool, bool, int, Point3d>>();

            // 创建坐标转换矩阵
            _wcsToUcs = Matrix3d.Rotation(-_rotation, Vector3d.ZAxis, _basePoint);
            _ucsToWcs = Matrix3d.Rotation(_rotation, Vector3d.ZAxis, _basePoint);

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

            // 【核心修改】在比较和赋值之前，先把鼠标当前点从WCS转换到我们的旋转UCS
            Point3d transformedPoint = result.Value.TransformBy(_wcsToUcs);
            Point3d transformedCurrentPoint = _currentPoint.TransformBy(_wcsToUcs);

            if (transformedCurrentPoint.IsEqualTo(transformedPoint, new Tolerance(1e-4, 1e-4)))
                {
                return SamplerStatus.NoChange;
                }

            // 记录的依然是WCS的点，但在下一次比较前会再次转换
            _currentPoint = result.Value;
            return SamplerStatus.OK;
            }

        protected override bool WorldDraw(WorldDraw draw)
            {
            // 步骤 1: 在旋转后的坐标系(UCS)中计算真实的排版宽度
            Point3d transformedPoint = _currentPoint.TransformBy(_wcsToUcs);
            Point3d transformedBasePoint = _basePoint.TransformBy(_wcsToUcs);
            double currentWidth = Math.Abs(transformedPoint.X - transformedBasePoint.X);

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
            Point3d currentLineStartWcsPosition = _basePoint; // 这是第一行文字在世界坐标系(WCS)中的起始点
            bool useCustomSpacing = double.TryParse(_lineSpacing, out double customSpacingValue);

            // 步骤 2: 创建一个纯粹的旋转矩阵，用于变换方向向量
            Matrix3d rotationMatrix = Matrix3d.Rotation(_rotation, Vector3d.ZAxis, Point3d.Origin);

            string previousGroupKey = null;

            for (int p_idx = 0; p_idx < _paragraphInfos.Count; p_idx++)
                {
                var paraInfo = _paragraphInfos[p_idx];
                if (string.IsNullOrEmpty(paraInfo.Text)) continue;

                var tokens = _tokenCache.ContainsKey(p_idx) ? _tokenCache[p_idx] : new List<string>();

                // 【核心修正】根据是否为续行，为换行计算器准备不同的宽度参数
                bool isContinuation = !string.IsNullOrEmpty(paraInfo.GroupKey) && paraInfo.GroupKey == previousGroupKey;

                // 如果是续行，那么它的第一行就需要缩进，所以使用较小的宽度
                double firstLineWidth = isContinuation ? currentWidth - FinalIndent : currentWidth;

                // 段落内由于自动换行产生的后续行，总是需要缩进的
                double subsequentLineWidth = currentWidth - FinalIndent;

                var linesInParagraph = GetWrappedLines(tokens, firstLineWidth, subsequentLineWidth, paraInfo.Height, paraInfo.WidthFactor, paraInfo.TextStyleId);


                for (int i = 0; i < linesInParagraph.Count; i++)
                    {
                    string lineText = linesInParagraph[i];
                    bool isFirstLogicalLine = (i == 0 && !isContinuation);
                    double xOffset = isFirstLogicalLine ? 0 : FinalIndent;

                    // 步骤 3: 【核心修正】在旋转坐标系中创建缩进向量，然后将其变换到世界坐标系
                    Vector3d ucsIndentVector = new Vector3d(xOffset, 0, 0);
                    Vector3d wcsIndentVector = ucsIndentVector.TransformBy(rotationMatrix);

                    Point3d textWcsPosition = currentLineStartWcsPosition + wcsIndentVector;

                    using (var previewText = new DBText())
                        {
                        previewText.TextString = lineText;
                        previewText.Height = paraInfo.Height;
                        previewText.WidthFactor = paraInfo.WidthFactor;
                        previewText.TextStyleId = paraInfo.TextStyleId;
                        previewText.Position = textWcsPosition;
                        previewText.HorizontalMode = TextHorizontalMode.TextLeft;
                        previewText.VerticalMode = TextVerticalMode.TextTop;
                        previewText.AlignmentPoint = textWcsPosition;
                        previewText.Rotation = _rotation; // 应用旋转角度
                        draw.Geometry.Draw(previewText);
                        }

                    bool applyIndent = !isFirstLogicalLine;
                    FinalLineInfo.Add(new Tuple<string, bool, bool, int, Point3d>(lineText, applyIndent, isFirstLogicalLine, p_idx, textWcsPosition));

                    // 步骤 4: 【核心修正】在旋转坐标系中创建换行向量，然后将其变换到世界坐标系
                    double lineHeight = useCustomSpacing ? paraInfo.Height + customSpacingValue : paraInfo.Height * 1.5;
                    Vector3d ucsLineFeedVector = new Vector3d(0, -lineHeight, 0);
                    Vector3d wcsLineFeedVector = ucsLineFeedVector.TransformBy(rotationMatrix);

                    // 计算下一行文字的起始位置
                    currentLineStartWcsPosition += wcsLineFeedVector;
                    }
                previousGroupKey = paraInfo.GroupKey;
                }
            return true;
            }

        // ▼▼▼【最终重构】这个方法现在接收“词块”列表，而不是原始文本 ▼▼▼
        public static List<string> GetWrappedLines(List<string> tokens, double firstLineWidth, double subsequentLineWidth, double textHeight, double widthFactor, ObjectId textStyleId)
            {
            var lines = new List<string>();
            if (tokens == null || !tokens.Any()) return lines;

            using (var tempText = new DBText { Height = textHeight, WidthFactor = widthFactor, TextStyleId = textStyleId })
                {
                int currentTokenIndex = 0;
                bool isFirstLineOfParagraph = true; // 此标志用于决定该使用哪个宽度

                while (currentTokenIndex < tokens.Count)
                    {
                    // 【核心修正】根据是否为段落首行，选择正确的最大宽度
                    double maxWidth = isFirstLineOfParagraph ? firstLineWidth : subsequentLineWidth;
                    if (maxWidth < 1.0) maxWidth = 1.0; // 确保宽度有效

                    int low = 1;
                    int high = tokens.Count - currentTokenIndex;
                    int fitCount = 0;

                    // (二分法查找最适字符数的逻辑不变)
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

                    isFirstLineOfParagraph = false; // 处理完第一行后，后续所有行都使用“后续行宽度”
                    }
                }

            // (行首禁则处理逻辑不变)
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