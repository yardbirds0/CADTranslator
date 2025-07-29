using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models;
using CADTranslator.Models.CAD;
using CADTranslator.Services.Settings;
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
            var settingsService = new SettingsService();
            var currentSettings = settingsService.LoadSettings();
            bool addUnderline = currentSettings.AddUnderlineAfterSmartLayout;

            var paragraphInfos = new List<ParagraphInfo>();
            try // ◄◄◄ 1. 在最外层包裹 try
                {

                // 步骤 1: 准备Jig所需的数据 (现在会完整地传递所有几何信息)
                using (var tr = _db.TransactionManager.StartTransaction())
                    {
                    foreach (var block in textBlockList)
                        {
                        if (string.IsNullOrWhiteSpace(block.TranslatedText) || block.TranslatedText.StartsWith("[")) continue;
                        Entity templateEntity = null;
                        foreach (var id in block.SourceObjectIds)
                            {
                            if (id.IsNull || id.IsErased) continue;
                            var entity = tr.GetObject(id, OpenMode.ForRead);
                            if (entity is DBText || entity is MText)
                                {
                                templateEntity = entity as Entity;
                                break;
                                }
                            }
                        if (templateEntity == null) continue;

                        var pInfo = new ParagraphInfo
                            {
                            Text = block.TranslatedText,
                            TemplateEntity = templateEntity,
                            AssociatedGraphicsBlockId = block.AssociatedGraphicsBlockId,
                            OriginalAnchorPoint = block.OriginalAnchorPoint,
                            OriginalSpaceCount = block.OriginalSpaceCount,
                            Position = block.Position,
                            AlignmentPoint = block.AlignmentPoint,
                            HorizontalMode = block.HorizontalMode,
                            VerticalMode = block.VerticalMode,
                            SourceObjectIds = block.SourceObjectIds.ToList(),
                            GroupKey = block.GroupKey,
                            Rotation = block.Rotation, // 传递旋转角度
                            Oblique = block.Oblique
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
                    tr.Abort();
                    }

                if (paragraphInfos.Count == 0)
                    {
                    _editor.WriteMessage("\n没有有效的翻译文本可供排版。");
                    return true;
                    }


                // 步骤 2: 开始交互式排版
                using (_doc.LockDocument())
                    {
                    var ppr = _editor.GetPoint("\n请为翻译后的段落指定左上角基点:");
                    if (ppr.Status != PromptStatus.OK) return false;
                    Point3d basePoint = ppr.Value;

                    // 【核心修改】从paragraphInfos获取原始旋转角度，并传递给Jig
                    double rotation = 0;
                    if (paragraphInfos.Any())
                        {
                        rotation = paragraphInfos[0].Rotation;
                        }

                    var jig = new SmartLayoutJig(paragraphInfos, basePoint, lineSpacing, rotation);
                    var dragResult = _editor.Drag(jig);
                    if (dragResult.Status != PromptStatus.OK) return false;

                    // 步骤 3: 用户确认后，将最终结果写入数据库
                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                        {
                        var objectsToUnderline = new Dictionary<ObjectId, bool>();
                        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                        foreach (var id in idsToDelete)
                            {
                            if (!id.IsErased)
                                {
                                var entToErase = tr.GetObject(id, OpenMode.ForWrite);
                                entToErase.Erase();
                                }
                            }

                        var newTextIds = new List<ObjectId>(); // 用于收集新文字ID以便添加下划线

                        for (int i = 0; i < jig.FinalLineInfo.Count; i++)
                            {
                            var (lineText, paraNeedsIndent, isFirstLineOfPara, currentParaIndex, finalWcsPosition) = jig.FinalLineInfo[i];
                            var currentParaInfo = paragraphInfos[currentParaIndex];
                            string finalLineText = lineText;

                            var match = FindJigPlaceholderMatch(finalLineText);

                            if (match.Success)
                                {
                                PlaceGraphicsAlongsideText(finalLineText, match, currentParaInfo, finalWcsPosition, jig, tr, modelSpace);
                                string matchedValue = match.Value;
                                string corePlaceholder = AdvancedTextService.JigPlaceholder;
                                string replacementString = matchedValue.Replace(corePlaceholder, new string(' ', currentParaInfo.OriginalSpaceCount));
                                finalLineText = finalLineText.Remove(match.Index, match.Length).Insert(match.Index, replacementString);
                                }

                            if (string.IsNullOrWhiteSpace(finalLineText)) continue;

                            using (var newText = new DBText())
                                {
                                newText.SetPropertiesFrom(currentParaInfo.TemplateEntity);
                                newText.TextString = finalLineText;
                                newText.Height = currentParaInfo.Height;
                                newText.WidthFactor = currentParaInfo.WidthFactor;
                                newText.TextStyleId = currentParaInfo.TextStyleId;
                                newText.Rotation = rotation; // 【核心修改】应用旋转角度
                                newText.Oblique = currentParaInfo.Oblique;

                                var originalHMode = currentParaInfo.HorizontalMode;
                                var originalVMode = currentParaInfo.VerticalMode;
                                newText.HorizontalMode = originalHMode;
                                newText.VerticalMode = originalVMode;

                                // 【核心修正】智能判断：保留原始对齐方式，并让CAD自动调整
                                if (originalHMode == TextHorizontalMode.TextLeft && originalVMode == TextVerticalMode.TextBase)
                                    {
                                    newText.Position = finalWcsPosition;
                                    }
                                else
                                    {
                                    newText.AlignmentPoint = finalWcsPosition;
                                    newText.AdjustAlignment(_db); // <-- 关键！
                                    }

                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);
                                newText.AdjustAlignment(_db);

                                objectsToUnderline.Add(newText.ObjectId, currentParaInfo.IsTitle);
                                }
                            }

                        // 【核心修改】在事务提交前，检查是否需要添加下划线
                        if (addUnderline && objectsToUnderline.Any())
                            {
                            _editor.WriteMessage($"\n正在为新生成的 {objectsToUnderline.Count} 个文本对象添加下划线...");

                            tr.Commit(); // 先提交，让下划线服务能访问到新对象

                            var underlineService = new UnderlineService(_doc);
                            var underlineOptions = new UnderlineOptions();
                            underlineService.AddUnderlinesToObjectIds(objectsToUnderline, underlineOptions); // <-- 传递指令字典
                            }
                        else
                            {
                            tr.Commit();
                            }
                        }
                    return true;
                    }
                }
            catch (System.Exception ex) // ◄◄◄ 2. 捕获所有可能的异常
                {
                _editor.WriteMessage($"\n[CadLayoutService] 应用智能排版时发生严重错误: {ex.Message}\n{ex.StackTrace}");
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
            if (!paraInfo.AssociatedGraphicsBlockId.IsNull)
                {
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
            }


        public bool ApplyTranslationToCad(ObservableCollection<TextBlockViewModel> textBlockList, List<ObjectId> idsToDelete)
            {
            // 注意：参数中的 idsToDelete 在这个新逻辑中不再被直接使用，
            // 因为我们会在循环内部根据每个 item 的 SourceObjectIds 来删除。
            // 但我们保留它以维持接口的一致性。

            try
                {
                using (_doc.LockDocument())
                    {
                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                        {
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                        // 【核心逻辑修正】
                        // 我们不再先统一删除，而是遍历每个翻译项，完成“创建”之后再“删除”
                        foreach (var item in textBlockList)
                            {
                            // 跳过无效的翻译项
                            if (string.IsNullOrWhiteSpace(item.TranslatedText) || item.TranslatedText.StartsWith("[")) continue;

                            // 获取该翻译项的一个源对象作为基础属性（图层、颜色等）的模板
                            var firstObjectId = item.SourceObjectIds.FirstOrDefault();
                            if (firstObjectId.IsNull || firstObjectId.IsErased) continue;

                            var baseEntity = tr.GetObject(firstObjectId, OpenMode.ForRead) as Entity;
                            if (baseEntity == null) continue;

                            string final_text = item.TranslatedText.Replace(LegendPlaceholder, new string(' ', item.OriginalSpaceCount));
                            string singleLineText = final_text.Replace('\n', ' ').Replace('\r', ' ');

                            // 步骤 1: 创建新的翻译后文字
                            using (DBText newText = new DBText())
                                {
                                newText.TextString = singleLineText;
                                newText.SetPropertiesFrom(baseEntity); // 继承图层、颜色等基础属性

                                // 【核心】所有几何和样式属性，现在都完整地从 ViewModel 中获取
                                newText.Height = (item.Height <= 0) ? 2.5 : item.Height;
                                newText.Rotation = item.Rotation;
                                newText.Oblique = item.Oblique;
                                newText.WidthFactor = item.WidthFactor;
                                newText.TextStyleId = item.TextStyleId;
                                newText.HorizontalMode = item.HorizontalMode;
                                newText.VerticalMode = item.VerticalMode;

                                // 根据对齐方式设置 Position 或 AlignmentPoint
                                if (item.HorizontalMode == TextHorizontalMode.TextLeft && item.VerticalMode == TextVerticalMode.TextBase)
                                    {
                                    newText.Position = item.Position;
                                    }
                                else
                                    {
                                    newText.AlignmentPoint = item.AlignmentPoint;
                                    }

                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);

                                // 如果需要，调整对齐
                                if (newText.HorizontalMode != TextHorizontalMode.TextLeft || newText.VerticalMode != TextVerticalMode.TextBase)
                                    {
                                    newText.AdjustAlignment(_db);
                                    }
                                }

                            // 步骤 2: 在新文字创建成功后，再删除这个翻译项对应的所有源对象
                            foreach (var idToErase in item.SourceObjectIds)
                                {
                                if (!idToErase.IsNull && !idToErase.IsErased)
                                    {
                                    var entityToErase = tr.GetObject(idToErase, OpenMode.ForWrite) as Entity;
                                    entityToErase?.Erase();
                                    }
                                }
                            }

                        tr.Commit(); // 所有操作成功，提交事务
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