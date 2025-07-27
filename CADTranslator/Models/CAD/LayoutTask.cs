using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CADTranslator.Models.CAD
    {
    public class LayoutTask
        {
        public ObjectId ObjectId { get; }
        public string OriginalText { get; }
        public string TranslatedText { get; set; }
        public Extents3d Bounds { get; private set; }
        public Point3d Position { get; }
        public Point3d AlignmentPoint { get; }
        public double Rotation { get; }
        public double Oblique { get; }
        public double Height { get; }
        public double WidthFactor { get; }
        public ObjectId TextStyleId { get; }
        public TextHorizontalMode HorizontalMode { get; }
        public TextVerticalMode VerticalMode { get; }
        public string SemanticType { get; set; } = "独立文本";
        public double SearchRangeFactor { get; set; } = 8.0;
        public Line AssociatedLeader { get; set; }
        public Point3d? AlgorithmPosition { get; set; }
        public Point3d? CurrentUserPosition { get; set; }
        public bool IsManuallyMoved { get; set; } = false;
        public Point3d? BestPosition { get; set; }
        public string FailureReason { get; set; }
        public Dictionary<Point3d, ObjectId> CollisionDetails { get; set; } = new Dictionary<Point3d, ObjectId>();

        // ◄◄◄ 【核心修正】恢复被遗漏的 SourceObjectIds 属性
        public List<ObjectId> SourceObjectIds { get; private set; } = new List<ObjectId>();


        private LayoutTask(Entity entity, Transaction tr)
            {
            this.ObjectId = entity.ObjectId;
            this.Bounds = entity.GeometricExtents;
            this.SourceObjectIds.Add(entity.ObjectId); // ◄◄◄ 【核心修正】确保在创建时就记录下原始ID

            if (entity is DBText dbText)
                {
                OriginalText = dbText.TextString;
                TranslatedText = dbText.TextString;
                Position = dbText.Position;
                AlignmentPoint = dbText.AlignmentPoint;
                Rotation = dbText.Rotation;
                Oblique = dbText.Oblique;
                Height = dbText.Height;
                TextStyleId = dbText.TextStyleId;
                HorizontalMode = dbText.HorizontalMode;
                VerticalMode = dbText.VerticalMode;

                double widthFactor = dbText.WidthFactor;
                if (widthFactor == 0 && tr != null)
                    {
                    var textStyle = tr.GetObject(dbText.TextStyleId, OpenMode.ForRead) as TextStyleTableRecord;
                    if (textStyle != null) widthFactor = textStyle.XScale;
                    }
                WidthFactor = (widthFactor == 0) ? 1.0 : widthFactor;
                }
            else if (entity is MText mText)
                {
                OriginalText = mText.Text;
                TranslatedText = mText.Text;
                Position = mText.Location;
                AlignmentPoint = mText.Location;
                Rotation = mText.Rotation;
                Oblique = 0;
                Height = mText.TextHeight;
                TextStyleId = mText.TextStyleId;
                WidthFactor = 1.0;
                HorizontalMode = TextHorizontalMode.TextLeft;
                VerticalMode = TextVerticalMode.TextTop;
                }
            }

        public static LayoutTask From(DBText dbText, Transaction tr) => new LayoutTask(dbText, tr);
        public static LayoutTask From(MText mText) => new LayoutTask(mText, null);

        public LayoutTask(LayoutTask template, string mergedText, Extents3d mergedBounds)
            {
            ObjectId = template.ObjectId;
            Position = template.Position;
            AlignmentPoint = template.AlignmentPoint;
            Rotation = template.Rotation;
            Oblique = template.Oblique;
            Height = template.Height;
            WidthFactor = template.WidthFactor;
            TextStyleId = template.TextStyleId;
            HorizontalMode = template.HorizontalMode;
            VerticalMode = template.VerticalMode;
            OriginalText = mergedText;
            TranslatedText = mergedText;
            Bounds = mergedBounds;

            // ◄◄◄ 【核心修正】确保合并后的任务也能继承原始ID
            // 注意：这里的逻辑是基于您现有的 SemanticAnalyzer 中的合并方式，我们只继承模板任务的ID
            // 如果未来要支持合并多个任务的ID，需要改造 SemanticAnalyzer
            SourceObjectIds = new List<ObjectId>(template.SourceObjectIds);
            }

        public LayoutTask(LayoutTask other)
            {
            ObjectId = other.ObjectId;
            OriginalText = other.OriginalText;
            TranslatedText = other.TranslatedText;
            Bounds = other.Bounds;
            Position = other.Position;
            AlignmentPoint = other.AlignmentPoint;
            Rotation = other.Rotation;
            Oblique = other.Oblique;
            Height = other.Height;
            WidthFactor = other.WidthFactor;
            TextStyleId = other.TextStyleId;
            HorizontalMode = other.HorizontalMode;
            VerticalMode = other.VerticalMode;
            SemanticType = other.SemanticType;
            SearchRangeFactor = other.SearchRangeFactor;
            AlgorithmPosition = other.AlgorithmPosition;
            CurrentUserPosition = other.CurrentUserPosition;
            IsManuallyMoved = other.IsManuallyMoved;
            BestPosition = other.BestPosition;
            FailureReason = other.FailureReason;
            CollisionDetails = new Dictionary<Point3d, ObjectId>(other.CollisionDetails);

            // ◄◄◄ 【核心修正】确保在复制任务时，也完整复制ID列表
            SourceObjectIds = new List<ObjectId>(other.SourceObjectIds);
            }

        public override string ToString()
            {
            var sb = new StringBuilder();
            sb.AppendLine($"--- 文字对象 (ID: {ObjectId}) [类型: {SemanticType}] ---");
            sb.AppendLine($"  原文: '{OriginalText}'");
            if (OriginalText != TranslatedText)
                {
                sb.AppendLine($"  译文: '{TranslatedText}'");
                }
            if (BestPosition.HasValue)
                {
                sb.AppendLine($"  [计算结果] 最佳位置: {BestPosition.Value}");
                }
            else
                {
                sb.AppendLine("  [计算结果] 未能找到合适的位置！");
                if (!string.IsNullOrEmpty(FailureReason)) sb.AppendLine($"  [失败原因] {FailureReason}");
                if (CollisionDetails.Any())
                    {
                    var firstCollision = CollisionDetails.First();
                    sb.AppendLine($"  [碰撞详情(示例)]: 候选点 {firstCollision.Key} 与障碍物 (ID: {firstCollision.Value}) 发生碰撞。");
                    }
                }
            return sb.ToString();
            }
        }
    }

