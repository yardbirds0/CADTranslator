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
            public ObjectId AssociatedGraphicsBlockId { get; set; } = ObjectId.Null;
            public Point3d OriginalAnchorPoint { get; set; }
            public bool ContainsSpecialPattern { get; set; } = false;
            public int OriginalSpaceCount { get; set; } = 0;
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
                    var textObjectIds = new List<ObjectId>();
                    var deletableObjectIds = new HashSet<ObjectId>();

                    foreach (SelectedObject selObj in selRes.Value)
                    {
                        var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent is DBText || ent is MText)
                        {
                            textObjectIds.Add(ent.ObjectId);
                            deletableObjectIds.Add(ent.ObjectId);
                        }
                        else if (ent != null)
                        {
                            graphicEntities.Add(ent);
                        }
                    }

                    if (textObjectIds.Count == 0)
                    {
                        editor.WriteMessage("\n选择的对象中未找到任何有效文字。");
                        return;
                    }

                    List<TextBlockItem> rawTextBlocks = ExtractAndMergeText(doc, SelectionSet.FromObjectIds(textObjectIds.ToArray()));

                    var paragraphInfos = new List<ParagraphInfo>();
                    string specialPattern = @"\s{3,}";
                    string placeholder = "*图例位置*";

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

                        // ▼▼▼ 核心修改：统一计算段落的总边界和左上角锚点 ▼▼▼
                        var paragraphBounds = new Extents3d(Point3d.Origin, Point3d.Origin);
                        double maxLineHeight = 0;
                        foreach (var textId in block.SourceObjectIds)
                        {
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
                        // ▲▲▲ 修改结束 ▲▲▲

                        paraInfo.Text = block.OriginalText;
                        paraInfo.TemplateEntity = template;
                        paraInfo.HorizontalMode = hMode;
                        paraInfo.VerticalMode = vMode;
                        paraInfo.Height = height > 0 ? height : 2.5;
                        paraInfo.WidthFactor = widthFactor;
                        paraInfo.TextStyleId = textStyleId;

                        // ▼▼▼ 核心修改：追踪“信标”位置 ▼▼▼
                        var match = Regex.Match(block.OriginalText, specialPattern);
                        if (match.Success)
                        {
                            paraInfo.ContainsSpecialPattern = true;
                            paraInfo.OriginalSpaceCount = match.Length;

                            // 1. 获取原始信标位置
                            string textBeforePlaceholder = block.OriginalText.Substring(0, match.Index);
                            using (var tempText = new DBText { TextString = textBeforePlaceholder, Height = paraInfo.Height, WidthFactor = paraInfo.WidthFactor, TextStyleId = paraInfo.TextStyleId })
                            {
                                // 必须先将临时文字加入数据库才能获取准确的几何信息
                                var tempId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                                var btr = (BlockTableRecord)tr.GetObject(tempId, OpenMode.ForWrite);
                                btr.AppendEntity(tempText);
                                tr.AddNewlyCreatedDBObject(tempText, true);

                                if (template is DBText originalDbText)
                                {
                                    tempText.HorizontalMode = originalDbText.HorizontalMode;
                                    tempText.VerticalMode = originalDbText.VerticalMode;
                                    if (tempText.HorizontalMode != TextHorizontalMode.TextLeft || tempText.VerticalMode != TextVerticalMode.TextBase)
                                    {
                                        tempText.AlignmentPoint = originalDbText.AlignmentPoint;
                                        tempText.AdjustAlignment(db);
                                    }
                                    else
                                    {
                                        tempText.Position = originalDbText.Position;
                                    }
                                }

                                // 获取文字“之前”部分的边界，其右上角就是信标的开始位置
                                if (tempText.Bounds.HasValue)
                                {
                                    paraInfo.OriginalAnchorPoint = tempText.Bounds.Value.MaxPoint;
                                }
                                tempText.Erase(); // 用完后立即删除
                            }

                            // 2. 用信标替换空格
                            paraInfo.Text = block.OriginalText.Substring(0, match.Index) + placeholder + block.OriginalText.Substring(match.Index + match.Length);

                            // 3. 关联图形并“成块”
                            //var searchBounds = paragraphBounds;
                            //searchBounds.TransformBy(Matrix3d.Scaling(1.0, new Point3d(0, paragraphBounds.MinPoint.Y - maxLineHeight, 0)));
                            //searchBounds.TransformBy(Matrix3d.Scaling(1.0, new Point3d(0, paragraphBounds.MaxPoint.Y + maxLineHeight, 0)));

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
                                        deletableObjectIds.Add(graphic.ObjectId);
                                    }
                                }
                            }

                            if (graphicsToGroup.Any())
                            {
                                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                                BlockTableRecord btr = new BlockTableRecord();
                                btr.Name = "TEMP_GRAPHIC_GROUP_" + Guid.NewGuid().ToString("N");
                                btr.Origin = paraInfo.OriginalAnchorPoint;

                                foreach (var ent in graphicsToGroup)
                                {
                                    var clonedEnt = ent.Clone() as Entity;
                                    //clonedEnt.TransformBy(Matrix3d.Displacement(btr.Origin.GetVectorTo(Point3d.Origin)));
                                    btr.AppendEntity(clonedEnt);
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

                    foreach (var id in deletableObjectIds)
                    {
                        if (!id.IsErased)
                        {
                            using (var entToErase = tr.GetObject(id, OpenMode.ForWrite)) { entToErase.Erase(); }
                        }
                    }

                    var unifiedVerticalMode = paragraphInfos.FirstOrDefault()?.VerticalMode ?? TextVerticalMode.TextBase;

                    // ▼▼▼ 核心修改：在创建最终文字时，计算新信标位置并进行变换 ▼▼▼
                    for (int i = 0; i < jig.FinalLineInfo.Count; i++)
                    {
                        var lineInfo = jig.FinalLineInfo[i];
                        string lineText = lineInfo.Item1;
                        bool paraNeedsIndent = lineInfo.Item2;
                        bool isFirstLineOfPara = lineInfo.Item3;
                        int currentParaIndex = lineInfo.Item4;

                        var currentParaInfo = paragraphInfos[currentParaIndex];

                        // 如果是特殊段落，且当前行包含信标，则计算新锚点并移动块
                        if (currentParaInfo.ContainsSpecialPattern && lineText.Contains(placeholder))
                        {
                            // 1. 获取新锚点
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
                                tempText.AdjustAlignment(db);


                                // 同样需要临时加入数据库来获取准确的几何信息
                                var tempId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                                var btr = (BlockTableRecord)tr.GetObject(tempId, OpenMode.ForWrite);
                                btr.AppendEntity(tempText);
                                tr.AddNewlyCreatedDBObject(tempText, true);

                                if (tempText.Bounds.HasValue)
                                {
                                    Point3d newAnchorPoint = tempText.Bounds.Value.MaxPoint;
                                    Vector3d displacement = newAnchorPoint - currentParaInfo.OriginalAnchorPoint;
                                    Matrix3d transformMatrix = Matrix3d.Displacement(displacement);

                                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                                    using (var blockRef = new BlockReference(currentParaInfo.OriginalAnchorPoint, currentParaInfo.AssociatedGraphicsBlockId))
                                    {
                                        blockRef.TransformBy(transformMatrix);
                                        modelSpace.AppendEntity(blockRef);
                                        tr.AddNewlyCreatedDBObject(blockRef, true);

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
                                        blockRef.Erase();
                                    }
                                }
                                tempText.Erase();
                            }
                            // 2. 将信标替换回原始空格
                            lineText = lineText.Replace(placeholder, new string(' ', currentParaInfo.OriginalSpaceCount)); 
                        }


                        using (var newText = new DBText())
                        {
                            if (currentParaInfo.TemplateEntity != null) newText.SetPropertiesFrom(currentParaInfo.TemplateEntity);
                            newText.TextString = lineText;;
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

                            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                            ms.AppendEntity(newText);
                            tr.AddNewlyCreatedDBObject(newText, true);
                        }
                    }
                    // ▲▲▲ 修改结束 ▲▲▲
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