using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CADTranslator.Services
    {
    /// <summary>
    /// 提供高级文本分析功能的服务。
    /// 负责从CAD选择集中提取、合并文本，并关联对应的图例图形。
    /// 这是代码重用的核心。
    /// </summary>
    public class AdvancedTextService
        {
        private readonly Database _db;

        public AdvancedTextService(Document doc)
            {
            _db = doc.Database;
            }

        /// <summary>
        /// 从选择集中提取并处理文本块，返回包含段落和图例信息的列表。
        /// </summary>
        /// <param name="selSet">用户的选择集</param>
        /// <returns>处理后的段落信息列表</returns>
        public List<ParagraphInfo> ExtractAndProcessParagraphs(SelectionSet selSet)
            {
            using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    // 1. 分类选择的实体
                    var (textEntities, graphicEntities) = ClassifyEntities(selSet, tr);

                    if (textEntities.Count == 0)
                        {
                        return new List<ParagraphInfo>(); // 如果没有文字，直接返回空列表
                        }

                    // 2. 合并原始文本实体为初步的文本块
                    List<TextBlock> rawTextBlocks = MergeRawText(textEntities);

                    // 3. 将文本块处理成包含图例信息的最终段落
                    var paragraphInfos = ProcessBlocksIntoParagraphs(rawTextBlocks, graphicEntities, tr);

                    tr.Commit();
                    return paragraphInfos;
                    }
                catch (System.Exception ex)
                    {
                    // 在服务层，我们通常记录或向上抛出异常
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n在AdvancedTextService中处理文本时出错: {ex.Message}");
                    tr.Abort();
                    return new List<ParagraphInfo>();
                    }
                }
            }

        // 内部方法1：将选择集中的实体分类为文字和图形
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

        // 内部方法2：将零散的文字实体智能合并成文本块
        private List<TextBlock> MergeRawText(List<TextEntityInfo> textEntities)
            {
            var sortedEntities = textEntities.OrderBy(e => -e.Position.Y).ThenBy(e => e.Position.X).ToList();
            var textBlocks = new List<TextBlock>();
            if (sortedEntities.Count == 0) return textBlocks;

            var currentBlock = new TextBlock { Id = 1, OriginalText = sortedEntities[0].Text, SourceObjectIds = new List<ObjectId> { sortedEntities[0].ObjectId } };
            textBlocks.Add(currentBlock);

            var paragraphMarkers = new Regex(@"^\s*(?:\d+[、\.]|\(\d+\)\.?)");

            for (int i = 1; i < sortedEntities.Count; i++)
                {
                var previousEntity = sortedEntities[i - 1];
                var currentEntity = sortedEntities[i];

                double verticalDist = previousEntity.Position.Y - currentEntity.Position.Y;
                bool isTooFar = verticalDist > previousEntity.Height * 3.5;
                bool isNewParagraph = paragraphMarkers.IsMatch(currentEntity.Text);

                if (isNewParagraph || isTooFar)
                    {
                    currentBlock = new TextBlock { Id = textBlocks.Count + 1, OriginalText = currentEntity.Text, SourceObjectIds = new List<ObjectId> { currentEntity.ObjectId } };
                    textBlocks.Add(currentBlock);
                    }
                else
                    {
                    currentBlock.OriginalText += " " + currentEntity.Text;
                    currentBlock.SourceObjectIds.Add(currentEntity.ObjectId);
                    }
                }
            return textBlocks;
            }

        // 内部方法3：处理文本块，识别特殊占位符并关联图形
        private List<ParagraphInfo> ProcessBlocksIntoParagraphs(List<TextBlock> rawTextBlocks, List<Entity> graphicEntities, Transaction tr)
            {
            var paragraphInfos = new List<ParagraphInfo>();
            string specialPattern = @"\s{3,}"; // 匹配3个或更多连续空格作为图例占位符
            string placeholder = "*图例位置*";

            foreach (var block in rawTextBlocks)
                {
                var paraInfo = new ParagraphInfo();
                var firstId = block.SourceObjectIds.FirstOrDefault(id => !id.IsNull && !id.IsErased);
                if (firstId.IsNull) continue;

                var templateEntity = tr.GetObject(firstId, OpenMode.ForRead) as Entity;
                paraInfo.TemplateEntity = templateEntity;
                if (templateEntity is DBText dbText)
                    {
                    paraInfo.HorizontalMode = dbText.HorizontalMode;
                    paraInfo.VerticalMode = dbText.VerticalMode;
                    paraInfo.Height = dbText.Height;
                    paraInfo.WidthFactor = dbText.WidthFactor;
                    paraInfo.TextStyleId = dbText.TextStyleId;
                    }
                else if (templateEntity is MText mText)
                    {
                    paraInfo.HorizontalMode = TextHorizontalMode.TextLeft;
                    paraInfo.VerticalMode = TextVerticalMode.TextTop;
                    paraInfo.Height = mText.TextHeight;
                    paraInfo.TextStyleId = mText.TextStyleId;
                    paraInfo.WidthFactor = 1.0;
                    }
                paraInfo.Height = (paraInfo.Height <= 0) ? 2.5 : paraInfo.Height;

                var match = Regex.Match(block.OriginalText, specialPattern);
                if (match.Success && graphicEntities.Any())
                    {
                    paraInfo.ContainsSpecialPattern = true;
                    paraInfo.OriginalSpaceCount = match.Length;

                    string textBeforePlaceholder = block.OriginalText.Substring(0, match.Index);

                    // 关键修正：调用GetTextEndPoint时，不再传递当前事务 tr
                    paraInfo.OriginalAnchorPoint = GetTextEndPoint(textBeforePlaceholder, paraInfo);

                    var (associatedGraphics, paragraphBounds) = FindAssociatedGraphics(block.SourceObjectIds, graphicEntities, tr);
                    if (associatedGraphics.Any())
                        {
                        paraInfo.AssociatedGraphicsBlockId = CreateAnonymousBlockForGraphics(associatedGraphics, paraInfo.OriginalAnchorPoint, tr);
                        }

                    paraInfo.Text = block.OriginalText.Substring(0, match.Index) + placeholder + block.OriginalText.Substring(match.Index + match.Length);
                    }
                else
                    {
                    paraInfo.Text = block.OriginalText;
                    }

                paraInfo.SourceObjectIds.AddRange(block.SourceObjectIds);
                paragraphInfos.Add(paraInfo);
                }

            return paragraphInfos;
            }

        // 辅助方法：计算一段文字的包围框末端点，用于定位图例
        private Point3d GetTextEndPoint(string text, ParagraphInfo paraInfo)
            {
            Point3d endPoint = Point3d.Origin;

            // 关键修正：为这个临时测量操作，开启一个全新的、独立的事务
            using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    using (var tempText = new DBText { TextString = text, Height = paraInfo.Height, WidthFactor = paraInfo.WidthFactor, TextStyleId = paraInfo.TextStyleId })
                        {
                        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                        modelSpace.AppendEntity(tempText);
                        tr.AddNewlyCreatedDBObject(tempText, true);

                        if (paraInfo.TemplateEntity is DBText originalDbText)
                            {
                            tempText.HorizontalMode = originalDbText.HorizontalMode;
                            tempText.VerticalMode = originalDbText.VerticalMode;
                            if (tempText.HorizontalMode != TextHorizontalMode.TextLeft || tempText.VerticalMode != TextVerticalMode.TextBase)
                                {
                                tempText.AlignmentPoint = originalDbText.Position; // 注意：这里使用Position作为对齐基准
                                tempText.AdjustAlignment(_db);
                                }
                            else
                                {
                                tempText.Position = originalDbText.Position;
                                }
                            }

                        endPoint = tempText.Bounds?.MaxPoint ?? Point3d.Origin;
                        tempText.Erase(); // 在同一个事务内删除
                        }
                    tr.Commit(); // 提交这个独立的事务
                    }
                catch (System.Exception ex)
                    {
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n在GetTextEndPoint中发生错误: {ex.Message}");
                    tr.Abort(); // 如果出错，则中止
                    }
                }
            return endPoint;
            }

        // 辅助方法：根据文本的边界范围，查找被其“包裹”的图形
        private (List<Entity> associatedGraphics, Extents3d paragraphBounds) FindAssociatedGraphics(List<ObjectId> textObjectIds, List<Entity> allGraphics, Transaction tr)
            {
            var paragraphBounds = new Extents3d(Point3d.Origin, Point3d.Origin);
            double maxLineHeight = 0;

            // 计算整个文本段落的联合包围框
            foreach (var textId in textObjectIds)
                {
                var textEnt = tr.GetObject(textId, OpenMode.ForRead) as Entity;
                if (textEnt?.Bounds == null) continue;

                if (paragraphBounds.MinPoint == Point3d.Origin)
                    paragraphBounds = textEnt.Bounds.Value;
                else
                    paragraphBounds.AddExtents(textEnt.Bounds.Value);

                if (textEnt is DBText dbt) maxLineHeight = Math.Max(maxLineHeight, dbt.Height);
                else if (textEnt is MText mt) maxLineHeight = Math.Max(maxLineHeight, mt.TextHeight);
                }

            var associatedGraphics = new List<Entity>();
            foreach (var graphic in allGraphics)
                {
                if (graphic?.Bounds == null) continue;

                var graphicBounds = graphic.Bounds.Value;
                // 判断图形是否在文本段落的边界内（Y方向上放宽一个行高）
                bool isContained = paragraphBounds.MinPoint.X <= graphicBounds.MinPoint.X &&
                                   (paragraphBounds.MinPoint.Y - maxLineHeight) <= graphicBounds.MinPoint.Y &&
                                   paragraphBounds.MaxPoint.X >= graphicBounds.MaxPoint.X &&
                                   (paragraphBounds.MaxPoint.Y + maxLineHeight) >= graphicBounds.MaxPoint.Y;

                if (isContained)
                    {
                    associatedGraphics.Add(graphic);
                    }
                }

            return (associatedGraphics, paragraphBounds);
            }

        // 辅助方法：将找到的图形打包成一个匿名的块定义
        private ObjectId CreateAnonymousBlockForGraphics(List<Entity> graphics, Point3d basePoint, Transaction tr)
            {
            BlockTable bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForWrite);

            BlockTableRecord btr = new BlockTableRecord
                {
                Name = "*U" + Guid.NewGuid().ToString("N"),
                Origin = basePoint
                };

            int successfullyClonedCount = 0;
            foreach (var ent in graphics)
                {
                try
                    {
                    var clonedEnt = ent.Clone() as Entity;
                    if (clonedEnt != null)
                        {
                        btr.AppendEntity(clonedEnt);
                        successfullyClonedCount++;
                        }
                    }
                catch (System.Exception ex)
                    {
                    // 关键修正：从当前活动的文档获取Editor对象来打印消息
                    var editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                    editor.WriteMessage($"\n[CADTranslator] 警告：一个ID为 {ent.ObjectId} 的图形实体无法被处理，已自动跳过。错误信息: {ex.Message}");
                    }
                }

            if (successfullyClonedCount == 0)
                {
                return ObjectId.Null;
                }

            ObjectId blockId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            return blockId;
            }
        }
    }