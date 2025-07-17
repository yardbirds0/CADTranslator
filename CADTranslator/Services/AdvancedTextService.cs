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
        public const string LegendPlaceholder = "__LEGEND_POS__"; 
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

                    // 4. 处理段落、图例，并执行“占位符注入”
                    string specialPattern = @"\s{3,}";

                    foreach (var block in rawTextBlocks)
                        {
                        var paraInfo = new ParagraphInfo();
                        paraInfo.OriginalText = block.OriginalText; // 始终保存一份未经修改的原文

                        var firstId = block.SourceObjectIds.FirstOrDefault(id => !id.IsNull && !id.IsErased);
                        if (firstId.IsNull) continue;

                        var templateEntity = tr.GetObject(firstId, OpenMode.ForRead) as Entity;
                        if (templateEntity == null) continue;

                        // 完整捕获原始文本的几何状态
                        if (templateEntity is DBText dbText)
                            {
                            paraInfo.Height = dbText.Height;
                            paraInfo.WidthFactor = dbText.WidthFactor;
                            paraInfo.TextStyleId = dbText.TextStyleId;
                            paraInfo.Position = dbText.Position;
                            paraInfo.AlignmentPoint = dbText.AlignmentPoint;
                            paraInfo.HorizontalMode = dbText.HorizontalMode;
                            paraInfo.VerticalMode = dbText.VerticalMode;
                            }
                        else if (templateEntity is MText mText)
                            {
                            paraInfo.Height = mText.TextHeight;
                            paraInfo.WidthFactor = 1.0;
                            paraInfo.TextStyleId = mText.TextStyleId;
                            paraInfo.Position = mText.Location;
                            paraInfo.AlignmentPoint = mText.Location;
                            paraInfo.HorizontalMode = TextHorizontalMode.TextLeft;
                            paraInfo.VerticalMode = TextVerticalMode.TextTop;
                            }

                        var match = Regex.Match(block.OriginalText, specialPattern);
                        if (match.Success)
                            {
                            // a. 记录原始空格数量
                            paraInfo.OriginalSpaceCount = match.Length;

                            // b. 计算“旧”锚点
                            string textBeforeSpaces = block.OriginalText.Substring(0, match.Index);
                            paraInfo.OriginalAnchorPoint = GetTextEndPoint(textBeforeSpaces, paraInfo, tr);

                            // c. 关联图形并创建块
                            var (associatedGraphics, _) = FindAssociatedGraphics(block.SourceObjectIds, graphicEntities, tr);
                            if (associatedGraphics.Any())
                                {
                                paraInfo.AssociatedGraphicsBlockId = CreateAnonymousBlockForGraphics(associatedGraphics, paraInfo.OriginalAnchorPoint, tr, bt);
                                }

                            // d.【核心】执行“占位符注入”，生成用于翻译的文本
                            paraInfo.Text = new Regex(specialPattern).Replace(block.OriginalText, LegendPlaceholder, 1);
                            paraInfo.ContainsSpecialPattern = true;
                            }
                        else
                            {
                            // 如果没有图例，Text属性就等于未经修改的原文
                            paraInfo.Text = block.OriginalText;
                            paraInfo.ContainsSpecialPattern = false;
                            }

                        paraInfo.SourceObjectIds.AddRange(block.SourceObjectIds);
                        paragraphInfos.Add(paraInfo);
                        }

                    tr.Commit();
                    // ▲▲▲ 请替换到这里结束 ▲▲▲
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
            Point3d endPoint = Point3d.Origin; // 初始化一个默认值

            // 创建一个临时DBText对象来精确模拟原始状态
            using (var tempText = new DBText())
                {
                // 1. 设置文本内容和基本样式
                tempText.TextString = text;
                tempText.Height = paraInfo.Height;
                tempText.WidthFactor = paraInfo.WidthFactor;
                tempText.TextStyleId = paraInfo.TextStyleId;

                // 2.【核心】应用原始的对齐属性
                tempText.HorizontalMode = paraInfo.HorizontalMode;
                tempText.VerticalMode = paraInfo.VerticalMode;

                // 3.【核心】根据不同的对齐方式，设置Position或AlignmentPoint
                if (tempText.HorizontalMode == TextHorizontalMode.TextLeft && tempText.VerticalMode == TextVerticalMode.TextBase)
                    {
                    // 对于默认的左下角对齐，我们设置Position
                    tempText.Position = paraInfo.Position;
                    }
                else
                    {
                    // 对于所有其他对齐方式，我们必须设置AlignmentPoint
                    tempText.AlignmentPoint = paraInfo.AlignmentPoint;
                    }

                // 4.【关键步骤】将临时文本添加到数据库并调整对齐，以获取正确的几何边界
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                modelSpace.AppendEntity(tempText);
                tr.AddNewlyCreatedDBObject(tempText, true);

                // 让CAD根据对齐属性，自动计算其准确的几何位置
                tempText.AdjustAlignment(_db);

                // 5. 获取计算后的精确边界
                if (tempText.Bounds.HasValue)
                    {
                    endPoint = tempText.Bounds.Value.MaxPoint;
                    }

                // 6. 任务完成，从数据库中擦除这个临时对象
                tempText.Erase();
                }

            return endPoint;
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

            // 正则：匹配段落开头的编号
            var paragraphMarkers = new Regex(@"^\s*(?:\d+[、\.]|\(\d+\)\.?)");
            // 【新增】正则：匹配段落结尾的标点
            var endOfParagraphMarkers = new Regex(@"[：:。]\s*$");

            for (int i = 1; i < sortedEntities.Count; i++)
                {
                var prev = sortedEntities[i - 1];
                var curr = sortedEntities[i];

                bool isTooFar = (prev.Position.Y - curr.Position.Y) > (prev.Height * 3.5);
                bool startsNewParagraph = paragraphMarkers.IsMatch(curr.Text);
                // 【新增】判断上一段是否以结束符结尾
                bool prevBlockEnds = endOfParagraphMarkers.IsMatch(currentBlock.OriginalText);

                // 【核心修改】在判断条件中加入新规则
                if (startsNewParagraph || isTooFar || prevBlockEnds)
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