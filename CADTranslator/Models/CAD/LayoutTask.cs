using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CADTranslator.Models.CAD
    {
    public class LayoutTask
        {
        public ObjectId ObjectId { get; }
        public string OriginalText { get; }
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
        public double SearchRangeFactor { get; set; } = 8.0; // 为每个任务存储搜索范围因子
        public Line AssociatedLeader { get; set; }

        /// <summary>
        /// 算法计算出的原始最佳位置
        /// </summary>
        public Point3d? AlgorithmPosition { get; set; }

        /// <summary>
        /// 用户当前放置的位置（可被拖动修改）
        /// </summary>
        public Point3d? CurrentUserPosition { get; set; }

        /// <summary>
        /// 标记此任务是否被用户手动移动过
        /// </summary>
        public bool IsManuallyMoved { get; set; } = false;

        public Point3d? BestPosition { get; set; }
        public string FailureReason { get; set; }
        public Dictionary<Point3d, ObjectId> CollisionDetails { get; set; } = new Dictionary<Point3d, ObjectId>();

        // 私有主构造函数，用于从CAD实体创建
        private LayoutTask(Entity entity, Transaction tr)
            {
            this.ObjectId = entity.ObjectId;
            this.Bounds = entity.GeometricExtents;

            if (entity is DBText dbText)
                {
                OriginalText = dbText.TextString;
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

        // 公共静态工厂方法，用于安全、无歧义地创建实例
        public static LayoutTask From(DBText dbText, Transaction tr) => new LayoutTask(dbText, tr);
        public static LayoutTask From(MText mText) => new LayoutTask(mText, null);

        // 用于合并的构造函数
        public LayoutTask(LayoutTask template, string mergedText, Extents3d mergedBounds)
            {
            ObjectId = template.ObjectId; // ObjectId暂时只保留第一个的
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
            Bounds = mergedBounds;
            }

        // 用于在多轮推演中创建副本的“复制构造函数”
        public LayoutTask(LayoutTask other)
            {
            ObjectId = other.ObjectId;
            OriginalText = other.OriginalText;
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
            SearchRangeFactor = other.SearchRangeFactor; // 复制搜索范围因子
            //AssociatedLeader = other.AssociatedLeader;
            // 复制新增的交互属性
            AlgorithmPosition = other.AlgorithmPosition;
            CurrentUserPosition = other.CurrentUserPosition;
            IsManuallyMoved = other.IsManuallyMoved;

            BestPosition = other.BestPosition;
            FailureReason = other.FailureReason;
            CollisionDetails = new Dictionary<Point3d, ObjectId>(other.CollisionDetails);
            }

        public override string ToString()
            {
            var sb = new StringBuilder();
            sb.AppendLine($"--- 文字对象 (ID: {ObjectId}) [类型: {SemanticType}] ---");
            sb.AppendLine($"  原文: '{OriginalText}'");
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
    }