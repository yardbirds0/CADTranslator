// 文件路径: CADTranslator/Services/CAD/UnderlineService.cs
// 【请用此代码完整替换】

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions; // ◄◄◄ 【新增】引入正则表达式库

namespace CADTranslator.Services.CAD
    {
    /// <summary>
    /// 提供为文字添加下划线功能的服务。
    /// 【已升级】现在能够为标题和普通文本应用不同的下划线样式。
    /// </summary>
    public class UnderlineService
        {
        #region --- 字段与构造函数 ---

        private readonly Document _doc;
        private readonly Database _db;
        private readonly Editor _editor;

        // 【新增】用于识别标题的正则表达式，与AdvancedTextService保持一致
        private readonly Regex _titleMarkers = new Regex(@"(说明|注意|技术要求|参数|示例|NOTES|SPECIFICATION|LEGEND|DESCRIPTION)[\s:：]*$|:$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public UnderlineService(Document doc)
            {
            _doc = doc;
            _db = doc.Database;
            _editor = doc.Editor;
            }

        #endregion

        #region --- 1. 交互式入口 (供 WZXHX 命令调用) ---

        /// <summary>
        /// 【保持不变】这是给用户直接使用的 WZXHX 命令的入口。
        /// 它会提示用户去选择对象。
        /// </summary>
        public void AddUnderlinesToSelectedText(UnderlineOptions options)
            {
            var selRes = _editor.GetSelection();
            if (selRes.Status != PromptStatus.OK) return;

            // 调用核心的执行逻辑
            ExecuteUnderlining(selRes.Value, options);
            }

        #endregion

        #region --- 2. 自动化入口 (供 WZPB 命令调用) ---

        /// <summary>
        /// 【保持不变】这是给其他命令自动化调用的入口。
        /// </summary>
        public void AddUnderlinesToObjectIds(List<ObjectId> objectIds, UnderlineOptions options)
            {
            if (objectIds == null || !objectIds.Any()) return;

            var selSet = SelectionSet.FromObjectIds(objectIds.ToArray());

            // 调用核心的执行逻辑
            ExecuteUnderlining(selSet, options);
            }

        #endregion

        // 文件路径: CADTranslator/Services/CAD/UnderlineService.cs

        #region --- 3. 核心执行逻辑 (ExecuteUnderlining) ---

        private void ExecuteUnderlining(SelectionSet selSet, UnderlineOptions options)
            {
            using (var tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    EnsureLayerAndLinetype(tr, options.TitleStyle);
                    EnsureLayerAndLinetype(tr, options.DefaultStyle);

                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                    var textEntities = new List<Entity>();
                    foreach (SelectedObject selObj in selSet)
                        {
                        var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent is DBText || ent is MText)
                            {
                            textEntities.Add(ent);
                            }
                        }

                    if (textEntities.Count == 0)
                        {
                        if (_editor.IsQuiescent) _editor.WriteMessage("\n选择的对象中未找到任何有效文字。");
                        return;
                        }

                    // 步骤 1: 只针对“非标题”的普通行，计算最大宽度和最左对齐点
                    var nonTitleEntities = textEntities.Where(ent => !_titleMarkers.IsMatch(GetTextFromEntity(ent).Trim())).ToList();

                    double maxWidth = 0;
                    if (nonTitleEntities.Any())
                        {
                        foreach (var ent in nonTitleEntities)
                            {
                            double currentWidth = 0;
                            if (ent is DBText dbText)
                                {
                                using (var tempText = (DBText)dbText.Clone())
                                    {
                                    tempText.Rotation = 0;
                                    if (tempText.HorizontalMode != TextHorizontalMode.TextLeft || tempText.VerticalMode != TextVerticalMode.TextBase)
                                        {
                                        tempText.Position = tempText.AlignmentPoint;
                                        tempText.Justify = AttachmentPoint.BaseLeft;
                                        }
                                    var extents = tempText.GeometricExtents;
                                    if (extents != null) currentWidth = extents.MaxPoint.X - extents.MinPoint.X;
                                    }
                                }
                            else if (ent is MText mText)
                                {
                                currentWidth = mText.ActualWidth;
                                }
                            if (currentWidth > maxWidth) maxWidth = currentWidth;
                            }
                        }

                    double minUcsX = double.MaxValue;
                    double dominantRotation = 0;
                    if (nonTitleEntities.Any())
                        {
                        if (nonTitleEntities.FirstOrDefault() is DBText firstDbText) dominantRotation = firstDbText.Rotation;
                        else if (nonTitleEntities.FirstOrDefault() is MText firstMText) dominantRotation = firstMText.Rotation;

                        Matrix3d wcsToUcs = Matrix3d.Rotation(-dominantRotation, Vector3d.ZAxis, Point3d.Origin);
                        foreach (var ent in nonTitleEntities)
                            {
                            Point3d insertionPoint = (ent is DBText dbText) ? dbText.Position : ((MText)ent).Location;
                            Point3d insertionPointUcs = insertionPoint.TransformBy(wcsToUcs);
                            if (insertionPointUcs.X < minUcsX) minUcsX = insertionPointUcs.X;
                            }
                        }

                    // 步骤 2: 遍历所有行，根据是否为标题，应用不同逻辑
                    int linesUnderlined = 0;
                    foreach (var ent in textEntities)
                        {
                        double rotation = 0, height = 0, unrotatedWidth = 0;
                        string textContent = "";
                        Point3d originalInsertionPoint = Point3d.Origin;

                        if (ent is DBText dbText)
                            {
                            rotation = dbText.Rotation;
                            height = dbText.Height;
                            textContent = dbText.TextString;
                            originalInsertionPoint = dbText.Position;
                            }
                        else if (ent is MText mText)
                            {
                            rotation = mText.Rotation;
                            height = mText.TextHeight;
                            textContent = mText.Text;
                            originalInsertionPoint = mText.Location;
                            }

                        bool isTitle = _titleMarkers.IsMatch(textContent.Trim());
                        UnderlineStyle styleToUse = isTitle ? options.TitleStyle : options.DefaultStyle;

                        double verticalOffset = isTitle ? -(height / 4.0) : options.VerticalOffset;
                        Vector3d offsetVector = new Vector3d(0, verticalOffset, 0);
                        Matrix3d rotationMatrix = Matrix3d.Rotation(rotation, Vector3d.ZAxis, Point3d.Origin);
                        Vector3d rotatedOffset = offsetVector.TransformBy(rotationMatrix);

                        Point3d startPointWcs, endPointWcs;

                        if (isTitle)
                            {
                            // --- 标题逻辑：使用自身宽度和位置 ---
                            // 【核心修正】不再重新声明变量，而是直接使用克隆体来计算宽度
                            using (var tempClone = (Entity)ent.Clone())
                                {
                                if (tempClone is DBText tempDb)
                                    {
                                    tempDb.Rotation = 0;
                                    if (tempDb.HorizontalMode != TextHorizontalMode.TextLeft || tempDb.VerticalMode != TextVerticalMode.TextBase) { tempDb.Position = tempDb.AlignmentPoint; tempDb.Justify = AttachmentPoint.BaseLeft; }
                                    var extents = tempDb.GeometricExtents;
                                    if (extents != null) unrotatedWidth = extents.MaxPoint.X - extents.MinPoint.X;
                                    }
                                else if (tempClone is MText tempMtext) unrotatedWidth = tempMtext.ActualWidth;
                                }

                            Vector3d baselineVector = new Vector3d(unrotatedWidth, 0, 0);
                            Vector3d rotatedBaseline = baselineVector.TransformBy(rotationMatrix);
                            startPointWcs = originalInsertionPoint + rotatedOffset;
                            endPointWcs = startPointWcs + rotatedBaseline;
                            }
                        else
                            {
                            // --- 正文逻辑：使用最大宽度和最左对齐点 ---
                            Matrix3d wcsToUcs = Matrix3d.Rotation(-dominantRotation, Vector3d.ZAxis, Point3d.Origin);
                            Point3d originalInsertionPointUcs = originalInsertionPoint.TransformBy(wcsToUcs);
                            Point3d alignedInsertionPointUcs = new Point3d(minUcsX, originalInsertionPointUcs.Y, originalInsertionPointUcs.Z);
                            Matrix3d ucsToWcs = Matrix3d.Rotation(dominantRotation, Vector3d.ZAxis, Point3d.Origin);
                            Point3d alignedInsertionPointWcs = alignedInsertionPointUcs.TransformBy(ucsToWcs);

                            Vector3d baselineVector = new Vector3d(maxWidth, 0, 0);
                            Vector3d rotatedBaseline = baselineVector.TransformBy(rotationMatrix);
                            startPointWcs = alignedInsertionPointWcs + rotatedOffset;
                            endPointWcs = startPointWcs + rotatedBaseline;
                            }

                        // ... (创建Polyline或Line的部分保持不变) ...
                        if (isTitle)
                            {
                            using (var underline = new Polyline())
                                {
                                underline.AddVertexAt(0, new Point2d(startPointWcs.X, startPointWcs.Y), 0, 0, 0);
                                underline.AddVertexAt(1, new Point2d(endPointWcs.X, endPointWcs.Y), 0, 0, 0);
                                underline.SetDatabaseDefaults();
                                underline.Layer = styleToUse.Layer;
                                underline.ColorIndex = styleToUse.ColorIndex;
                                underline.Linetype = styleToUse.Linetype;
                                underline.LinetypeScale = styleToUse.LinetypeScale;
                                underline.ConstantWidth = styleToUse.GlobalWidth;
                                modelSpace.AppendEntity(underline);
                                tr.AddNewlyCreatedDBObject(underline, true);
                                }
                            }
                        else
                            {
                            using (var underline = new Line(startPointWcs, endPointWcs))
                                {
                                underline.SetDatabaseDefaults();
                                underline.Layer = styleToUse.Layer;
                                underline.ColorIndex = styleToUse.ColorIndex;
                                underline.Linetype = styleToUse.Linetype;
                                underline.LinetypeScale = styleToUse.LinetypeScale;
                                modelSpace.AppendEntity(underline);
                                tr.AddNewlyCreatedDBObject(underline, true);
                                }
                            }
                        linesUnderlined++;
                        }

                    tr.Commit();
                    if (_editor.IsQuiescent) _editor.WriteMessage($"\n成功为 {linesUnderlined} 个文字对象添加了下划线。");
                    }
                catch (Exception ex)
                    {
                    _editor.WriteMessage($"\n添加下划线时出错: {ex.Message}\n{ex.StackTrace}");
                    tr.Abort();
                    }
                }
            }

        #endregion

        #region --- 4. 辅助方法 ---

        /// <summary>
        /// 【已修改】确保下划线样式中定义的图层和线型都存在。
        /// </summary>
        private void EnsureLayerAndLinetype(Transaction tr, UnderlineStyle style)
            {
            // 确保图层存在
            var layerTable = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(style.Layer))
                {
                // 如果需要写入，则必须用写模式打开对象
                layerTable.UpgradeOpen();
                using (var newLayer = new LayerTableRecord { Name = style.Layer })
                    {
                    layerTable.Add(newLayer);
                    tr.AddNewlyCreatedDBObject(newLayer, true);
                    }
                layerTable.DowngradeOpen(); // 操作完成，降回读模式
                }

            // 确保线型存在
            var linetypeTable = (LinetypeTable)tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            if (!string.Equals(style.Linetype, "ByLayer", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(style.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase) &&
                !linetypeTable.Has(style.Linetype))
                {
                try { _db.LoadLineTypeFile(style.Linetype, "acad.lin"); }
                catch { _editor.WriteMessage($"\n警告: 无法从 acad.lin 加载线型 '{style.Linetype}'。将使用默认连续线型。"); }
                }
            }

        /// <summary>
        /// 【新增】一个安全的、统一的从DBText或MText获取文本的方法。
        /// </summary>
        private string GetTextFromEntity(Entity ent)
            {
            if (ent is DBText dbText) return dbText.TextString;
            if (ent is MText mText) return mText.Text;
            return string.Empty;
            }

        #endregion
        }
    }