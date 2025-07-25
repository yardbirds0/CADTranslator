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

        // 【修改】此类已不再需要，因为我们不再预选候选点
        // private class CandidateSolution { ... }

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
                var bestLayoutMap = bestLayout.ToDictionary(t => t.ObjectId);
                foreach (var originalTask in targets)
                    {
                    if (bestLayoutMap.TryGetValue(originalTask.ObjectId, out var bestResultTask))
                        {
                        // 【核心修正】将计算结果同时赋给三个位置属性
                        originalTask.BestPosition = bestResultTask.BestPosition;
                        originalTask.AlgorithmPosition = bestResultTask.BestPosition; // 算法原始位置
                        originalTask.CurrentUserPosition = bestResultTask.BestPosition; // 用户当前位置（初始值）

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
                // 【核心修改】将 random 对象传递给 FindBestPositionFor
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

        /// <summary>
        /// 【核心重构】为单个文字的单个候选位置进行预评分
        /// </summary>
        private double CalculateSingleTaskScore(LayoutTask task, Point3d candidatePosition, List<LayoutTask> currentLayout, STRtree<ProcessedObstacle> staticObstacleIndex, (double up, double down, double left, double right) isolationFactors)
            {
            // 创建一个临时的任务对象来评估
            var tempTask = new LayoutTask(task) { BestPosition = candidatePosition };

            double distanceCost = tempTask.BestPosition.Value.DistanceTo(tempTask.Bounds.MinPoint);

            var allIndexTasks = currentLayout.Where(t => t.SemanticType.StartsWith("索引")).ToList();

            double semanticScore = CalculateSemanticScore(tempTask, allIndexTasks);

            double avoidanceScore = 0;
            double alignmentScore = 0;
            if (!tempTask.SemanticType.StartsWith("索引") && !tempTask.SemanticType.StartsWith("图名"))
                {
                // 【核心修改】直接使用传入的 isolationFactors
                var scores = CalculateAvoidanceAndAlignmentScores(tempTask, isolationFactors);
                avoidanceScore = scores.avoidance;
                alignmentScore = scores.alignment;
                }

            const double weightDistance = 500.0;

            // 返回总成本
            return (distanceCost * weightDistance) - semanticScore - avoidanceScore - alignmentScore;
            }

        /// <summary>
        /// 【核心重构】现在的总评分只是简单地累加所有任务的最终成本
        /// </summary>
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

                    // 【核心修正】在调用评分函数前，为当前任务计算其孤立因子
                    var isolationFactors = GetIsolationFactors(task, staticObstacleIndex, placedObstacles);

                    // 【核心修正】将计算好的孤立因子作为参数传入
                    totalScore += CalculateSingleTaskScore(task, task.BestPosition.Value, layout, staticObstacleIndex, isolationFactors);
                    }
                }

            if (successfulPlacements == 0) return double.MaxValue;

            // --- 全局性成本与惩罚 ---
            double failurePenalty = (layout.Count - successfulPlacements) * 100000;

            var overallEnvelope = new Extents3d();
            allPlacedBounds.ForEach(b => overallEnvelope.AddExtents(b));
            double dispersionCost = (overallEnvelope.MaxPoint.X - overallEnvelope.MaxPoint.X) * (overallEnvelope.MaxPoint.Y - overallEnvelope.MinPoint.Y);
            const double weightDispersion = 0.1;

            // 最终总分 = 所有任务的最低成本之和 + 全局成本
            return totalScore + failurePenalty + (dispersionCost * weightDispersion);
            }

        /// <summary>
        /// 【核心重构】FindBestPositionFor 现在会使用预评分来选择最佳点
        /// </summary>
        private Point3d? FindBestPositionFor(LayoutTask task, STRtree<ProcessedObstacle> staticIndex, List<ProcessedObstacle> dynamicObstacles, List<LayoutTask> currentLayout, Random random)
            {
            const int eliteCount = 5;

            // --- 1. “智能播种”与“预剪枝” ---
            var safeCandidatePoints = new List<Point3d>();
            var originalBounds = task.Bounds;
            var center = originalBounds.GetCenter();
            var width = originalBounds.Width();
            var height = originalBounds.Height();

            // 定义搜索区域
            double searchHalfWidth = (width / 2.0) * 5;
            double searchHalfHeight = (height / 2.0) * 8;
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

                        // 跳过原文区域
                        if (candidatePoint.X >= originalBounds.MinPoint.X && candidatePoint.X <= originalBounds.MaxPoint.X &&
                            candidatePoint.Y >= originalBounds.MinPoint.Y && candidatePoint.Y <= originalBounds.MaxPoint.Y)
                            {
                            continue;
                            }

                        // 【核心优化：预剪枝】在生成点后，立刻检查碰撞
                        var textPolygon = GeometryConverter.ToNtsPolygon(task.GetBoundsAt(candidatePoint));
                        var collidingObstacle = CheckForGeometricCollision(textPolygon, staticIndex, dynamicObstacles);

                        if (collidingObstacle == null)
                            {
                            // 只有不碰撞的“安全点”，才有资格加入列表
                            safeCandidatePoints.Add(candidatePoint);
                            }
                        else
                            {
                            // （可选）如果需要，可以在这里记录碰撞信息
                            task.CollisionDetails[candidatePoint] = collidingObstacle.SourceId;
                            }
                        }
                    }
                }

            // --- 2. 对“安全点”进行并行评分与决策 ---
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

            return null; // 理论上不会执行到这里
            }

        #endregion

        #region --- 3. 梯度评分计算 (无需修改) ---

        // ... CalculateSemanticScore, CalculateAvoidanceAndAlignmentScores, GetIsolationFactors 等方法保持不变 ...
        private double CalculateSemanticScore(LayoutTask task, List<LayoutTask> allIndexTasks)
            {
            double score = 0;
            // 规则 1.1: 索引奖惩
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
                        score -= 15000; // 惩罚分，所以是减
                    }
                else
                    {
                    if ((isOriginallyAbove && isNowBelow) || (!isOriginallyAbove && !isNowBelow))
                        score += 10000; // 奖励分
                    }
                }
            // 规则 1.2: 图名奖惩
            if (task.SemanticType.StartsWith("图名"))
                {
                if (task.BestPosition.Value.Y >= task.Bounds.MinPoint.Y)
                    score += 8000; // 避让奖励

                // 居中对齐额外奖励
                double xOffset = Math.Abs(task.GetTranslatedBounds().GetCenter().X - task.Bounds.GetCenter().X);
                double width = task.Bounds.Width();
                if (width > 1e-6)
                    {
                    // 偏移越小，奖励越高 (线性衰减)
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

            // --- 避让分 ---
            bool placedUp = newCenter.Y > oldCenter.Y;
            bool placedRight = newCenter.X > oldCenter.X;

            // 规则 2: 垂直避让
            if (placedUp && factors.up > factors.down) avoidanceScore += 8000;
            if (!placedUp && factors.down > factors.up) avoidanceScore += 8000;

            // 规则 3: 水平避让
            if (placedRight && factors.right > factors.left) avoidanceScore += 5000;
            if (!placedRight && factors.left > factors.right) avoidanceScore += 5000;

            // 规则 4 & 5: 复合方向(1.5梯度)奖励
            bool isRightDominant = factors.right > factors.left;
            bool isUpDominant = factors.up > factors.down;
            bool isYAligned = Math.Abs(newCenter.Y - oldCenter.Y) < task.Height * 0.2;

            if (isUpDominant && isRightDominant && placedUp && placedRight) // 右上空旷，且放在右上
                {
                if (isYAligned && placedRight) avoidanceScore += 12000; // 正右方，1.5梯度
                else if (isYAligned && !placedRight) avoidanceScore += 5000; // 正左方，3梯度
                }
            else if (!isUpDominant && !isRightDominant && !placedUp && !placedRight) // 左下空旷
                {
                if (isYAligned && !placedRight) avoidanceScore += 12000;
                else if (isYAligned && placedRight) avoidanceScore += 5000;
                }

            // --- 对齐分 (规则 3 细则) ---
            if (isRightDominant) // 右侧空旷，左对齐优先
                {
                double xOffset = newBounds.MinPoint.X - task.Bounds.MinPoint.X; // 左对齐时为0
                alignmentScore += 5000 * (1.0 - Math.Min(Math.Abs(xOffset) / task.Bounds.Width(), 1.0));
                }
            else // 左侧空旷，右对齐优先
                {
                double xOffset = newBounds.MaxPoint.X - task.Bounds.MaxPoint.X; // 右对齐时为0
                alignmentScore += 5000 * (1.0 - Math.Min(Math.Abs(xOffset) / task.Bounds.Width(), 1.0));
                }

            return (avoidanceScore, alignmentScore);
            }

        #endregion

        #region --- 4. 空间分析辅助方法 (无需修改) ---

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

        #region --- 5. 碰撞检测与候选点生成 (无需修改) ---

        // ... CheckForGeometricCollision 和 GenerateDenseGridPoints 方法保持不变 ...
        private ProcessedObstacle CheckForGeometricCollision(Geometry geometryToCheck, STRtree<ProcessedObstacle> staticIndex, List<ProcessedObstacle> dynamicObstacles)
            {
            var candidateObstacles = staticIndex.Query(geometryToCheck.EnvelopeInternal);
            foreach (var obstacle in candidateObstacles) { if (geometryToCheck.Intersects(obstacle.Geometry)) return obstacle; }
            foreach (var obstacle in dynamicObstacles) { if (geometryToCheck.EnvelopeInternal.Intersects(obstacle.Geometry.EnvelopeInternal)) { if (geometryToCheck.Intersects(obstacle.Geometry)) return obstacle; } }
            return null;
            }

       
        #endregion
        }

    #region --- 扩展方法 (无需修改) ---

    // ... 扩展方法保持不变 ...
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