public static class LayoutTaskExtensions
        {
        public static Extents3d GetTranslatedBounds(this LayoutTask task)
            {
            if (!task.BestPosition.HasValue) return task.Bounds;
            var width = task.Bounds.MaxPoint.X - task.Bounds.MinPoint.X;
            var height = task.Bounds.MaxPoint.Y - task.Bounds.MinPoint.Y;
            return new Extents3d(
                task.BestPosition.Value,
                new Point3d(task.BestPosition.Value.X + width, task.BestPosition.Value.Y + height, 0)
            );
            }

        public static Extents3d GetBoundsAt(this LayoutTask task, Point3d newPosition)
            {
            var width = task.Bounds.MaxPoint.X - task.Bounds.MinPoint.X;
            var height = task.Bounds.MaxPoint.Y - task.Bounds.MinPoint.Y;
            return new Extents3d(
                newPosition,
                new Point3d(newPosition.X + width, newPosition.Y + height, 0)
            );
            }

        public static double GetLength(this Curve curve)
            {
            if (curve == null) return 0.0;
            try
                {
                return curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
                }
            catch
                {
                if (curve.Bounds.HasValue)
                    {
                    return curve.Bounds.Value.MaxPoint.DistanceTo(curve.Bounds.Value.MinPoint);
                    }
                return 0.0;
                }
            }
        }
    