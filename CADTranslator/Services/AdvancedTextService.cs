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
    /// 高级文本分析服务。
    /// 此版本假定调用方已处理 DocumentLock，因此内部使用单个事务处理所有读写操作。
    /// </summary>
    public class AdvancedTextService
        {
        private readonly Database _db;
        private readonly Editor _editor;

        public AdvancedTextService(Document doc)
            {
            _db = doc.Database;
            _editor = doc.Editor;
            }

        public List<ParagraphInfo> ExtractAndProcessParagraphs(SelectionSet selSet, out List<ObjectId> allSourceIds)
            {
            var paragraphInfos = new List<ParagraphInfo>();
            allSourceIds = new List<ObjectId>();

            // 因为外部已经加锁，所以我们可以安全地开启一个包含读写的事务
            using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    foreach (SelectedObject selObj in selSet)
                        {
                        allSourceIds.Add(selObj.ObjectId);
                        }
                    // 1. 在事务开始时就用写模式打开块表，获得最高权限
                    BlockTable bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForWrite);

                    // 2. 分类实体
                    var (textEntities, graphicEntities) = ClassifyEntities(selSet, tr);
                    if (textEntities.Count == 0) return paragraphInfos;

                    // 3. 合并文本
                    List<TextBlock> rawTextBlocks = MergeRawText(textEntities);

                    // 4. 处理段落、图例
                    string specialPattern = @"\s{3,}";

                    foreach (var block in rawTextBlocks)
                        {
                        var paraInfo = new ParagraphInfo();
                        var firstId = block.SourceObjectIds.FirstOrDefault(id => !id.IsNull && !id.IsErased);
                        if (firstId.IsNull) continue;

                        var templateEntity = tr.GetObject(firstId, OpenMode.ForRead) as Entity;
                        if (templateEntity is DBText dbText)
                            {
                            paraInfo.Height = dbText.Height; paraInfo.WidthFactor = dbText.WidthFactor; paraInfo.TextStyleId = dbText.TextStyleId;
                            }
                        else if (templateEntity is MText mText)
                            {
                            paraInfo.Height = mText.TextHeight; paraInfo.WidthFactor = 1.0; paraInfo.TextStyleId = mText.TextStyleId;
                            }

                        var match = Regex.Match(block.OriginalText, specialPattern);
                        if (match.Success && graphicEntities.Any())
                            {
                            // A. 计算锚点
                            string textBeforePlaceholder = block.OriginalText.Substring(0, match.Index);
                            paraInfo.OriginalAnchorPoint = GetTextEndPoint(textBeforePlaceholder, paraInfo, tr);

                            // B. 关联图形并创建块
                            var (associatedGraphics, _) = FindAssociatedGraphics(block.SourceObjectIds, graphicEntities, tr);
                            if (associatedGraphics.Any())
                                {
                                paraInfo.AssociatedGraphicsBlockId = CreateAnonymousBlockForGraphics(associatedGraphics, paraInfo.OriginalAnchorPoint, tr, bt);
                                }

                            paraInfo.Text = block.OriginalText;
                            paraInfo.ContainsSpecialPattern = true; // 明确地标记这个段落含有图例
                            }
                        else
                            {
                            paraInfo.Text = block.OriginalText;
                            paraInfo.ContainsSpecialPattern = false;
                            }

                        paraInfo.SourceObjectIds.AddRange(block.SourceObjectIds);
                        paragraphInfos.Add(paraInfo);
                        }

                    tr.Commit();
                    }
                catch (System.Exception ex)
                    {
                    _editor.WriteMessage($"\n处理文本时出错: {ex.Message}\n{ex.StackTrace}");
                    tr.Abort();
                    }
                }
            return paragraphInfos;
            }

        // --- 辅助方法现在都接收事务作为参数 ---

        private Point3d GetTextEndPoint(string text, ParagraphInfo paraInfo, Transaction tr)
            {
            using (var tempText = new DBText { TextString = text, Height = paraInfo.Height, WidthFactor = paraInfo.WidthFactor, TextStyleId = paraInfo.TextStyleId })
                {
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                modelSpace.AppendEntity(tempText);
                tr.AddNewlyCreatedDBObject(tempText, true);
                Point3d endPoint = tempText.Bounds?.MaxPoint ?? Point3d.Origin;
                tempText.Erase();
                return endPoint;
                }
            }

        private ObjectId CreateAnonymousBlockForGraphics(List<Entity> graphics, Point3d basePoint, Transaction tr, BlockTable bt)
            {
            // 使用您在 TextLayoutService.cs 中已经验证成功的命名方式
            BlockTableRecord btr = new BlockTableRecord();
            btr.Name = "TEMP_GRAPHIC_GROUP_" + Guid.NewGuid().ToString("N");
            btr.Origin = basePoint;

            int count = 0;
            foreach (var ent in graphics)
                {
                try
                    {
                    var clone = ent.Clone() as Entity;
                    if (clone != null)
                        {
                        btr.AppendEntity(clone);
                        count++;
                        }
                    }
                catch (System.Exception ex)
                    {
                    _editor.WriteMessage($"\n警告: 无法克隆一个图形实体: {ex.Message}");
                    }
                }

            if (count == 0) return ObjectId.Null;

            ObjectId blockId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            return blockId;
            }

        // 其他不涉及写操作的辅助方法保持原样
        private (List<TextEntityInfo> textEntities, List<Entity> graphicEntities) ClassifyEntities(SelectionSet selSet, Transaction tr)
            {
            var textEntities = new List<TextEntityInfo>();
            var graphicEntities = new List<Entity>();
            foreach (SelectedObject selObj in selSet)
                {
                var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (ent is DBText dbText) textEntities.Add(new TextEntityInfo { ObjectId = ent.ObjectId, Text = dbText.TextString, Position = dbText.Position, Height = dbText.Height });
                else if (ent is MText mText) textEntities.Add(new TextEntityInfo { ObjectId = ent.ObjectId, Text = mText.Text, Position = mText.Location, Height = mText.TextHeight });
                else graphicEntities.Add(ent);
                }
            return (textEntities, graphicEntities);
            }

        private List<TextBlock> MergeRawText(List<TextEntityInfo> textEntities)
            {
            var sortedEntities = textEntities.OrderBy(e => -e.Position.Y).ThenBy(e => e.Position.X).ToList();
            var textBlocks = new List<TextBlock>();
            if (sortedEntities.Count == 0) return textBlocks;
            var currentBlock = new TextBlock { Id = 1, OriginalText = sortedEntities[0].Text, SourceObjectIds = { sortedEntities[0].ObjectId } };
            textBlocks.Add(currentBlock);
            var paragraphMarkers = new Regex(@"^\s*(?:\d+[、\.]|\(\d+\)\.?)");
            for (int i = 1; i < sortedEntities.Count; i++)
                {
                var prev = sortedEntities[i - 1];
                var curr = sortedEntities[i];
                bool isTooFar = (prev.Position.Y - curr.Position.Y) > (prev.Height * 3.5);
                if (paragraphMarkers.IsMatch(curr.Text) || isTooFar)
                    {
                    currentBlock = new TextBlock { Id = textBlocks.Count + 1, OriginalText = curr.Text, SourceObjectIds = { curr.ObjectId } };
                    textBlocks.Add(currentBlock);
                    }
                else
                    {
                    currentBlock.OriginalText += " " + curr.Text;
                    currentBlock.SourceObjectIds.Add(curr.ObjectId);
                    }
                }
            return textBlocks;
            }

        private (List<Entity> associatedGraphics, Extents3d paragraphBounds) FindAssociatedGraphics(List<ObjectId> textObjectIds, List<Entity> allGraphics, Transaction tr)
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
            var associatedGraphics = allGraphics.Where(g => g?.Bounds != null && pBounds.MinPoint.X <= g.Bounds.Value.MinPoint.X && (pBounds.MinPoint.Y - maxLineHeight) <= g.Bounds.Value.MinPoint.Y && pBounds.MaxPoint.X >= g.Bounds.Value.MaxPoint.X && (pBounds.MaxPoint.Y + maxLineHeight) >= g.Bounds.Value.MaxPoint.Y).ToList();
            return (associatedGraphics, pBounds);
            }
        }
    }