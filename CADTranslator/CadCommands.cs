using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using System.Text;

namespace CADTranslator
{
    public class CadCommands
    {
        private static TranslatorWindow translatorWindow;

        public class ParagraphInfo
        {
            public string Text { get; set; }
            public Entity TemplateEntity { get; set; }
            public TextHorizontalMode HorizontalMode { get; set; }
            public TextVerticalMode VerticalMode { get; set; }
            public double Height { get; set; }
            public double WidthFactor { get; set; }
            public ObjectId TextStyleId { get; set; }
            // ▼▼▼ 核心修改：用一个BlockId来管理所有关联的图形 ▼▼▼
            public ObjectId AssociatedGraphicsBlockId { get; set; } = ObjectId.Null;
            // ▲▲▲ 修改结束 ▲▲▲
            public Point3d OriginalAnchorPoint { get; set; }
            public bool ContainsSpecialPattern { get; set; } = false;
        }

        [CommandMethod("GJX")]
        public void LaunchToolbox()
        {
            if (translatorWindow == null || !translatorWindow.IsLoaded)
            {
                translatorWindow = new TranslatorWindow();
                translatorWindow.Show();
            }
            else
            {
                translatorWindow.Activate();
                if (!translatorWindow.IsVisible)
                {
                    translatorWindow.Show();
                }
            }
        }

