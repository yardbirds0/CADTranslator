using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CADTranslator.Models;

namespace CADTranslator.AutoCAD.Jigs
    {
    public class SmartLayoutJig : DrawJig
    {
        public List<Tuple<string, bool, bool, int, Point3d>> FinalLineInfo { get; private set; }
        public double FinalIndent { get; private set; }

        private readonly List<ParagraphInfo> _paragraphInfos;
        private readonly string _lineSpacing;
        private readonly Point3d _basePoint;
        private Point3d _currentPoint;

        public SmartLayoutJig(List<ParagraphInfo> paragraphInfos, Point3d basePoint, string lineSpacing)
            {
            _paragraphInfos = paragraphInfos;
            _basePoint = basePoint;
            _currentPoint = basePoint;
            _lineSpacing = lineSpacing;
            FinalLineInfo = new List<Tuple<string, bool, bool, int, Point3d>>();
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
                    if (extents != null) FinalIndent = extents.MaxPoint.X - extents.MinPoint.X;
                    }
                }

            FinalLineInfo.Clear();
            Point3d currentDrawingPosition = _basePoint;

            bool useCustomSpacing = double.TryParse(_lineSpacing, out double customSpacingValue);

            for (int p_idx = 0; p_idx < _paragraphInfos.Count; p_idx++)
                {
                var paraInfo = _paragraphInfos[p_idx];
                if (string.IsNullOrEmpty(paraInfo.Text)) continue;

                var linesInParagraph = GetWrappedLines(paraInfo.Text, currentWidth, paraInfo.Height, paraInfo.WidthFactor, paraInfo.TextStyleId);
                bool applyIndent = linesInParagraph.Count > 1;

                for (int i = 0; i < linesInParagraph.Count; i++)
                    {
                    string lineText = linesInParagraph[i];
                    double xOffset = (i > 0 && applyIndent) ? FinalIndent : 0;

                    // 1. 在 using 外部声明和定义 textPosition
                    Point3d textPosition = currentDrawingPosition + new Vector3d(xOffset, 0, 0);

                    using (var previewText = new DBText())
                        {
                        previewText.TextString = lineText;
                        previewText.Height = paraInfo.Height;
                        previewText.WidthFactor = paraInfo.WidthFactor;
                        previewText.TextStyleId = paraInfo.TextStyleId;

                        // 2. 在这里直接使用 textPosition
                        previewText.Position = textPosition;
                        previewText.HorizontalMode = TextHorizontalMode.TextLeft;
                        previewText.VerticalMode = TextVerticalMode.TextTop;
                        previewText.AlignmentPoint = textPosition;

                        draw.Geometry.Draw(previewText);
                        }

                    FinalLineInfo.Add(new Tuple<string, bool, bool, int, Point3d>(lineText, applyIndent, i == 0, p_idx, textPosition));

                    // -- 从这里开始是新的行距计算逻辑 --
                    if (useCustomSpacing)
                        {
                        // 使用自定义行距：Y坐标减去 当前行文字高度 和 用户指定的间距
                        currentDrawingPosition += new Vector3d(0, -(paraInfo.Height + customSpacingValue), 0);
                        }
                    else
                        {
                        // 使用默认行距
                        currentDrawingPosition += new Vector3d(0, -paraInfo.Height * 1.5, 0);
                        }
                    // -- 新逻辑结束 --
                    }
                }
            return true;
            }

        public static List<string> GetWrappedLines(string text, double maxWidth, double textHeight, double widthFactor, ObjectId textStyleId)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            using (var tempText = new DBText { Height = textHeight, WidthFactor = widthFactor, TextStyleId = textStyleId })
            {
                int lineStart = 0;
                while (lineStart < text.Length)
                {
                    // 1. 二分法高效查找最大容纳字符数
                    int low = 1;
                    int high = text.Length - lineStart;
                    int fitCount = 0;

                    while (low <= high)
                    {
                        int mid = low + (high - low) / 2;
                        tempText.TextString = text.Substring(lineStart, mid);
                        var extents = tempText.GeometricExtents;

                        if (extents != null && (extents.MaxPoint.X - extents.MinPoint.X) <= maxWidth)
                        {
                            fitCount = mid;
                            low = mid + 1;
                        }
                        else
                        {
                            high = mid - 1;
                        }
                    }

                    if (fitCount == 0 && (text.Length - lineStart) > 0)
                    {
                        fitCount = 1;
                    }

                    int breakPos = lineStart + fitCount;

                    // 2. 智能回溯，仅在需要时（即在英文单词或数字序列中间）触发
                    if (breakPos < text.Length)
                    {
                        // 判断断点是否在一个连续的ASCII字母或数字序列的中间
                        bool isMidWord = IsAsciiLetterOrDigit(text[breakPos]) && IsAsciiLetterOrDigit(text[breakPos - 1]);

                        if (isMidWord)
                        {
                            // 如果是，才回溯寻找上一个非字母或数字的字符（通常是空格或标点）
                            int lastWordBreak = -1;
                            for (int i = breakPos - 1; i > lineStart; i--)
                            {
                                if (!IsAsciiLetterOrDigit(text[i]))
                                {
                                    lastWordBreak = i + 1;
                                    break;
                                }
                            }

                            // 如果找到了更早的断点，就在那里断开
                            if (lastWordBreak > lineStart)
                            {
                                breakPos = lastWordBreak;
                            }
                            // 如果没找到（一个超长的英文单词），就只能在fitCount处硬换行
                        }
                    }

                    // 3. Unicode代理项对安全检查
                    if (breakPos > lineStart && breakPos < text.Length)
                    {
                        if (char.IsHighSurrogate(text[breakPos - 1]) && char.IsLowSurrogate(text[breakPos]))
                        {
                            breakPos--;
                        }
                    }

                    lines.Add(text.Substring(lineStart, breakPos - lineStart));
                    lineStart = breakPos;
                }
            }
            return lines;
        }

        // 辅助方法，判断一个字符是否是ASCII字母或数字
        private static bool IsAsciiLetterOrDigit(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }
        // ▲▲▲ 修改结束 ▲▲▲
    }
}