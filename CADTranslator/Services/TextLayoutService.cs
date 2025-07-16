using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models;
using CADTranslator.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using CADTranslator.AutoCAD.Jigs; // 确保引用了Jigs命名空间

namespace CADTranslator.Services
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

        public void Execute(SelectionSet selSet, string lineSpacing)
        {
            if (selSet == null || selSet.Count == 0) return;

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                try
                {
                    var graphicEntities = new List<Entity>();
                    var textObjectIds = new List<ObjectId>();
                    var deletableObjectIds = new HashSet<ObjectId>();

                    // 1. 直接使用从命令传入的 selSet
                    foreach (SelectedObject selObj in selSet)
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
                        _editor.WriteMessage("\n选择的对象中未找到任何有效文字。");
                        return;
                    }

                    var textService = new CadTextService(Application.DocumentManager.MdiActiveDocument);
                    // 2. 直接使用 textObjectIds 创建临时的 SelectionSet
                    List<TextBlockViewModel> rawTextBlocks = textService.ExtractAndMergeText(SelectionSet.FromObjectIds(textObjectIds.ToArray()));

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
                        var textStyleId = _db.Textstyle;

                        var firstId = block.SourceObjectIds.FirstOrDefault(id => !id.IsNull && !id.IsErased);
                        var paraInfo = new ParagraphInfo();

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

                        paraInfo.Text = block.OriginalText;
                        paraInfo.TemplateEntity = template;
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
                                var tempId = SymbolUtilityServices.GetBlockModelSpaceId(_db);
                                var btrTemp = (BlockTableRecord)tr.GetObject(tempId, OpenMode.ForWrite);
                                btrTemp.AppendEntity(tempText);
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
                                BlockTable bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForWrite);
                                BlockTableRecord btr = new BlockTableRecord();
                                btr.Name = "TEMP_GRAPHIC_GROUP_" + Guid.NewGuid().ToString("N");
                                btr.Origin = paraInfo.OriginalAnchorPoint;

                                foreach (var ent in graphicsToGroup)
                                    {
                                    var clonedEnt = ent.Clone() as Entity;
                                    btr.AppendEntity(clonedEnt);
                                    }
                                paraInfo.AssociatedGraphicsBlockId = bt.Add(btr);
                                tr.AddNewlyCreatedDBObject(btr, true);
                                }
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

                                var tempId = SymbolUtilityServices.GetBlockModelSpaceId(_db);
                                var btrTemp = (BlockTableRecord)tr.GetObject(tempId, OpenMode.ForWrite);
                                btrTemp.AppendEntity(tempText);
                                tr.AddNewlyCreatedDBObject(tempText, true);

                                if (tempText.Bounds.HasValue)
                                    {
                                    Point3d newAnchorPoint = tempText.Bounds.Value.MaxPoint;
                                    Vector3d displacement = newAnchorPoint - currentParaInfo.OriginalAnchorPoint;
                                    Matrix3d transformMatrix = Matrix3d.Displacement(displacement);

                                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
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

                            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                            ms.AppendEntity(newText);
                            tr.AddNewlyCreatedDBObject(newText, true);
                            }
                        }
                    tr.Commit();
                    }
                catch (System.Exception ex)
                    {
                    _editor.WriteMessage($"\n[WZPB] 命令在服务层执行时发生错误: {ex.Message}");
                    tr.Abort(); // 确保在服务层也中止事务
                    }
                }
                }
            }
        }
   
