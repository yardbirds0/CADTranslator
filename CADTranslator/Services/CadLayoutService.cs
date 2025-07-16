// 文件路径: CADTranslator/Services/CadLayoutService.cs

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.AutoCAD.Jigs;
using CADTranslator.Models;
using CADTranslator.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CADTranslator.Services
    {
    public class CadLayoutService
        {
        // 最好是引用一个共享的常量，但为了简单，我们在这里也定义它
        private const string LegendPlaceholder = "__LEGEND_POS__";

        private readonly Document _doc;
        private readonly Database _db;
        private readonly Editor _editor;

        public CadLayoutService(Document doc)
            {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc), "Document cannot be null.");

            _doc = doc;
            _db = doc.Database;
            _editor = doc.Editor;
            }

        public bool ApplySmartLayoutToCad(ObservableCollection<TextBlockViewModel> textBlockList, List<ObjectId> idsToDelete)
            {
            var paragraphInfos = new List<ParagraphInfo>();

            // 1. 准备Jig所需的数据。我们现在完全信任从ViewModel传来的数据。
            using (var tr = _db.TransactionManager.StartTransaction())
                {
                foreach (var block in textBlockList)
                    {
                    if (string.IsNullOrWhiteSpace(block.TranslatedText) || block.TranslatedText.StartsWith("[")) continue;

                    var firstId = block.SourceObjectIds.FirstOrDefault(id => !id.IsNull && !id.IsErased);
                    if (firstId.IsNull) continue;

                    var templateEntity = tr.GetObject(firstId, OpenMode.ForRead) as Entity;
                    if (templateEntity == null) continue;

                    var pInfo = new ParagraphInfo
                        {
                        Text = block.TranslatedText,
                        ContainsSpecialPattern = block.TranslatedText.Contains(LegendPlaceholder),
                        TemplateEntity = templateEntity,
                        AssociatedGraphicsBlockId = block.AssociatedGraphicsBlockId,
                        OriginalAnchorPoint = block.OriginalAnchorPoint,
                        OriginalSpaceCount = block.OriginalSpaceCount,
                        Position = block.Position,
                        AlignmentPoint = block.AlignmentPoint,
                        HorizontalMode = block.HorizontalMode,
                        VerticalMode = block.VerticalMode,
                        SourceObjectIds = block.SourceObjectIds.ToList()
                        };

                    if (templateEntity is DBText dbText)
                        {
                        pInfo.Height = dbText.Height;
                        pInfo.WidthFactor = dbText.WidthFactor;
                        pInfo.TextStyleId = dbText.TextStyleId;
                        }
                    else if (templateEntity is MText mText)
                        {
                        pInfo.Height = mText.TextHeight;
                        pInfo.WidthFactor = 1.0;
                        pInfo.TextStyleId = mText.TextStyleId;
                        }
                    pInfo.Height = (pInfo.Height <= 0) ? 2.5 : pInfo.Height;

                    paragraphInfos.Add(pInfo);
                    }
                tr.Abort(); // 只读操作，中止事务
                }
            if (paragraphInfos.Count == 0)
                {
                _editor.WriteMessage("\n没有有效的翻译文本可供排版。");
                return true;
                }

            // 2. 开始交互式排版
            try
                {
                using (_doc.LockDocument())
                    {
                    var ppr = _editor.GetPoint("\n请为翻译后的段落指定左上角基点:");
                    if (ppr.Status != PromptStatus.OK) return false;
                    Point3d basePoint = ppr.Value;

                    var jig = new SmartLayoutJig(paragraphInfos, basePoint);
                    var dragResult = _editor.Drag(jig);
                    if (dragResult.Status != PromptStatus.OK) return false;

                    // 3. 用户确认后，将最终结果写入数据库
                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                        {
                        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                        foreach (var id in idsToDelete)
                            {
                            if (!id.IsErased)
                                {
                                var entToErase = tr.GetObject(id, OpenMode.ForWrite);
                                entToErase.Erase();
                                }
                            }

                        for (int i = 0; i < jig.FinalLineInfo.Count; i++)
                            {
                            var (lineText, paraNeedsIndent, isFirstLineOfPara, currentParaIndex) = jig.FinalLineInfo[i];
                            var currentParaInfo = paragraphInfos[currentParaIndex];
                            string finalLineText = lineText;

                            if (currentParaInfo.ContainsSpecialPattern && finalLineText.Contains(LegendPlaceholder))
                                {
                                PlaceGraphicsAlongsideText(finalLineText, currentParaInfo, basePoint, i, paraNeedsIndent, isFirstLineOfPara, jig, tr, modelSpace);
                                finalLineText = finalLineText.Replace(LegendPlaceholder, new string(' ', currentParaInfo.OriginalSpaceCount));
                                }

                            if (string.IsNullOrWhiteSpace(finalLineText)) continue;

                            using (var newText = new DBText())
                                {
                                // ▼▼▼【核心修正】就是下面这一行！▼▼▼
                                // 从模板实体完整复制如图层、颜色、线型等所有基本属性
                                newText.SetPropertiesFrom(currentParaInfo.TemplateEntity);
                                // ▲▲▲ 修正结束 ▲▲▲

                                newText.TextString = finalLineText;
                                newText.Height = currentParaInfo.Height;
                                newText.WidthFactor = currentParaInfo.WidthFactor;
                                newText.TextStyleId = currentParaInfo.TextStyleId;

                                double xOffset = (paraNeedsIndent && !isFirstLineOfPara) ? jig.FinalIndent : 0;
                                double yOffset = i * currentParaInfo.Height * 1.5;
                                Point3d linePosition = basePoint + new Vector3d(xOffset, -yOffset, 0);

                                newText.HorizontalMode = TextHorizontalMode.TextLeft;
                                newText.VerticalMode = TextVerticalMode.TextBase;
                                newText.Position = linePosition;

                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);
                                }
                            }
                        tr.Commit();
                        }
                    return true;
                    }
                }
            catch (System.Exception ex)
                {
                _editor.WriteMessage($"\n[CadLayoutService] 应用智能排版时发生错误: {ex.Message}\n{ex.StackTrace}");
                return false;
                }
            }


        private void PlaceGraphicsAlongsideText(string lineText, ParagraphInfo paraInfo, Point3d basePoint, int lineIndex, bool paraNeedsIndent, bool isFirstLineOfPara, SmartLayoutJig jig, Transaction tr, BlockTableRecord modelSpace)
            {
            int placeholderIndex = lineText.IndexOf(LegendPlaceholder);
            if (placeholderIndex < 0) return;
            string textBeforePlaceholderInLine = lineText.Substring(0, placeholderIndex);

            Point3d newAnchorPoint;

            using (var tempText = new DBText())
                {
                tempText.TextString = textBeforePlaceholderInLine;
                tempText.Height = paraInfo.Height;
                tempText.WidthFactor = paraInfo.WidthFactor;
                tempText.TextStyleId = paraInfo.TextStyleId;

                // 【核心】使用原始对齐属性来计算新锚点
                tempText.HorizontalMode = paraInfo.HorizontalMode;
                tempText.VerticalMode = paraInfo.VerticalMode;

                double xOffset = (paraNeedsIndent && !isFirstLineOfPara) ? jig.FinalIndent : 0;
                double yOffset = lineIndex * paraInfo.Height * 1.5;
                Point3d lineBasePoint = basePoint + new Vector3d(xOffset, -yOffset, 0);

                if (tempText.HorizontalMode == TextHorizontalMode.TextLeft && tempText.VerticalMode == TextVerticalMode.TextBase)
                    {
                    tempText.Position = lineBasePoint;
                    }
                else
                    {
                    tempText.AlignmentPoint = lineBasePoint;
                    }

                modelSpace.AppendEntity(tempText);
                tr.AddNewlyCreatedDBObject(tempText, true);
                tempText.AdjustAlignment(_db);

                newAnchorPoint = tempText.Bounds.HasValue ? tempText.Bounds.Value.MaxPoint : lineBasePoint;
                tempText.Erase();
                }

            Vector3d displacement = newAnchorPoint - paraInfo.OriginalAnchorPoint;
            Matrix3d transformMatrix = Matrix3d.Displacement(displacement);

            using (var blockRef = new BlockReference(paraInfo.OriginalAnchorPoint, paraInfo.AssociatedGraphicsBlockId))
                {
                blockRef.TransformBy(transformMatrix);
                modelSpace.AppendEntity(blockRef);
                tr.AddNewlyCreatedDBObject(blockRef, true);

                DBObjectCollection explodedObjects = new DBObjectCollection();
                blockRef.Explode(explodedObjects);
                foreach (DBObject obj in explodedObjects)
                    {
                    if (obj is Entity explodedEntity)
                        {
                        modelSpace.AppendEntity(explodedEntity);
                        tr.AddNewlyCreatedDBObject(explodedEntity, true);
                        }
                    }
                blockRef.Erase();
                }
            }

        public bool ApplyTranslationToCad(ObservableCollection<TextBlockViewModel> textBlockList)
            {
            // 这个方法在“实时排版”关闭时使用，其逻辑保持不变
            try
                {
                using (_doc.LockDocument())
                    {
                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                        {
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                        foreach (var item in textBlockList)
                            {
                            if (string.IsNullOrWhiteSpace(item.TranslatedText) || item.SourceObjectIds == null || !item.SourceObjectIds.Any()) continue;

                            // 简单的替换逻辑，用于非实时排版模式
                            string final_text = item.TranslatedText.Replace(LegendPlaceholder, new string(' ', item.OriginalSpaceCount));

                            var firstObjectId = item.SourceObjectIds.First();
                            if (firstObjectId.IsNull || firstObjectId.IsErased) continue;
                            var baseEntity = tr.GetObject(firstObjectId, OpenMode.ForRead) as Entity;
                            if (baseEntity == null) continue;

                            foreach (var objectId in item.SourceObjectIds)
                                {
                                if (objectId.IsNull || objectId.IsErased) continue;
                                var entityToErase = tr.GetObject(objectId, OpenMode.ForWrite) as Entity;
                                entityToErase?.Erase();
                                }

                            string singleLineText = final_text.Replace('\n', ' ').Replace('\r', ' ');
                            using (DBText newText = new DBText())
                                {
                                newText.TextString = singleLineText;
                                newText.SetPropertiesFrom(baseEntity);
                                if (baseEntity is DBText originalDbText)
                                    {
                                    newText.Position = originalDbText.Position;
                                    newText.Height = originalDbText.Height;
                                    newText.Rotation = originalDbText.Rotation;
                                    newText.Oblique = originalDbText.Oblique;
                                    newText.WidthFactor = originalDbText.WidthFactor;
                                    newText.TextStyleId = originalDbText.TextStyleId;
                                    newText.HorizontalMode = originalDbText.HorizontalMode;
                                    newText.VerticalMode = originalDbText.VerticalMode;
                                    if (newText.HorizontalMode != TextHorizontalMode.TextLeft || newText.VerticalMode != TextVerticalMode.TextBase)
                                        {
                                        newText.AlignmentPoint = originalDbText.AlignmentPoint;
                                        }
                                    }
                                else if (baseEntity is MText originalMText)
                                    {
                                    newText.Position = originalMText.Location;
                                    newText.Height = originalMText.TextHeight;
                                    newText.Rotation = originalMText.Rotation;
                                    newText.TextStyleId = originalMText.TextStyleId;
                                    newText.HorizontalMode = TextHorizontalMode.TextLeft;
                                    newText.VerticalMode = TextVerticalMode.TextBase;
                                    }

                                if (newText.Height <= 0) newText.Height = 2.5;
                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);
                                }
                            }
                        tr.Commit();
                        _editor.WriteMessage("\n所有翻译已成功应用到CAD图纸！");
                        return true;
                        }
                    }
                }
            catch (System.Exception ex)
                {
                _editor.WriteMessage($"\n将翻译应用到CAD时发生严重错误: {ex.Message}");
                return false;
                }
            }
        }
    }