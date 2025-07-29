// 文件路径: CADTranslator/Services/CAD/UnderlineService.cs
// 【完整文件替换 - 最终修正版】

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

        // 这两个正则表达式现在专供交互式命令(WZXHX)使用
        private readonly Regex _titleMarkers = new Regex(@"(说明|注意|技术要求|参数|示例|NOTES|SPECIFICATION|LEGEND|DESCRIPTION)[\s:：]*$|:$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly Regex _paragraphMarkers = new Regex(@"^\s*(?:[<(（【〔](?:\d+|[a-zA-Z])[>)）】〕]|\d+[、.]|[\u25A0\u25CF\u25B2\u25B6]|[a-zA-Z][、.])");

        public UnderlineService(Document doc)
            {
            _doc = doc;
            _db = doc.Database;
            _editor = doc.Editor;
            }

        #endregion

        #region --- 1. 交互式入口 (供 WZXHX 命令调用) ---

        public void AddUnderlinesToSelectedText(UnderlineOptions options)
            {
            var selRes = _editor.GetSelection();
            if (selRes.Status != PromptStatus.OK) return;

            // 调用为 SelectionSet 设计的重载方法
            ExecuteUnderlining(selRes.Value, options);
            }

        #endregion

        #region --- 2. 自动化入口 (供 WZPB 等命令调用) ---

        public void AddUnderlinesToObjectIds(Dictionary<ObjectId, bool> objectsToUnderline, UnderlineOptions options)
            {
            if (objectsToUnderline == null || !objectsToUnderline.Any()) return;

            // 调用为 Dictionary 设计的重载方法
            ExecuteUnderlining(objectsToUnderline, options);
            }

        #endregion

        #region --- 3. 核心执行逻辑 (重载) ---

        /// <summary>
        /// 【新增重载】为交互式命令 WZXHX 服务。
        /// 它会自己进行标题判断。
        /// </summary>
        private void ExecuteUnderlining(SelectionSet selSet, UnderlineOptions options)
            {
            var objectsToUnderline = new Dictionary<ObjectId, bool>();
            using (var tr = _db.TransactionManager.StartTransaction())
                {
                foreach (SelectedObject selObj in selSet)
                    {
                    var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent is DBText || ent is MText)
                        {
                        string textContent = GetTextFromEntity(ent);
                        bool isTitle = _titleMarkers.IsMatch(textContent.Trim()) && !_paragraphMarkers.IsMatch(textContent.Trim());
                        objectsToUnderline.Add(selObj.ObjectId, isTitle);
                        }
                    }
                tr.Commit();
                }

            // 调用核心实现
            if (objectsToUnderline.Any())
                {
                ExecuteUnderlining(objectsToUnderline, options);
                }
            }

        /// <summary>
        /// 【核心实现】为自动化流程服务。
        /// 它直接使用外部传入的标题判断结果。
        /// </summary>
        private void ExecuteUnderlining(Dictionary<ObjectId, bool> objectsToUnderline, UnderlineOptions options)
            {
            if (objectsToUnderline == null || !objectsToUnderline.Any()) return;

            using (var tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                    EnsureLayerAndLinetype(tr, options.TitleStyle);
                    EnsureLayerAndLinetype(tr, options.DefaultStyle);

                    var nonTitleIds = objectsToUnderline.Where(kvp => !kvp.Value).Select(kvp => kvp.Key).ToList();
                    var nonTitleEntities = nonTitleIds.Select(id => tr.GetObject(id, OpenMode.ForRead) as Entity).Where(e => e != null).ToList();

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
                                    // ▼▼▼ 错误修复点 ▼▼▼
                                    if (extents != null) currentWidth = extents.MaxPoint.X - extents.MinPoint.X;
                                    // ▲▲▲ 错误修复点 ▲▲▲
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
                        var firstEntity = nonTitleEntities.First();
                        if (firstEntity is DBText firstDbText) dominantRotation = firstDbText.Rotation;
                        else if (firstEntity is MText firstMText) dominantRotation = firstMText.Rotation;

                        Matrix3d wcsToUcs = Matrix3d.Rotation(-dominantRotation, Vector3d.ZAxis, Point3d.Origin);
                        foreach (var ent in nonTitleEntities)
                            {
                            Point3d insertionPoint = (ent is DBText dbText) ? dbText.Position : ((MText)ent).Location;
                            Point3d insertionPointUcs = insertionPoint.TransformBy(wcsToUcs);
                            if (insertionPointUcs.X < minUcsX) minUcsX = insertionPointUcs.X;
                            }
                        }

                    int linesUnderlined = 0;
                    foreach (var pair in objectsToUnderline)
                        {
                        var ent = tr.GetObject(pair.Key, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        double rotation = 0, height = 0, unrotatedWidth = 0;
                        Point3d originalInsertionPoint = Point3d.Origin;

                        if (ent is DBText dbText)
                            {
                            rotation = dbText.Rotation;
                            height = dbText.Height;
                            originalInsertionPoint = dbText.Position;
                            }
                        else if (ent is MText mText)
                            {
                            rotation = mText.Rotation;
                            height = mText.TextHeight;
                            originalInsertionPoint = mText.Location;
                            }

                        bool isTitle = pair.Value;
                        UnderlineStyle styleToUse = isTitle ? options.TitleStyle : options.DefaultStyle;

                        double verticalOffset = isTitle ? -(height / 4.0) : options.VerticalOffset;
                        Vector3d offsetVector = new Vector3d(0, verticalOffset, 0);
                        Matrix3d rotationMatrix = Matrix3d.Rotation(rotation, Vector3d.ZAxis, Point3d.Origin);
                        Vector3d rotatedOffset = offsetVector.TransformBy(rotationMatrix);

                        Point3d startPointWcs, endPointWcs;

                        if (isTitle)
                            {
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

        private void EnsureLayerAndLinetype(Transaction tr, UnderlineStyle style)
            {
            var layerTable = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(style.Layer))
                {
                layerTable.UpgradeOpen();
                using (var newLayer = new LayerTableRecord { Name = style.Layer })
                    {
                    layerTable.Add(newLayer);
                    tr.AddNewlyCreatedDBObject(newLayer, true);
                    }
                layerTable.DowngradeOpen();
                }

            var linetypeTable = (LinetypeTable)tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            if (!string.Equals(style.Linetype, "ByLayer", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(style.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase) &&
                !linetypeTable.Has(style.Linetype))
                {
                try { _db.LoadLineTypeFile(style.Linetype, "acad.lin"); }
                catch { _editor.WriteMessage($"\n警告: 无法从 acad.lin 加载线型 '{style.Linetype}'。将使用默认连续线型。"); }
                }
            }

        private string GetTextFromEntity(Entity ent)
            {
            if (ent is DBText dbText) return dbText.TextString;
            if (ent is MText mText) return mText.Text;
            return string.Empty;
            }

        #endregion
        }
    }