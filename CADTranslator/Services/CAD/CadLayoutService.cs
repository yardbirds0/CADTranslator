// 文件路径: CADTranslator/Services/CadLayoutService.cs

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models;
using CADTranslator.Models.CAD;
using CADTranslator.Tools.CAD.Jigs;
using CADTranslator.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace CADTranslator.Services.CAD
    {
    // ▼▼▼ 【核心修改】在这里添加对 ICadLayoutService 接口的实现 ▼▼▼
    public class CadLayoutService : ICadLayoutService
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

        public bool ApplySmartLayoutToCad(ObservableCollection<TextBlockViewModel> textBlockList, List<ObjectId> idsToDelete, string lineSpacing)
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
                        //ContainsSpecialPattern = block.TranslatedText.Contains(LegendPlaceholder),
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

                    var jig = new SmartLayoutJig(paragraphInfos, basePoint, lineSpacing);
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
                            var (lineText, paraNeedsIndent, isFirstLineOfPara, currentParaIndex, linePosition) = jig.FinalLineInfo[i];
                            var currentParaInfo = paragraphInfos[currentParaIndex];
                            string finalLineText = lineText;

                            var match = FindJigPlaceholderMatch(finalLineText);

                            if (match.Success)
                                {
                                // 1. 调用图例定位方法（这部分逻辑不变且正确）
                                PlaceGraphicsAlongsideText(finalLineText, match, currentParaInfo, linePosition, jig, tr, modelSpace);

                                // 2. 【核心修正】执行智能替换
                                string matchedValue = match.Value; // 获取匹配到的完整字符串，如 "*图例位置*"
                                string corePlaceholder = AdvancedTextService.JigPlaceholder; // 获取核心占位符，如 *图例位置*

                                // 3. 只替换核心部分：将 "*图例位置*" 内部的 *图例位置* 替换为空格
                                string replacementString = matchedValue.Replace(corePlaceholder, new string(' ', currentParaInfo.OriginalSpaceCount));

                                // 4. 将原文中的整个匹配项，替换为我们新构造的、保留了引号的字符串
                                finalLineText = finalLineText.Remove(match.Index, match.Length).Insert(match.Index, replacementString);
                                }

                            if (string.IsNullOrWhiteSpace(finalLineText)) continue;

                            using (var newText = new DBText())
                                {
                                // 1. 先从模板继承基础样式
                                newText.SetPropertiesFrom(currentParaInfo.TemplateEntity);
                                newText.TextString = finalLineText;
                                newText.Height = currentParaInfo.Height;
                                newText.WidthFactor = currentParaInfo.WidthFactor;
                                newText.TextStyleId = currentParaInfo.TextStyleId;

                                // 2. 【核心修改】根据原始文本的对齐方式，设置正确的位置属性
                                // 我们从 ParagraphInfo 中获取原始的对齐模式
                                var originalHMode = currentParaInfo.HorizontalMode;
                                var originalVMode = currentParaInfo.VerticalMode;

                                newText.HorizontalMode = originalHMode;
                                newText.VerticalMode = originalVMode;

                                // 3. 判断：到底该用 Position 还是 AlignmentPoint？
                                if (originalHMode == TextHorizontalMode.TextLeft && originalVMode == TextVerticalMode.TextBase)
                                    {
                                    // 对于默认的“左下角”对齐，我们设置 Position
                                    newText.Position = linePosition;
                                    }
                                else
                                    {
                                    // 对于所有其他的对齐方式，我们必须设置 AlignmentPoint
                                    newText.AlignmentPoint = linePosition;
                                    }

                                // 4. 将新文字添加到模型空间
                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);

                                // 5. 【关键】在添加后，调用一次 AdjustAlignment，确保万无一失
                                newText.AdjustAlignment(_db);
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

        private void PlaceGraphicsAlongsideText(string lineText, System.Text.RegularExpressions.Match placeholderMatch, ParagraphInfo paraInfo, Point3d lineBasePoint, SmartLayoutJig jig, Transaction tr, BlockTableRecord modelSpace)
            {
            // 直接精确查找我们新的、短小的Jig占位符
            string textBeforePlaceholderInLine = lineText.Substring(0, placeholderMatch.Index);

            Point3d newAnchorPoint;

            using (var tempText = new DBText())
                {
                tempText.TextString = textBeforePlaceholderInLine;
                tempText.Height = paraInfo.Height;
                tempText.WidthFactor = paraInfo.WidthFactor;
                tempText.TextStyleId = paraInfo.TextStyleId;

                tempText.HorizontalMode = paraInfo.HorizontalMode;
                tempText.VerticalMode = paraInfo.VerticalMode;

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

        public bool ApplyTranslationToCad(ObservableCollection<TextBlockViewModel> textBlockList, List<ObjectId> idsToDelete)
            {
            try
                {
                using (_doc.LockDocument())
                    {
                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                        {
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                        // 【核心修正】使用从ViewModel传入的、精确的待删除列表
                        foreach (var objectId in idsToDelete)
                            {
                            if (!objectId.IsNull && !objectId.IsErased)
                                {
                                var entityToErase = tr.GetObject(objectId, OpenMode.ForWrite) as Entity;
                                entityToErase?.Erase();
                                }
                            }

                        foreach (var item in textBlockList)
                            {
                            if (string.IsNullOrWhiteSpace(item.TranslatedText) || item.TranslatedText.StartsWith("[")) continue;

                            var firstObjectId = item.SourceObjectIds.FirstOrDefault();
                            if (firstObjectId.IsNull || firstObjectId.IsErased) continue;
                            var baseEntity = tr.GetObject(firstObjectId, OpenMode.ForRead) as Entity;
                            if (baseEntity == null) continue;

                            string final_text = item.TranslatedText.Replace(LegendPlaceholder, new string(' ', item.OriginalSpaceCount));
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

        private System.Text.RegularExpressions.Match FindJigPlaceholderMatch(string text)
            {
            // 1. 对核心占位符中的特殊字符(*)进行转义，确保它们被当作普通文本匹配
            string corePlaceholder = System.Text.RegularExpressions.Regex.Escape(AdvancedTextService.JigPlaceholder);

            // 2. 创建一个新的正则表达式，它会优先匹配带引号的版本，如果找不到，再匹配不带引号的版本
            //    ("corePlaceholder"|corePlaceholder) -> "或者" 的关系
            var regex = new System.Text.RegularExpressions.Regex($"(\"{corePlaceholder}\"|{corePlaceholder})");
            return regex.Match(text);
            }
        }
    }