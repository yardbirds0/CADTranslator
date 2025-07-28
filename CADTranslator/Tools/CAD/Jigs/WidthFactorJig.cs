using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CADTranslator.Tools.CAD.Jigs
    {
    public class WidthFactorJig : DrawJig
        {
        private readonly Document _doc;
        private readonly List<ObjectId> _entityIds;
        private readonly Entity _leaderEntity;
        private readonly double _leaderOriginalWidth;
        private readonly double _leaderOriginalWidthFactor;
        private readonly double _rotation;
        private readonly Matrix3d _wcsToUcs;
        private Point3d _currentPoint;

        public double FinalWidthFactor { get; private set; } = 1.0;

        public WidthFactorJig(Document doc, List<ObjectId> entityIds)
            {
            _doc = doc;
            _entityIds = entityIds;

            // --- “侦察”阶段：分析选中的文字，找出“领队” ---
            using (var tr = doc.TransactionManager.StartTransaction())
                {
                double maxWidth = -1;

                foreach (var id in entityIds)
                    {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

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

                    if (currentWidth > maxWidth)
                        {
                        maxWidth = currentWidth;
                        _leaderEntity = ent;
                        }
                    }

                if (_leaderEntity is DBText leaderDbText)
                    {
                    _rotation = leaderDbText.Rotation;
                    _leaderOriginalWidthFactor = leaderDbText.WidthFactor == 0 ? 1.0 : leaderDbText.WidthFactor;
                    _currentPoint = leaderDbText.GeometricExtents.MaxPoint;
                    }
                else if (_leaderEntity is MText leaderMText)
                    {
                    _rotation = leaderMText.Rotation;
                    _leaderOriginalWidthFactor = 1.0; // MText的宽度因子总是1
                    _currentPoint = leaderMText.GeometricExtents.MaxPoint;
                    }

                _leaderOriginalWidth = maxWidth;

                // 创建坐标变换矩阵
                Point3d basePoint = _leaderEntity.GeometricExtents.MinPoint;
                _wcsToUcs = Matrix3d.Rotation(-_rotation, Vector3d.ZAxis, basePoint);

                tr.Commit();
                }
            }

        protected override SamplerStatus Sampler(JigPrompts prompts)
            {
            var options = new JigPromptPointOptions("\n请拖动鼠标调整宽度，点击确认:")
                {
                Cursor = CursorType.Crosshair
                };
            var result = prompts.AcquirePoint(options);
            if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;

            if (_currentPoint.IsEqualTo(result.Value, new Tolerance(1e-4, 1e-4)))
                {
                return SamplerStatus.NoChange;
                }

            _currentPoint = result.Value;

            // --- “翻译官”：将鼠标位置换算为新的WidthFactor ---
            Point3d transformedMousePoint = _currentPoint.TransformBy(_wcsToUcs);
            Point3d transformedBasePoint = _leaderEntity.GeometricExtents.MinPoint.TransformBy(_wcsToUcs);

            double targetWidth = transformedMousePoint.X - transformedBasePoint.X;
            if (targetWidth < 1e-6) targetWidth = 1e-6; // 防止宽度为0或负数

            // 核心公式：新宽度因子 = 目标宽度 / (原始宽度 / 原始宽度因子)
            if (_leaderOriginalWidth > 1e-6)
                {
                FinalWidthFactor = targetWidth / (_leaderOriginalWidth / _leaderOriginalWidthFactor);
                }

            return SamplerStatus.OK;
            }

        protected override bool WorldDraw(WorldDraw draw)
            {
            // --- “幻影军团”预览 ---
            using (var tr = _doc.TransactionManager.StartOpenCloseTransaction())
                {
                foreach (var id in _entityIds)
                    {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    var clone = ent.Clone() as Entity;

                    if (clone is DBText dbText)
                        {
                        dbText.WidthFactor = FinalWidthFactor;
                        }
                    else if (clone is MText mText)
                        {
                        // MText不支持直接修改WidthFactor，但我们可以通过矩阵变换来模拟预览效果
                        var scaleMatrix = Matrix3d.Scaling(FinalWidthFactor, mText.GeometricExtents.MinPoint);
                        clone.TransformBy(scaleMatrix);
                        }

                    draw.Geometry.Draw(clone);
                    }
                }
            return true;
            }
        }
    }