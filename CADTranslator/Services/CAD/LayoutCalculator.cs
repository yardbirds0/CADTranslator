using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using Point = NetTopologySuite.Geometries.Point;

namespace CADTranslator.Services.CAD
    {
    public class LayoutCalculator
        {
        #region --- 内部辅助类 ---

        private class ProcessedObstacle
            {
            public Geometry Geometry { get; }
            public ObjectId SourceId { get; }
            public ProcessedObstacle(Geometry geometry, ObjectId sourceId)
                {
                Geometry = geometry;
                SourceId = sourceId;
                }
            }

        #endregion

        #region --- 1. 主入口与单轮推演 ---

        public (int rounds, double bestScore, double worstScore) CalculateLayouts(List<LayoutTask> targets, List<Entity> rawObstacles, int numberOfRounds = 100)
            {
            if (!targets.Any()) return (0, 0, 0);

            var staticObstacles = new List<ProcessedObstacle>();
            foreach (var entity in rawObstacles)
                {
                foreach (var geom in GeometryConverter.ToNtsGeometry(entity))
                    {
                    staticObstacles.Add(new ProcessedObstacle(geom, entity.ObjectId));
                    }
                }
            var staticObstacleIndex = new STRtree<ProcessedObstacle>();
            foreach (var obstacle in staticObstacles)
                {
                staticObstacleIndex.Insert(obstacle.Geometry.EnvelopeInternal, obstacle);
                }

            List<LayoutTask> bestLayout = null;
            double bestLayoutScore = double.MaxValue;
            double worstLayoutScore = 0;
            var random = new Random();

            for (int i = 0; i < numberOfRounds; i++)
                {
                var shuffledTargets = targets.OrderBy(t => random.Next()).ToList();
                var currentLayout = RunSingleLayoutRound(shuffledTargets, staticObstacleIndex, random);
                double currentScore = ScoreFullLayout(currentLayout, staticObstacleIndex);

                if (currentScore < double.MaxValue && (worstLayoutScore == 0 || currentScore > worstLayoutScore)) { worstLayoutScore = currentScore; }
                if (currentScore < bestLayoutScore) { bestLayoutScore = currentScore; bestLayout = currentLayout; }
                }

            if (bestLayout != null)
                {
                // 【核心修改】使用绝对唯一的 UniqueId 作为字典的键，而不是不靠谱的 ObjectId
                var bestLayoutMap = bestLayout.ToDictionary(t => t.UniqueId);
                foreach (var originalTask in targets)
                    {
                    // 【核心修改】同样使用 UniqueId 来匹配原始任务和计算后的结果
                    if (bestLayoutMap.TryGetValue(originalTask.UniqueId, out var bestResultTask))
                        {
                        originalTask.BestPosition = bestResultTask.BestPosition;
                        // ▼▼▼ 【核心修正】计算并存储用于UI显示的“左上角”坐标 ▼▼▼
                        if (bestResultTask.BestPosition.HasValue)
                            {
                            var bottomLeft = bestResultTask.BestPosition.Value;
                            var height = originalTask.Bounds.Height(); // 获取原始文本的高度
                            var topLeft = new Point3d(bottomLeft.X, bottomLeft.Y + height, bottomLeft.Z);
                            originalTask.AlgorithmPosition = topLeft;
                            originalTask.CurrentUserPosition = topLeft;
                            }
                        else
                            {
                            originalTask.AlgorithmPosition = null;
                            originalTask.CurrentUserPosition = null;
                            }
                        // ▲▲▲ 修正结束 ▲▲▲

                        originalTask.FailureReason = bestResultTask.FailureReason;
                        originalTask.CollisionDetails = bestResultTask.CollisionDetails;
                        }
                    }
                }
            return (numberOfRounds, bestLayoutScore, worstLayoutScore);
            }

        private List<LayoutTask> RunSingleLayoutRound(List<LayoutTask> targets, STRtree<ProcessedObstacle> staticObstacleIndex, Random random)
            {
            var layoutResult = targets.Select(t => new LayoutTask(t)).ToList();
            var dynamicObstacles = new List<ProcessedObstacle>();

            foreach (var task in layoutResult)
                {
                var bestPosition = FindBestPositionFor(task, staticObstacleIndex, dynamicObstacles, layoutResult, random);
                task.BestPosition = bestPosition;

                if (bestPosition.HasValue)
                    {
                    var newBounds = task.GetTranslatedBounds();
                    var newObstaclePolygon = GeometryConverter.ToNtsPolygon(newBounds);
                    var dynamicObstacle = new ProcessedObstacle(newObstaclePolygon, task.ObjectId);
                    dynamicObstacles.Add(dynamicObstacle);
                    }
                }
            return layoutResult;
            }

        #endregion

        #region --- 2. 评分系统 & 候选点决策 ---

        private double CalculateSingleTaskScore(LayoutTask task, Point3d candidatePosition, List<LayoutTask> currentLayout, STRtree<ProcessedObstacle> staticObstacleIndex, (double up, double down, double left, double right) isolationFactors)
            {
            var tempTask = new LayoutTask(task) { BestPosition = candidatePosition };

            double distanceCost = tempTask.BestPosition.Value.DistanceTo(tempTask.Bounds.MinPoint);

            var allIndexTasks = currentLayout.Where(t => t.SemanticType.StartsWith("索引")).ToList();

            double semanticScore = CalculateSemanticScore(tempTask, allIndexTasks);

            double avoidanceScore = 0;
            double alignmentScore = 0;
            if (!tempTask.SemanticType.StartsWith("索引") && !tempTask.SemanticType.StartsWith("图名"))
                {
                var scores = CalculateAvoidanceAndAlignmentScores(tempTask, isolationFactors);
                avoidanceScore = scores.avoidance;
                alignmentScore = scores.alignment;
                }

            const double weightDistance = 500.0;

            return (distanceCost * weightDistance) - semanticScore - avoidanceScore - alignmentScore;
            }

        private double ScoreFullLayout(List<LayoutTask> layout, STRtree<ProcessedObstacle> staticObstacleIndex)
            {
            if (!layout.Any()) return double.MaxValue;

            double totalScore = 0;
            int successfulPlacements = 0;
            var allPlacedBounds = new List<Extents3d>();
            var placedObstacles = layout.Where(t => t.BestPosition.HasValue)
                                        .Select(t => new ProcessedObstacle(GeometryConverter.ToNtsPolygon(t.GetTranslatedBounds()), t.ObjectId))
                                        .ToList();

            foreach (var task in layout)
                {
                if (task.BestPosition.HasValue)
                    {
                    successfulPlacements++;
                    allPlacedBounds.Add(task.GetTranslatedBounds());

                    var isolationFactors = GetIsolationFactors(task, staticObstacleIndex, placedObstacles);
                    totalScore += CalculateSingleTaskScore(task, task.BestPosition.Value, layout, staticObstacleIndex, isolationFactors);
                    }
                }

            if (successfulPlacements == 0) return double.MaxValue;

            double failurePenalty = (layout.Count - successfulPlacements) * 100000;

            var overallEnvelope = new Extents3d();
            allPlacedBounds.ForEach(b => overallEnvelope.AddExtents(b));
            double dispersionCost = (overallEnvelope.MaxPoint.X - overallEnvelope.MaxPoint.X) * (overallEnvelope.MaxPoint.Y - overallEnvelope.MinPoint.Y);
            const double weightDispersion = 0.1;

            return totalScore + failurePenalty + (dispersionCost * weightDispersion);
            }

        private Point3d? FindBestPositionFor(LayoutTask task, STRtree<ProcessedObstacle> staticIndex, List<ProcessedObstacle> dynamicObstacles, List<LayoutTask> currentLayout, Random random)
            {
            const int eliteCount = 5;

            var safeCandidatePoints = new List<Point3d>();
            var originalBounds = task.Bounds;
            var center = originalBounds.GetCenter();
            var width = originalBounds.Width();
            var height = originalBounds.Height();

            double searchHalfWidth = (width / 2.0) * 5; // 水平搜索范围保持5倍宽度
            double searchHalfHeight = (height / 2.0) * task.SearchRangeFactor; // 使用从任务传入的高度倍数
            var searchAreaMin = new Point3d(center.X - searchHalfWidth, center.Y - searchHalfHeight, 0);
            var searchAreaMax = new Point3d(center.X + searchHalfWidth, center.Y + searchHalfHeight, 0);
            double step = task.Height * 0.5;

            if (step > 1e-6)
                {
                for (double y = searchAreaMin.Y; y <= searchAreaMax.Y; y += step)
                    {
                    for (double x = searchAreaMin.X; x <= searchAreaMax.X; x += step)
                        {
                        var candidatePoint = new Point3d(x, y, 0);

                        if (candidatePoint.X >= originalBounds.MinPoint.X && candidatePoint.X <= originalBounds.MaxPoint.X &&
                            candidatePoint.Y >= originalBounds.MinPoint.Y && candidatePoint.Y <= originalBounds.MaxPoint.Y)
                            {
                            continue;
                            }

                        var textPolygon = GeometryConverter.ToNtsPolygon(task.GetBoundsAt(candidatePoint));
                        var collidingObstacle = CheckForGeometricCollision(textPolygon, staticIndex, dynamicObstacles);

                        if (collidingObstacle == null)
                            {
                            safeCandidatePoints.Add(candidatePoint);
                            }
                        else
                            {
                            task.CollisionDetails[candidatePoint] = collidingObstacle.SourceId;
                            }
                        }
                    }
                }

            if (!safeCandidatePoints.Any())
                {
                task.FailureReason = "在定义的搜索区域内未能找到任何有效的候选位置。";
                return null;
                }

            var isolationFactors = GetIsolationFactors(task, staticIndex, dynamicObstacles);

            var eliteCandidates = safeCandidatePoints
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount - 1)
                .Select(c => new
                    {
                    Position = c,
                    Score = CalculateSingleTaskScore(task, c, currentLayout, staticIndex, isolationFactors)
                    })
                .OrderBy(c => c.Score)
                .Take(eliteCount)
                .ToList();

            if (eliteCandidates.Any())
                {
                int randomIndex = random.Next(eliteCandidates.Count);
                return eliteCandidates[randomIndex].Position;
                }

            return null;
            }

        #endregion

        #region --- 3. 梯度评分计算 ---

        private double CalculateSemanticScore(LayoutTask task, List<LayoutTask> allIndexTasks)
            {
            double score = 0;
            if (task.SemanticType.StartsWith("索引") && task.AssociatedLeader != null)
                {
                var leaderY = task.AssociatedLeader.StartPoint.Y;
                var newPosCenterY = task.GetTranslatedBounds().MinPoint.Y + task.Height / 2;
                bool isOriginallyAbove = task.SemanticType.Contains("上");
                bool isNowBelow = newPosCenterY < leaderY;
                bool hasPartner = allIndexTasks.Any(other => other != task && other.AssociatedLeader?.ObjectId == task.AssociatedLeader.ObjectId);

                if (hasPartner)
                    {
                    if ((isOriginallyAbove && isNowBelow) || (!isOriginallyAbove && !isNowBelow))
                        score -= 15000;
                    }
                else
                    {
                    if ((isOriginallyAbove && isNowBelow) || (!isOriginallyAbove && !isNowBelow))
                        score += 10000;
                    }
                }
            if (task.SemanticType.StartsWith("图名"))
                {
                if (task.BestPosition.Value.Y >= task.Bounds.MinPoint.Y)
                    score += 8000;

                double xOffset = Math.Abs(task.GetTranslatedBounds().GetCenter().X - task.Bounds.GetCenter().X);
                double width = task.Bounds.Width();
                if (width > 1e-6)
                    {
                    score += 7000 * (1.0 - Math.Min(xOffset / (width * 0.5), 1.0));
                    }
                }
            return score;
            }

        private (double avoidance, double alignment) CalculateAvoidanceAndAlignmentScores(LayoutTask task, (double up, double down, double left, double right) factors)
            {
            double avoidanceScore = 0;
            double alignmentScore = 0;
            var newBounds = task.GetTranslatedBounds();
            var newCenter = newBounds.GetCenter();
            var oldCenter = task.Bounds.GetCenter();

            bool placedUp = newCenter.Y > oldCenter.Y;
            bool placedRight = newCenter.X > oldCenter.X;

            if (placedUp && factors.up > factors.down) avoidanceScore += 8000;
            if (!placedUp && factors.down > factors.up) avoidanceScore += 8000;

            if (placedRight && factors.right > factors.left) avoidanceScore += 5000;
            if (!placedRight && factors.left > factors.right) avoidanceScore += 5000;

            bool isRightDominant = factors.right > factors.left;
            bool isUpDominant = factors.up > factors.down;
            bool isYAligned = Math.Abs(newCenter.Y - oldCenter.Y) < task.Height * 0.2;

            if (isUpDominant && isRightDominant && placedUp && placedRight)
                {
                if (isYAligned && placedRight) avoidanceScore += 12000;
                else if (isYAligned && !placedRight) avoidanceScore += 5000;
                }
            else if (!isUpDominant && !isRightDominant && !placedUp && !placedRight)
                {
                if (isYAligned && !placedRight) avoidanceScore += 12000;
                else if (isYAligned && placedRight) avoidanceScore += 5000;
                }

            if (isRightDominant)
                {
                double xOffset = newBounds.MinPoint.X - task.Bounds.MinPoint.X;
                alignmentScore += 5000 * (1.0 - Math.Min(Math.Abs(xOffset) / task.Bounds.Width(), 1.0));
                }
            else
                {
                double xOffset = newBounds.MaxPoint.X - task.Bounds.MaxPoint.X;
                alignmentScore += 5000 * (1.0 - Math.Min(Math.Abs(xOffset) / task.Bounds.Width(), 1.0));
                }

            return (avoidanceScore, alignmentScore);
            }

        #endregion

        #region --- 4. 空间分析辅助方法 ---

        private (double up, double down, double left, double right) GetIsolationFactors(LayoutTask task, STRtree<ProcessedObstacle> staticIndex, List<ProcessedObstacle> dynamicObstacles)
            {
            var allObstacles = staticIndex.Query(staticIndex.Root.Bounds).Cast<ProcessedObstacle>().ToList();
            allObstacles.AddRange(dynamicObstacles);

            var transform = Matrix3d.Rotation(-task.Rotation, Vector3d.ZAxis, task.Bounds.GetCenter());
            var centerInRelative = task.Bounds.GetCenter().TransformBy(transform);

            double searchRadius = task.Height * 5;
            double upScore = 0, downScore = 0, leftScore = 0, rightScore = 0;

            foreach (var obstacle in allObstacles)
                {
                if (obstacle.SourceId == task.ObjectId) continue;

                var obsCenter = obstacle.Geometry.Centroid;
                var obsCenter3d = new Point3d(obsCenter.X, obsCenter.Y, 0);
                var obsCenterRelative = obsCenter3d.TransformBy(transform);

                double dist = obsCenterRelative.DistanceTo(centerInRelative);
                if (dist < searchRadius)
                    {
                    if (obsCenterRelative.Y > centerInRelative.Y) upScore += (searchRadius - dist);
                    else downScore += (searchRadius - dist);

                    if (obsCenterRelative.X > centerInRelative.X) rightScore += (searchRadius - dist);
                    else leftScore += (searchRadius - dist);
                    }
                }

            return (
                1.0 / (1.0 + upScore),
                1.0 / (1.0 + downScore),
                1.0 / (1.0 + leftScore),
                1.0 / (1.0 + rightScore)
            );
            }

        #endregion

        #region --- 5. 碰撞检测与候选点生成 ---

        private ProcessedObstacle CheckForGeometricCollision(Geometry geometryToCheck, STRtree<ProcessedObstacle> staticIndex, List<ProcessedObstacle> dynamicObstacles)
            {
            var candidateObstacles = staticIndex.Query(geometryToCheck.EnvelopeInternal);
            foreach (var obstacle in candidateObstacles)
                {
                if (geometryToCheck.Intersects(obstacle.Geometry)) return obstacle;
                }
            foreach (var obstacle in dynamicObstacles)
                {
                if (geometryToCheck.EnvelopeInternal.Intersects(obstacle.Geometry.EnvelopeInternal))
                    {
                    if (geometryToCheck.Intersects(obstacle.Geometry)) return obstacle;
                    }
                }
            return null;
            }
        #endregion
        }

    #region --- 扩展方法 ---

    public static class LayoutCalculatorExtensions
        {
        public static Point3d GetCenter(this Extents3d extents)
            {
            return new Point3d(
                (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                0);
            }

        public static double Width(this Extents3d extents)
            {
            return extents.MaxPoint.X - extents.MinPoint.X;
            }

        public static double Height(this Extents3d extents)
            {
            return extents.MaxPoint.Y - extents.MinPoint.Y;
            }
        }

    #endregion
    }