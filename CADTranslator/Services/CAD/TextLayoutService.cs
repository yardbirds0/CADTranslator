// 文件路径: CADTranslator/Services/CAD/TextLayoutService.cs
// 【注意】这是一个完整的文件替换 (最终修正版)

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Tools.CAD.Jigs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CADTranslator.Services.CAD
    {
    public class TextLayoutService
        {
        private readonly Document _doc;
        private readonly Database _db;
        private readonly Editor _editor;

        public TextLayoutService(Document doc)
            {
            _doc = doc;
            _db = doc.Database;
            _editor = doc.Editor;
            }

        #region --- 1. 核心执行逻辑 (Execute) ---

        public void Execute(SelectionSet selSet, string lineSpacing)
            {
            if (selSet == null || selSet.Count == 0) return;

            var settingsService = new SettingsService();
            var settings = settingsService.LoadSettings();
            double similarityThreshold = settings.ParagraphSimilarityThreshold;

            using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    // 步骤 1: 使用移植过来的新方法，分类实体
                    var (textEntities, graphicEntities) = ClassifyEntities(selSet, tr);
                    if (textEntities.Count == 0)
                        {
                        _editor.WriteMessage("\n选择的对象中未找到任何有效文字。");
                        return;
                        }

                    // 步骤 2: 使用移植过来的新方法，智能合并文本
                    // 【已修正】这里的数据源现在是 List<TextBlock>
                    List<TextBlock> rawTextBlocks = MergeRawText(textEntities, similarityThreshold);

                    // ====================================================================
                    // 【重要】从这里开始，是您原有的、完全未经修改的图例和Jig处理逻辑
                    // 我只是将数据源从 `List<TextBlockViewModel>` 对接到了 `List<TextBlock>`
                    // ====================================================================

                    var paragraphInfos = new List<ParagraphInfo>();
                    string specialPattern = @"\s{3,}";
                    string placeholder = "*图例位置*";
                    var deletableObjectIds = new HashSet<ObjectId>();

                    foreach (var block in rawTextBlocks)
                        {
                        Entity template = null;
                        var hMode = TextHorizontalMode.TextLeft;
                        var vMode = TextVerticalMode.TextBase;
                        double height = 2.5;
                        double widthFactor = 1.0;
                        var textStyleId = _db.Textstyle;

                        var firstId = block.SourceObjectIds.FirstOrDefault(id => !id.IsNull && !id.IsErased);
                        var paraInfo = new ParagraphInfo();
                        paraInfo.GroupKey = block.GroupKey;

                        var paragraphBounds = new Extents3d(Point3d.Origin, Point3d.Origin);
                        double maxLineHeight = 0;
                        foreach (var textId in block.SourceObjectIds)
                            {
                            deletableObjectIds.Add(textId); // 将所有原始文本加入待删除列表
                            var textEnt = tr.GetObject(textId, OpenMode.ForRead) as Entity;
                            if (textEnt != null && textEnt.Bounds.HasValue)
                                {
                                if (paragraphBounds.MinPoint == Point3d.Origin && paragraphBounds.MaxPoint == Point3d.Origin)
                                    {
                                    paragraphBounds = textEnt.Bounds.Value;
                                    }
                                else
                                    {
                                    paragraphBounds.AddExtents(textEnt.Bounds.Value);
                                    }

                                if (textEnt is DBText dbt) maxLineHeight = Math.Max(maxLineHeight, dbt.Height);
                                else if (textEnt is MText mt) maxLineHeight = Math.Max(maxLineHeight, mt.TextHeight);
                                }
                            }

                        if (firstId != null)
                            {
                            var ent = tr.GetObject(firstId, OpenMode.ForRead);
                            template = ent as Entity;
                            if (ent is DBText dbText)
                                {
                                hMode = dbText.HorizontalMode;
                                vMode = dbText.VerticalMode;
                                height = dbText.Height;
                                widthFactor = dbText.WidthFactor;
                                textStyleId = dbText.TextStyleId;
                                }
                            else if (ent is MText mText)
                                {
                                hMode = TextHorizontalMode.TextLeft;
                                vMode = TextVerticalMode.TextTop;
                                height = mText.TextHeight;
                                textStyleId = mText.TextStyleId;
                                }
                            }

                        paraInfo.TemplateEntity = template; // 确保TemplateEntity被赋值
                        paraInfo.HorizontalMode = hMode;
                        paraInfo.VerticalMode = vMode;
                        paraInfo.Height = height > 0 ? height : 2.5;
                        paraInfo.WidthFactor = widthFactor;
                        paraInfo.TextStyleId = textStyleId;

                        var match = Regex.Match(block.OriginalText, specialPattern);
                        if (match.Success)
                            {
                            paraInfo.ContainsSpecialPattern = true;
                            paraInfo.OriginalSpaceCount = match.Length;

                            string textBeforePlaceholder = block.OriginalText.Substring(0, match.Index);
                            using (var tempText = new DBText { TextString = textBeforePlaceholder, Height = paraInfo.Height, WidthFactor = paraInfo.WidthFactor, TextStyleId = paraInfo.TextStyleId })
                                {
                                // 【已修正】修复了 modelSpace 的作用域问题
                                var modelSpaceForTemp = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                                modelSpaceForTemp.AppendEntity(tempText);
                                tr.AddNewlyCreatedDBObject(tempText, true);

                                if (template is DBText originalDbText)
                                    {
                                    tempText.HorizontalMode = originalDbText.HorizontalMode;
                                    tempText.VerticalMode = originalDbText.VerticalMode;
                                    if (tempText.HorizontalMode != TextHorizontalMode.TextLeft || tempText.VerticalMode != TextVerticalMode.TextBase)
                                        {
                                        tempText.AlignmentPoint = originalDbText.AlignmentPoint;
                                        tempText.AdjustAlignment(_db);
                                        }
                                    else
                                        {
                                        tempText.Position = originalDbText.Position;
                                        }
                                    }

                                if (tempText.Bounds.HasValue)
                                    {
                                    paraInfo.OriginalAnchorPoint = tempText.Bounds.Value.MaxPoint;
                                    }
                                tempText.Erase();
                                }

                            paraInfo.Text = block.OriginalText.Substring(0, match.Index) + placeholder + block.OriginalText.Substring(match.Index + match.Length);

                            // 【已修正】这部分是您原有的图形查找和分组逻辑，现在它会正常工作
                            var graphicsToGroup = new List<Entity>();
                            foreach (var graphic in graphicEntities)
                                {
                                if (graphic.Bounds.HasValue)
                                    {
                                    var graphicBounds = graphic.Bounds.Value;
                                    bool isContained = paragraphBounds.MinPoint.X <= graphicBounds.MinPoint.X &&
                                    (paragraphBounds.MinPoint.Y - maxLineHeight) <= graphicBounds.MinPoint.Y &&
                                    paragraphBounds.MaxPoint.X >= graphicBounds.MaxPoint.X &&
                                    (paragraphBounds.MaxPoint.Y + maxLineHeight) >= graphicBounds.MaxPoint.Y;

                                    if (isContained)
                                        {
                                        graphicsToGroup.Add(graphic);
                                        deletableObjectIds.Add(graphic.ObjectId); // 将关联的图形也加入待删除列表
                                        }
                                    }
                                }

                            if (graphicsToGroup.Any())
                                {
                                BlockTable bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForWrite);
                                BlockTableRecord btr = new BlockTableRecord
                                    {
                                    Name = "TEMP_GRAPHIC_GROUP_" + Guid.NewGuid().ToString("N"),
                                    Origin = paraInfo.OriginalAnchorPoint
                                    };

                                foreach (var ent in graphicsToGroup)
                                    {
                                    var clonedEnt = ent.Clone() as Entity;
                                    btr.AppendEntity(clonedEnt);
                                    }
                                paraInfo.AssociatedGraphicsBlockId = bt.Add(btr);
                                tr.AddNewlyCreatedDBObject(btr, true);
                                }
                            }
                        else
                            {
                            paraInfo.Text = block.OriginalText;
                            }

                        paragraphInfos.Add(paraInfo);
                        }

                    var ppr = _editor.GetPoint($"\n请为整个段落集合指定左上角基点:");
                    if (ppr.Status != PromptStatus.OK) { tr.Abort(); return; }
                    Point3d basePoint = ppr.Value;

                    var jig = new SmartLayoutJig(paragraphInfos, basePoint, lineSpacing);
                    var dragResult = _editor.Drag(jig);
                    if (dragResult.Status != PromptStatus.OK) { tr.Abort(); return; }

                    foreach (var id in deletableObjectIds)
                        {
                        if (!id.IsErased)
                            {
                            using (var entToErase = tr.GetObject(id, OpenMode.ForWrite)) { entToErase.Erase(); }
                            }
                        }

                    var unifiedVerticalMode = paragraphInfos.FirstOrDefault()?.VerticalMode ?? TextVerticalMode.TextBase;
                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                    for (int i = 0; i < jig.FinalLineInfo.Count; i++)
                        {
                        var lineInfo = jig.FinalLineInfo[i];
                        string lineText = lineInfo.Item1;
                        bool paraNeedsIndent = lineInfo.Item2;
                        bool isFirstLineOfPara = lineInfo.Item3;
                        int currentParaIndex = lineInfo.Item4;
                        var currentParaInfo = paragraphInfos[currentParaIndex];

                        if (currentParaInfo.ContainsSpecialPattern && lineText.Contains(placeholder))
                            {
                            int placeholderIndex = lineText.IndexOf(placeholder);
                            string textBeforePlaceholderInLine = lineText.Substring(0, placeholderIndex);

                            using (var tempText = new DBText { TextString = textBeforePlaceholderInLine, Height = currentParaInfo.Height, WidthFactor = currentParaInfo.WidthFactor, TextStyleId = currentParaInfo.TextStyleId })
                                {
                                double yOffset = i * currentParaInfo.Height * 1.5;
                                double xOffset = (paraNeedsIndent && !isFirstLineOfPara) ? jig.FinalIndent : 0;
                                Point3d linePos = basePoint + new Vector3d(xOffset, -yOffset, 0);

                                tempText.HorizontalMode = currentParaInfo.HorizontalMode;
                                tempText.VerticalMode = unifiedVerticalMode;
                                if (tempText.HorizontalMode != TextHorizontalMode.TextLeft || tempText.VerticalMode != TextVerticalMode.TextBase)
                                    {
                                    tempText.AlignmentPoint = linePos;
                                    }
                                else
                                    tempText.Position = linePos;
                                tempText.AdjustAlignment(_db);

                                // 【已修正】修复了 modelSpace 的作用域问题
                                var modelSpaceForJig = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                                modelSpaceForJig.AppendEntity(tempText);
                                tr.AddNewlyCreatedDBObject(tempText, true);

                                if (tempText.Bounds.HasValue)
                                    {
                                    Point3d newAnchorPoint = tempText.Bounds.Value.MaxPoint;
                                    Vector3d displacement = newAnchorPoint - currentParaInfo.OriginalAnchorPoint;
                                    Matrix3d transformMatrix = Matrix3d.Displacement(displacement);

                                    using (var blockRef = new BlockReference(currentParaInfo.OriginalAnchorPoint, currentParaInfo.AssociatedGraphicsBlockId))
                                        {
                                        blockRef.TransformBy(transformMatrix);
                                        modelSpaceForJig.AppendEntity(blockRef);
                                        tr.AddNewlyCreatedDBObject(blockRef, true);

                                        DBObjectCollection explodedObjects = new DBObjectCollection();
                                        blockRef.Explode(explodedObjects);
                                        foreach (DBObject obj in explodedObjects)
                                            {
                                            Entity explodedEntity = obj as Entity;
                                            if (explodedEntity != null)
                                                {
                                                modelSpaceForJig.AppendEntity(explodedEntity);
                                                tr.AddNewlyCreatedDBObject(explodedEntity, true);
                                                }
                                            }
                                        blockRef.Erase();
                                        }
                                    }
                                tempText.Erase();
                                }
                            lineText = lineText.Replace(placeholder, new string(' ', currentParaInfo.OriginalSpaceCount));
                            }

                        using (var newText = new DBText())
                            {
                            if (currentParaInfo.TemplateEntity != null) newText.SetPropertiesFrom(currentParaInfo.TemplateEntity);
                            newText.TextString = lineText; ;
                            newText.Height = currentParaInfo.Height;
                            newText.WidthFactor = currentParaInfo.WidthFactor;
                            newText.TextStyleId = currentParaInfo.TextStyleId;

                            double xOffset = 0;
                            if (paraNeedsIndent && !isFirstLineOfPara)
                                {
                                xOffset = jig.FinalIndent;
                                }

                            double yOffset = i * currentParaInfo.Height * 1.5;
                            Point3d linePosition = basePoint + new Vector3d(xOffset, -yOffset, 0);

                            newText.HorizontalMode = currentParaInfo.HorizontalMode;
                            newText.VerticalMode = unifiedVerticalMode;

                            if (newText.HorizontalMode != TextHorizontalMode.TextLeft || newText.VerticalMode != TextVerticalMode.TextBase)
                                {
                                newText.AlignmentPoint = linePosition;
                                }
                            else
                                {
                                newText.Position = linePosition;
                                }

                            modelSpace.AppendEntity(newText);
                            tr.AddNewlyCreatedDBObject(newText, true);
                            }
                        }
                    tr.Commit();
                    }
                catch (System.Exception ex)
                    {
                    _editor.WriteMessage($"\n[WZPB] 命令在服务层执行时发生错误: {ex.Message}");
                    tr.Abort();
                    }
                }
            }
        #endregion

        #region --- 2. 移植过来的高级文本处理及辅助方法 ---

        // 【已修正】FindAssociatedGraphics 方法现在在这里，解决了“不存在”的错误
        private (List<Entity> associatedGraphics, Extents3d paragraphBounds, double maxLineHeight) FindAssociatedGraphics(List<ObjectId> textObjectIds, List<Entity> allGraphics, Transaction tr)
            {
            var pBounds = new Extents3d(Point3d.Origin, Point3d.Origin);
            double maxLineHeight = 0;
            foreach (var textId in textObjectIds)
                {
                var textEnt = tr.GetObject(textId, OpenMode.ForRead) as Entity;
                if (textEnt?.Bounds == null) continue;
                if (pBounds.MinPoint == Point3d.Origin) pBounds = textEnt.Bounds.Value;
                else pBounds.AddExtents(textEnt.Bounds.Value);
                if (textEnt is DBText dbt) maxLineHeight = Math.Max(maxLineHeight, dbt.Height);
                else if (textEnt is MText mt) maxLineHeight = Math.Max(maxLineHeight, mt.TextHeight);
                }

            var associatedGraphics = allGraphics.Where(g =>
                g?.Bounds != null &&
                pBounds.MinPoint.X <= g.Bounds.Value.MaxPoint.X &&
                pBounds.MaxPoint.X >= g.Bounds.Value.MinPoint.X &&
                (pBounds.MinPoint.Y - maxLineHeight) <= g.Bounds.Value.MaxPoint.Y &&
                (pBounds.MaxPoint.Y + maxLineHeight) >= g.Bounds.Value.MinPoint.Y
            ).ToList();

            return (associatedGraphics, pBounds, maxLineHeight);
            }

        private (List<TextEntityInfo> textEntities, List<Entity> graphicEntities) ClassifyEntities(SelectionSet selSet, Transaction tr)
            {
            var textEntities = new List<TextEntityInfo>();
            var graphicEntities = new List<Entity>();
            foreach (SelectedObject selObj in selSet)
                {
                var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (ent is DBText dbText)
                    {
                    textEntities.Add(new TextEntityInfo { ObjectId = ent.ObjectId, Text = dbText.TextString, Position = dbText.Position, Height = dbText.Height });
                    }
                else if (ent is MText mText)
                    {
                    textEntities.Add(new TextEntityInfo { ObjectId = ent.ObjectId, Text = mText.Text, Position = mText.Location, Height = mText.TextHeight });
                    }
                else
                    {
                    graphicEntities.Add(ent);
                    }
                }
            return (textEntities, graphicEntities);
            }

        private List<TextBlock> MergeRawText(List<TextEntityInfo> textEntities, double similarityThreshold)
            {
            var sortedEntities = textEntities.OrderBy(e => -e.Position.Y).ThenBy(e => e.Position.X).ToList();
            var textBlocks = new List<TextBlock>();
            if (sortedEntities.Count == 0) return textBlocks;

            var titleMarkers = new Regex(@"(说明|注意|技术要求|参数|示例|NOTES|SPECIFICATION|LEGEND)[\s:：]*$|:$", RegexOptions.IgnoreCase);
            var paragraphMarkers = new Regex(@"^\s*(?:[<(（【〔](?:\d+|[a-zA-Z])[>)）】〕]|\d+[、.]|[\u25A0\u25CF\u25B2\u25B6]|[a-zA-Z][、.])");

            var firstEntity = sortedEntities.First();
            var firstBlock = new TextBlock { Id = 1, OriginalText = firstEntity.Text.Trim(), SourceObjectIds = { firstEntity.ObjectId } };
            if (titleMarkers.IsMatch(firstBlock.OriginalText))
                {
                firstBlock.IsTitle = true;
                }
            textBlocks.Add(firstBlock);

            string currentGroupKey = null;

            for (int i = 1; i < sortedEntities.Count; i++)
                {
                var prevEntity = sortedEntities[i - 1];
                var currEntity = sortedEntities[i];
                string currentText = currEntity.Text.Trim();
                if (string.IsNullOrEmpty(currentText)) continue;

                var lastBlock = textBlocks.Last();

                bool isTooFar = (prevEntity.Position.Y - currEntity.Position.Y) > (prevEntity.Height * 2.0);
                if (isTooFar)
                    {
                    textBlocks.Add(new TextBlock { Id = textBlocks.Count + 1, OriginalText = currentText, SourceObjectIds = { currEntity.ObjectId } });
                    currentGroupKey = null;
                    continue;
                    }

                bool startsNewParagraph = paragraphMarkers.IsMatch(currentText);
                if (startsNewParagraph)
                    {
                    textBlocks.Add(new TextBlock { Id = textBlocks.Count + 1, OriginalText = currentText, SourceObjectIds = { currEntity.ObjectId } });
                    currentGroupKey = null;
                    continue;
                    }

                double levenshteinSimilarity = CalculateSimilarity(lastBlock.OriginalText, currentText);
                double structuralSimilarity = CalculateStructuralSimilarity(lastBlock.OriginalText, currentText);

                if (levenshteinSimilarity > similarityThreshold || structuralSimilarity > similarityThreshold)
                    {
                    if (currentGroupKey == null)
                        {
                        currentGroupKey = Guid.NewGuid().ToString();
                        lastBlock.GroupKey = currentGroupKey;
                        }
                    var newGroupedBlock = new TextBlock { Id = textBlocks.Count + 1, OriginalText = currentText, SourceObjectIds = { currEntity.ObjectId }, GroupKey = currentGroupKey };
                    textBlocks.Add(newGroupedBlock);
                    }
                else
                    {
                    currentGroupKey = null;
                    bool onSameLine = Math.Abs(prevEntity.Position.Y - currEntity.Position.Y) < (prevEntity.Height * 0.5);
                    string separator = onSameLine ? " " : "";
                    lastBlock.OriginalText += separator + currentText;
                    lastBlock.SourceObjectIds.Add(currEntity.ObjectId);
                    }
                }
            return textBlocks;
            }

        private static double CalculateSimilarity(string s1, string s2)
            {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            var maxLength = Math.Max(s1.Length, s2.Length);
            if (maxLength == 0) return 1.0;
            var d = new int[s1.Length + 1, s2.Length + 1];
            for (int i = 0; i <= s1.Length; i++) { d[i, 0] = i; }
            for (int j = 0; j <= s2.Length; j++) { d[0, j] = j; }
            for (int i = 1; i <= s1.Length; i++)
                {
                for (int j = 1; j <= s2.Length; j++)
                    {
                    int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                    }
                }
            return 1.0 - ((double)d[s1.Length, s2.Length] / maxLength);
            }

        private double CalculateStructuralSimilarity(string s1, string s2)
            {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;
            var tokenizer = new Regex(@"[a-zA-Z0-9\.-]+|%%[a-zA-Z0-9@]+|[^\s]");
            var tokens1 = tokenizer.Matches(s1).Cast<Match>().Select(m => m.Value).ToList();
            var tokens2 = tokenizer.Matches(s2).Cast<Match>().Select(m => m.Value).ToList();
            if (tokens1.Count == 0 || tokens2.Count == 0) return 0.0;
            int[,] dp = new int[tokens1.Count + 1, tokens2.Count + 1];
            for (int i = 1; i <= tokens1.Count; i++)
                {
                for (int j = 1; j <= tokens2.Count; j++)
                    {
                    if (tokens1[i - 1] == tokens2[j - 1])
                        {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                        }
                    else
                        {
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                        }
                    }
                }
            int lcsLength = dp[tokens1.Count, tokens2.Count];
            return (2.0 * lcsLength) / (tokens1.Count + tokens2.Count);
            }

        #endregion
        }
    }