        public static List<TextBlockItem> ExtractAndMergeText(Document doc, SelectionSet selSet)
        {
            var extractedEntities = new List<TextEntityInfo>();
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var textIds = new List<ObjectId>();
                foreach (SelectedObject selObj in selSet)
                {
                    if (selObj.ObjectId.ObjectClass.DxfName.EndsWith("TEXT"))
                    {
                        textIds.Add(selObj.ObjectId);
                    }
                }

                foreach (ObjectId id in textIds)
                {
                    try
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string text = "";
                        Point3d position = new Point3d();
                        double height = 1.0;

                        if (ent is DBText dbText)
                        {
                            text = dbText.TextString;
                            position = dbText.Position;
                            height = dbText.Height;
                        }
                        else if (ent is MText mText)
                        {
                            text = mText.Text;
                            position = mText.Location;
                            height = mText.TextHeight;
                        }

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            extractedEntities.Add(new TextEntityInfo
                            {
                                ObjectId = ent.ObjectId,
                                Text = text.Trim(),
                                Position = position,
                                Height = height
                            });
                        }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        doc.Editor.WriteMessage($"\n警告：跳过一个无法处理的文字实体。错误信息: {ex.Message}");
                    }
                }
                tr.Commit();
            }

            var sortedEntities = extractedEntities.OrderBy(e => -e.Position.Y).ThenBy(e => e.Position.X).ToList();
            var textBlocks = new List<TextBlockItem>();
            if (sortedEntities.Count == 0) return textBlocks;

            var currentBlock = new TextBlockItem { Id = 1, OriginalText = sortedEntities[0].Text, SourceObjectIds = new List<ObjectId> { sortedEntities[0].ObjectId } };
            textBlocks.Add(currentBlock);

            for (int i = 1; i < sortedEntities.Count; i++)
            {
                var previousEntity = sortedEntities[i - 1];
                var currentEntity = sortedEntities[i];

                double verticalDist = previousEntity.Position.Y - currentEntity.Position.Y;
                bool isTooFar = verticalDist > previousEntity.Height * 3.5;

                var paragraphMarkers = new Regex(@"^\s*(?:\d+[、\.]|\(\d+\)\.?)");
                bool isNewParagraph = paragraphMarkers.IsMatch(currentEntity.Text);

                if (isNewParagraph || isTooFar)
                {
                    currentBlock = new TextBlockItem { Id = textBlocks.Count + 1, OriginalText = currentEntity.Text, SourceObjectIds = new List<ObjectId> { currentEntity.ObjectId } };
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

        public class TextEntityInfo
        {
            public ObjectId ObjectId { get; set; }
            public string Text { get; set; }
            public Point3d Position { get; set; }
            public double Height { get; set; }
        }

        [CommandMethod("JDX")]
        public void DrawBreakLineCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            var ppr1 = editor.GetPoint("\n请指定截断线起点:");
            if (ppr1.Status != PromptStatus.OK) return;
            Point3d startPoint = ppr1.Value;

            var jig = new BreakLineJig(startPoint);
            var result = editor.Drag(jig);

            if (result.Status == PromptStatus.OK)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var finalPolyline = jig.Polyline;
                    if (finalPolyline != null)
                    {
                        modelSpace.AppendEntity(finalPolyline);
                        tr.AddNewlyCreatedDBObject(finalPolyline, true);
                    }
                    tr.Commit();
                }
            }
        }


        [CommandMethod("WZPB")]
        public void TextLayoutCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            try
            {
                var selRes = editor.GetSelection();
                if (selRes.Status != PromptStatus.OK) return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var graphicEntities = new List<Entity>();
                    var textSelection = new List<SelectedObject>();
                    foreach (SelectedObject selObj in selRes.Value)
                    {
                        var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent is DBText || ent is MText)
                        {
                            textSelection.Add(selObj);
                        }
                        else if (ent != null)
                        {
                            graphicEntities.Add(ent);
                        }
                    }

                    if (textSelection.Count == 0)
                    {
                        editor.WriteMessage("\n选择的对象中未找到任何有效文字。");
                        return;
                    }

                    List<TextBlockItem> rawTextBlocks = ExtractAndMergeText(doc, SelectionSet.FromObjectIds(textSelection.Select(s => s.ObjectId).ToArray()));

                    var paragraphInfos = new List<ParagraphInfo>();
                    string specialPattern = @"\s{6,}";

                    foreach (var block in rawTextBlocks)
                    {
                        Entity template = null;
                        var hMode = TextHorizontalMode.TextLeft;
                        var vMode = TextVerticalMode.TextBase;
                        double height = 2.5;
                        double widthFactor = 1.0;
                        var textStyleId = db.Textstyle;

                        var firstId = block.SourceObjectIds.FirstOrDefault(id => !id.IsNull && !id.IsErased);
                        var paraInfo = new ParagraphInfo();
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
                                paraInfo.OriginalAnchorPoint = (dbText.HorizontalMode == TextHorizontalMode.TextLeft && dbText.VerticalMode == TextVerticalMode.TextBase) ? dbText.Position : dbText.AlignmentPoint;
                            }
                            else if (ent is MText mText)
                            {
                                hMode = TextHorizontalMode.TextLeft;
                                vMode = TextVerticalMode.TextTop;
                                height = mText.TextHeight;
                                textStyleId = mText.TextStyleId;
                                paraInfo.OriginalAnchorPoint = mText.Location;
                            }
                        }
                        paraInfo.Text = block.OriginalText;
                        paraInfo.TemplateEntity = template;
                        paraInfo.HorizontalMode = hMode;
                        paraInfo.VerticalMode = vMode;
                        paraInfo.Height = height > 0 ? height : 2.5;
                        paraInfo.WidthFactor = widthFactor;
                        paraInfo.TextStyleId = textStyleId;

                        // ▼▼▼ 核心修改：将关联的图形“成组为块” ▼▼▼
                        if (Regex.IsMatch(block.OriginalText, specialPattern) && template != null && template.Bounds.HasValue)
                        {
                            paraInfo.ContainsSpecialPattern = true;
                            var textBounds = template.Bounds.Value;

                            var graphicsToGroup = new List<Entity>();
                            foreach (var graphic in graphicEntities)
                            {
                                if (graphic.Bounds.HasValue)
                                {
                                    var graphicBounds = graphic.Bounds.Value;
                                    bool isContained = textBounds.MinPoint.X <= graphicBounds.MinPoint.X &&
                                                       textBounds.MinPoint.Y <= graphicBounds.MinPoint.Y+2000 &&
                                                       textBounds.MaxPoint.X >= graphicBounds.MaxPoint.X &&
                                                       textBounds.MaxPoint.Y >= graphicBounds.MaxPoint.Y-2000;

                                    if (isContained)
                                    {
                                        graphicsToGroup.Add(graphic.Clone() as Entity);
                                    }
                                }
                            }

                            if (graphicsToGroup.Any())
                            {
                                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                                // 创建一个匿名的块定义
                                BlockTableRecord btr = new BlockTableRecord();
                                btr.Name = "TEMP_GRAPHIC_GROUP_" + Guid.NewGuid().ToString("N");
                                btr.Origin = paraInfo.OriginalAnchorPoint; // 将块的基点设为文字的锚点

                                foreach (var ent in graphicsToGroup)
                                {
                                    btr.AppendEntity(ent);
                                }
                                paraInfo.AssociatedGraphicsBlockId = bt.Add(btr);
                                tr.AddNewlyCreatedDBObject(btr, true);
                            }
                        }
                        // ▲▲▲ 修改结束 ▲▲▲
                        paragraphInfos.Add(paraInfo);
                    }

                    var ppr = editor.GetPoint($"\n请为整个段落集合指定左上角基点:");
                    if (ppr.Status != PromptStatus.OK) { tr.Abort(); return; }
                    Point3d basePoint = ppr.Value;

                    var jig = new SmartLayoutJig(paragraphInfos, basePoint);
                    var dragResult = editor.Drag(jig);
                    if (dragResult.Status != PromptStatus.OK) { tr.Abort(); return; }

                    foreach (SelectedObject selObj in selRes.Value)
                    {
                        if (!selObj.ObjectId.IsErased)
                        {
                            using (var entToErase = tr.GetObject(selObj.ObjectId, OpenMode.ForWrite)) { entToErase.Erase(); }
                        }
                    }

                    var unifiedVerticalMode = paragraphInfos.FirstOrDefault()?.VerticalMode ?? TextVerticalMode.TextBase;

                    for (int i = 0; i < jig.FinalLineInfo.Count; i++)
                    {
                        var lineInfo = jig.FinalLineInfo[i];
                        string lineText = lineInfo.Item1;
                        bool paraNeedsIndent = lineInfo.Item2;
                        bool isFirstLineOfPara = lineInfo.Item3;
                        int currentParaIndex = lineInfo.Item4;

                        var currentParaInfo = paragraphInfos[currentParaIndex];

                        using (var newText = new DBText())
                        {
                            if (currentParaInfo.TemplateEntity != null) newText.SetPropertiesFrom(currentParaInfo.TemplateEntity);
                            newText.TextString = lineText;
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

                            // ▼▼▼ 核心修改：插入并炸开我们之前创建好的块 ▼▼▼
                            if (isFirstLineOfPara && currentParaInfo.ContainsSpecialPattern && !currentParaInfo.AssociatedGraphicsBlockId.IsNull)
                            {
                                Point3d newAnchorPoint = (newText.HorizontalMode != TextHorizontalMode.TextLeft || newText.VerticalMode != TextVerticalMode.TextBase) ? newText.AlignmentPoint : newText.Position;
                                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                                using (var blockRef = new BlockReference(newAnchorPoint, currentParaInfo.AssociatedGraphicsBlockId))
                                {
                                    modelSpace.AppendEntity(blockRef);
                                    tr.AddNewlyCreatedDBObject(blockRef, true);

                                    // 关键一步：就地炸开块引用，将图形还原
                                    DBObjectCollection explodedObjects = new DBObjectCollection();
                                    blockRef.Explode(explodedObjects);
                                    foreach (DBObject obj in explodedObjects)
                                    {
                                        Entity explodedEntity = obj as Entity;
                                        if (explodedEntity != null)
                                        {
                                            modelSpace.AppendEntity(explodedEntity);
                                            tr.AddNewlyCreatedDBObject(explodedEntity, true);
                                        }
                                    }
                                    // 炸开后删除块引用本身
                                    blockRef.Erase();
                                }
                            }
                            // ▲▲▲ 新增结束 ▲▲▲

                            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                            ms.AppendEntity(newText);
                            tr.AddNewlyCreatedDBObject(newText, true);
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[WZPB] 命令出错: {ex.Message}");
            }
        }
    }
}