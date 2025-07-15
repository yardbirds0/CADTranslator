// 请用此完整代码替换 CADTranslator/Services/CadLayoutService.cs 的全部内容

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.AutoCAD.Jigs; // 确保引用 Jigs
using CADTranslator.Models;
using CADTranslator.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace CADTranslator.Services
    {
    public class CadLayoutService
        {
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

        // ▼▼▼ 这是最终简化和修正后的版本 ▼▼▼
        public bool ApplySmartLayoutToCad(ObservableCollection<TextBlockViewModel> textBlockList)
            {
            var paragraphInfos = new List<ParagraphInfo>();
            // 1. 将 ViewModel 转换为服务层能理解的 ParagraphInfo
            // 这个过程现在非常简单，只是数据的直接传递
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
                        Text = block.TranslatedText, // 使用翻译后的文本
                        TemplateEntity = templateEntity,
                        AssociatedGraphicsBlockId = block.AssociatedGraphicsBlockId,
                        OriginalAnchorPoint = block.OriginalAnchorPoint, // 直接从ViewModel获取锚点
                        ContainsSpecialPattern = !block.AssociatedGraphicsBlockId.IsNull,
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
                tr.Abort(); // 我们只读取了数据，所以中止事务即可
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

                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                        {
                        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                        // 删除所有旧实体
                        var allSourceIds = paragraphInfos.SelectMany(p => p.SourceObjectIds).ToHashSet();
                        foreach (var id in allSourceIds)
                            {
                            if (!id.IsErased)
                                {
                                var entToErase = tr.GetObject(id, OpenMode.ForWrite);
                                entToErase.Erase();
                                }
                            }

                        // 创建新实体
                        for (int i = 0; i < jig.FinalLineInfo.Count; i++)
                            {
                            var (lineText, paraNeedsIndent, isFirstLineOfPara, currentParaIndex) = jig.FinalLineInfo[i];
                            var currentParaInfo = paragraphInfos[currentParaIndex];
                            string placeholder = "*图例位置*";

                            if (currentParaInfo.ContainsSpecialPattern && lineText.Contains(placeholder))
                                {
                                PlaceGraphicsAlongsideText(lineText, currentParaInfo, basePoint, i, paraNeedsIndent, isFirstLineOfPara, jig, tr, modelSpace);
                                lineText = lineText.Replace(placeholder, "");
                                }

                            using (var newText = new DBText())
                                {
                                if (currentParaInfo.TemplateEntity != null) newText.SetPropertiesFrom(currentParaInfo.TemplateEntity);
                                newText.TextString = lineText;
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
                _editor.WriteMessage($"\n[CadLayoutService] 应用智能排版时发生错误: {ex.Message}");
                return false;
                }
            }

        // 辅助方法：在文本旁边放置图例
        private void PlaceGraphicsAlongsideText(string lineText, ParagraphInfo paraInfo, Point3d basePoint, int lineIndex, bool paraNeedsIndent, bool isFirstLineOfPara, SmartLayoutJig jig, Transaction tr, BlockTableRecord modelSpace)
            {
            int placeholderIndex = lineText.IndexOf("*图例位置*");
            string textBeforePlaceholderInLine = lineText.Substring(0, placeholderIndex);

            // 计算图例应该被放置到的新位置
            using (var tempText = new DBText { TextString = textBeforePlaceholderInLine, Height = paraInfo.Height, WidthFactor = paraInfo.WidthFactor, TextStyleId = paraInfo.TextStyleId })
                {
                double xOffset = (paraNeedsIndent && !isFirstLineOfPara) ? jig.FinalIndent : 0;
                double yOffset = lineIndex * paraInfo.Height * 1.5;
                Point3d linePos = basePoint + new Vector3d(xOffset, -yOffset, 0);
                tempText.Position = linePos;

                // 关键：计算新旧锚点的位移
                Point3d newAnchorPoint = tempText.Bounds.HasValue ? tempText.Bounds.Value.MaxPoint : linePos;
                Vector3d displacement = newAnchorPoint - paraInfo.OriginalAnchorPoint;
                Matrix3d transformMatrix = Matrix3d.Displacement(displacement);

                // 将我们之前打包好的匿名块插入，并应用位移变换
                using (var blockRef = new BlockReference(Point3d.Origin, paraInfo.AssociatedGraphicsBlockId))
                    {
                    blockRef.TransformBy(transformMatrix);
                    modelSpace.AppendEntity(blockRef);
                    tr.AddNewlyCreatedDBObject(blockRef, true);

                    // 将块炸开，变回独立的图形实体
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
                    blockRef.Erase(); // 删除块参照
                    }
                }
            }

        // 这是之前从后台代码移动过来的核心方法 (保持不变)
        public bool ApplyTranslationToCad(ObservableCollection<TextBlockViewModel> textBlockList)
            {
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
                            string singleLineText = item.TranslatedText.Replace('\n', ' ').Replace('\r', ' ');
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
                        return true; // 操作成功，返回 true
                        }
                    }
                }
            catch (System.Exception ex)
                {
                _editor.WriteMessage($"\n将翻译应用到CAD时发生严重错误: {ex.Message}");
                return false; // 操作失败，返回 false
                }
            }
        }
    }