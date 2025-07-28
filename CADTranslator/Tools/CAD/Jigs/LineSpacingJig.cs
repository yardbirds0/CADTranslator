using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System.Collections.Generic;
using System.Linq;

namespace CADTranslator.Tools.CAD.Jigs
    {
    public class LineSpacingJig : DrawJig
        {
        private class TextInfo
            {
            public ObjectId Id { get; set; }
            public Point3d OriginalPosition { get; set; }
            public Entity TextEntity { get; set; }
            public Vector3d ParallelOffset { get; set; } // 【新增】用于记录“队形”的向量
            public bool IsLeftAligned { get; set; } = true;
            public TextHorizontalMode HorizontalMode { get; set; }
            public TextVerticalMode VerticalMode { get; set; }
            }

        private readonly List<TextInfo> _sortedTextInfos;
        private readonly Point3d _dragStartPoint;
        private readonly Vector3d _perpendicularDirection; // “轨道”方向
        private readonly Vector3d _initialLineSpacingVector;
        private Point3d _currentMousePoint;

        public Dictionary<ObjectId, Point3d> FinalPositions { get; } = new Dictionary<ObjectId, Point3d>();

        public LineSpacingJig(Document doc, List<ObjectId> entityIds)
            {
            // --- “侦察”阶段：建立坐标系，记录初始“队形”和“行距” ---
            using (var tr = doc.TransactionManager.StartTransaction())
                {
                var allTextInfos = new List<TextInfo>();
                foreach (var id in entityIds)
                    {
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    if (ent is DBText dbText)
                        {
                        // 【核心修正】记录下DBText的完整对齐信息
                        bool isLeft = dbText.HorizontalMode == TextHorizontalMode.TextLeft && dbText.VerticalMode == TextVerticalMode.TextBase;
                        allTextInfos.Add(new TextInfo
                            {
                            Id = id,
                            OriginalPosition = isLeft ? dbText.Position : dbText.AlignmentPoint,
                            TextEntity = (Entity)dbText.Clone(),
                            IsLeftAligned = isLeft,
                            HorizontalMode = dbText.HorizontalMode,
                            VerticalMode = dbText.VerticalMode
                            });
                        }
                    else if (ent is MText mText)
                        {
                        // MText的处理方式不变
                        allTextInfos.Add(new TextInfo { Id = id, OriginalPosition = mText.Location, TextEntity = (Entity)mText.Clone() });
                        }
                    }

                double dominantRotation = 0;
                if (allTextInfos.Any())
                    {
                    var firstEnt = tr.GetObject(allTextInfos.First().Id, OpenMode.ForRead) as Entity;
                    if (firstEnt is DBText dbText) dominantRotation = dbText.Rotation;
                    else if (firstEnt is MText mText) dominantRotation = mText.Rotation;
                    }

                var wcsToUcs = Matrix3d.Rotation(-dominantRotation, Vector3d.ZAxis, Point3d.Origin);

                _sortedTextInfos = allTextInfos
                    .OrderByDescending(e => e.OriginalPosition.TransformBy(wcsToUcs).Y)
                    .ThenBy(e => e.OriginalPosition.TransformBy(wcsToUcs).X)
                    .ToList();

                // 【核心】定义文字的“平行”和“垂直”方向向量
                Vector3d parallelDirection = Vector3d.XAxis.TransformBy(Matrix3d.Rotation(dominantRotation, Vector3d.ZAxis, Point3d.Origin));
                _perpendicularDirection = Vector3d.YAxis.TransformBy(Matrix3d.Rotation(dominantRotation, Vector3d.ZAxis, Point3d.Origin));

                var anchorPoint = _sortedTextInfos[0].OriginalPosition;

                // 【核心】计算并记录每一行的原始“队形”（缩进）
                foreach (var info in _sortedTextInfos)
                    {
                    Vector3d totalVector = info.OriginalPosition - anchorPoint;
                    info.ParallelOffset = totalVector.GetProjectedVectorOn(parallelDirection);
                    }

                // 【核心】计算初始行距，并设置拖拽起点
                _initialLineSpacingVector = _sortedTextInfos[1].OriginalPosition - _sortedTextInfos[0].OriginalPosition - _sortedTextInfos[1].ParallelOffset;
                _dragStartPoint = _sortedTextInfos[1].OriginalPosition;
                _currentMousePoint = _dragStartPoint;

                tr.Commit();
                }
            }

        protected override SamplerStatus Sampler(JigPrompts prompts)
            {
            var options = new JigPromptPointOptions("\n请沿文字垂直方向拖动鼠标调整行间距:")
                {
                Cursor = CursorType.Crosshair,
                BasePoint = _dragStartPoint, // 以拖拽起点为基点
                UseBasePoint = true
                };

            var result = prompts.AcquirePoint(options);
            if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (_currentMousePoint.IsEqualTo(result.Value)) return SamplerStatus.NoChange;

            _currentMousePoint = result.Value;
            return SamplerStatus.OK;
            }

        protected override bool WorldDraw(WorldDraw draw)
            {
            if (_sortedTextInfos.Count < 2) return true;

            // --- “幻影军团”预览 ---
            FinalPositions.Clear();

            // 1. 计算鼠标在“轨道”上的有效位移
            Vector3d mouseDragVector = _currentMousePoint - _dragStartPoint;
            Vector3d effectiveDragVector = mouseDragVector.GetProjectedVectorOn(_perpendicularDirection);

            // 2. 应用“三倍杠杆”，计算出新的统一“行间距向量”
            Vector3d currentLineSpacingVector = _initialLineSpacingVector + (effectiveDragVector / 3.0);

            // 3. 重新计算所有行的位置
            var anchorInfo = _sortedTextInfos[0];
            FinalPositions[anchorInfo.Id] = anchorInfo.OriginalPosition; // “锚点”（第一行）位置不变

            for (int i = 1; i < _sortedTextInfos.Count; i++)
                {
                var currentInfo = _sortedTextInfos[i];
                // 【核心】新位置 = 锚点位置 + 自己的“队形”偏移 + i倍的“行距”
                FinalPositions[currentInfo.Id] = anchorInfo.OriginalPosition + currentInfo.ParallelOffset + (currentLineSpacingVector * i);
                }

            // 4. 绘制所有“幻影”
            foreach (var info in _sortedTextInfos)
                {
                var clone = info.TextEntity.Clone() as Entity;
                var newPosition = FinalPositions[info.Id];
                clone.TransformBy(Matrix3d.Displacement(newPosition - info.OriginalPosition));
                draw.Geometry.Draw(clone);
                }

            return true;
            }
        }
    }