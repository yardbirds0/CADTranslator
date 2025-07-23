// 文件路径: CADTranslator/Services/CadLayoutService.cs

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
            // ▼▼▼ 【新增代码】在这里加载设置 ▼▼▼
            var settingsService = new SettingsService();
            var currentSettings = settingsService.LoadSettings();
            bool addUnderline = currentSettings.AddUnderlineAfterSmartLayout;
            // ▲▲▲ 新增结束 ▲▲▲

            var paragraphInfos = new List<ParagraphInfo>();

            // 1. 准备Jig所需的数据 (这部分不变)
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

                    // ... (这部分数据准备的代码也完全不变) ...

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
                        GroupKey = block.GroupKey
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

            // 2. 开始交互式排版 (这部分不变)
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

                        // ▼▼▼ 【新增代码】创建一个列表来收集新生成的文字ID ▼▼▼
                        var newTextIds = new List<ObjectId>();

                        for (int i = 0; i < jig.FinalLineInfo.Count; i++)
                            {
                            // ... (这部分Jig结果处理和文字创建的逻辑完全不变) ...
                            var (lineText, paraNeedsIndent, isFirstLineOfPara, currentParaIndex, linePosition) = jig.FinalLineInfo[i];
                            var currentParaInfo = paragraphInfos[currentParaIndex];
                            string finalLineText = lineText;

                            var match = FindJigPlaceholderMatch(finalLineText);

                            if (match.Success)
                                {
                                PlaceGraphicsAlongsideText(finalLineText, match, currentParaInfo, linePosition, jig, tr, modelSpace);
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
                                var originalHMode = currentParaInfo.HorizontalMode;
                                var originalVMode = currentParaInfo.VerticalMode;
                                newText.HorizontalMode = originalHMode;
                                newText.VerticalMode = originalVMode;
                                if (originalHMode == TextHorizontalMode.TextLeft && originalVMode == TextVerticalMode.TextBase)
                                    {
                                    newText.Position = linePosition;
                                    }
                                else
                                    {
                                    newText.AlignmentPoint = linePosition;
                                    }
                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);
                                newText.AdjustAlignment(_db);

                                // ▼▼▼ 【新增代码】将新文字的ID添加到列表中 ▼▼▼
                                newTextIds.Add(newText.ObjectId);
                                }
                            }

                        // ▼▼▼ 【新增代码】在这里实装下划线功能 ▼▼▼
                        if (addUnderline && newTextIds.Any())
                            {
                            _editor.WriteMessage($"\n正在为新生成的 {newTextIds.Count} 个文本对象添加下划线...");
                            var underlineService = new UnderlineService(_doc);
                            var underlineOptions = new UnderlineOptions(); // 使用默认的标题/正文样式

                            // 注意：这里我们不能直接调用 underlineService 的方法，因为它需要自己的事务。
                            // 因此，我们先提交当前事务，然后再调用它。
                            tr.Commit(); // 先提交文字创建

                            // 在新的独立事务中添加下划线
                            underlineService.AddUnderlinesToObjectIds(newTextIds, underlineOptions);
                            }
                        else
                            {
                            tr.Commit(); // 如果不需要添加下划线，正常提交即可
                            }

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