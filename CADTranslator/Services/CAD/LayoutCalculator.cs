using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace CADTranslator.Services.CAD
    {
    public class LayoutCalculator
        {
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

        // 【新增】一个内部类，用于存储候选位置及其得分
        private class CandidateSolution
            {
            public Point3d Position { get; }
            public double Score { get; }

            public CandidateSolution(Point3d position, double score)
                {
                Position = position;
                Score = score;
                }
            }

        // 主入口方法 CalculateLayouts 保持不变
        public (int rounds, double bestScore, double worstScore) CalculateLayouts(List<LayoutTask> targets, List<Entity> rawObstacles)
            {
            if (!targets.Any()) return (0, 0, 0);

            var staticObstacles = new List<ProcessedObstacle>();
            foreach (var entity in rawObstacles) { foreach (var geom in GeometryConverter.ToNtsGeometry(entity)) { staticObstacles.Add(new ProcessedObstacle(geom, entity.ObjectId)); } }
            var staticObstacleIndex = new STRtree<ProcessedObstacle>();
            foreach (var obstacle in staticObstacles) { staticObstacleIndex.Insert(obstacle.Geometry.EnvelopeInternal, obstacle); }

            int numberOfRounds =100;
            List<LayoutTask> bestLayout = null;
            double bestLayoutScore = double.MaxValue;
            double worstLayoutScore = 0;
            var random = new Random();

            for (int i = 0; i < numberOfRounds; i++)
                {
                var shuffledTargets = targets.OrderBy(t => random.Next()).ToList();
                var currentLayout = RunSingleLayoutRound(shuffledTargets, staticObstacleIndex, random); // 将random传递下去
                double currentScore = ScoreFullLayout(currentLayout);

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
                        originalTask.BestPosition = bestResultTask.BestPosition;
                        originalTask.FailureReason = bestResultTask.FailureReason;
                        originalTask.CollisionDetails = bestResultTask.CollisionDetails;
                        }
                    }
                }
            return (numberOfRounds, bestLayoutScore, worstLayoutScore);
            }

        // RunSingleLayoutRound 现在接收一个Random实例
        private List<LayoutTask> RunSingleLayoutRound(List<LayoutTask> targets, STRtree<ProcessedObstacle> staticObstacleIndex, Random random)
            {
            var layoutResult = targets.Select(t => new LayoutTask(t)).ToList();
            var dynamicObstacles = new List<ProcessedObstacle>();

            foreach (var task in layoutResult)
                {
                var bestPosition = FindBestPositionFor(task, staticObstacleIndex, dynamicObstacles, random); // 将random传递下去
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
        private double ScoreFullLayout(List<LayoutTask> layout)
            {
            if (!layout.Any()) return double.MaxValue;

            double totalDistance = 0;
            int successfulPlacements = 0;
            var allPlacedBounds = new List<Extents3d>();

            foreach (var task in layout)
                {
                if (task.BestPosition.HasValue)
                    {
                    successfulPlacements++;
                    // A. 计算偏移距离成本
                    totalDistance += task.BestPosition.Value.DistanceTo(task.Bounds.MinPoint);
                    allPlacedBounds.Add(task.GetTranslatedBounds());
                    }
                }

            if (successfulPlacements == 0) return double.MaxValue;

            // 计算平均偏移距离
            double averageDistance = totalDistance / successfulPlacements;

            // C. 计算布局离散度成本
            var overallEnvelope = new Extents3d();
            allPlacedBounds.ForEach(b => overallEnvelope.AddExtents(b));
            double dispersion = (overallEnvelope.MaxPoint.X - overallEnvelope.MinPoint.X) * (overallEnvelope.MaxPoint.Y - overallEnvelope.MinPoint.Y);

            // 失败惩罚：每有一个任务放置失败，就给予一个巨大的惩罚
            double failurePenalty = (layout.Count - successfulPlacements) * 100000; // 极高的惩罚值

            // 定义权重
            double weightDistance = 100.0;
            double weightDispersion = 0.1;

            // 返回最终的全局总成本
            return (averageDistance * weightDistance) + (dispersion * weightDispersion) + failurePenalty;
            }

        // ▼▼▼ 【核心升级 4/4】FindBestPositionFor 和 CheckForGeometricCollision 现在接收动态障碍物列表 ▼▼▼
        private Point3d? FindBestPositionFor(LayoutTask task, STRtree<ProcessedObstacle> staticIndex, List<ProcessedObstacle> dynamicObstacles, Random random)
            {
            int k = 5; // 定义我们要寻找的“精英候选团”的大小
            var topKCandidates = new List<CandidateSolution>();

            var candidatePoints = GenerateDenseGridPoints(task.Bounds, task.Height);

            foreach (var candidate in candidatePoints)
                {
                var textPolygon = GeometryConverter.ToNtsPolygon(task.GetBoundsAt(candidate));
                var collidingObstacle = CheckForGeometricCollision(textPolygon, staticIndex, dynamicObstacles);

                if (collidingObstacle != null)
                    {
                    task.CollisionDetails[candidate] = collidingObstacle.SourceId;
                    continue;
                    }

                double currentScore = candidate.DistanceTo(task.Bounds.MinPoint);

                // --- 维护一个有序的Top-K列表 ---
                if (topKCandidates.Count < k)
                    {
                    topKCandidates.Add(new CandidateSolution(candidate, currentScore));
                    topKCandidates = topKCandidates.OrderBy(c => c.Score).ToList();
                    }
                else if (currentScore < topKCandidates.Last().Score) // 如果比最差的那个还要好
                    {
                    topKCandidates.RemoveAt(k - 1); // 淘汰最差的
                    topKCandidates.Add(new CandidateSolution(candidate, currentScore));
                    topKCandidates = topKCandidates.OrderBy(c => c.Score).ToList();
                    }
                }

            // --- 最终决策 ---
            if (topKCandidates.Any())
                {
                // 【关键】从“精英候选团”中随机选择一个
                int randomIndex = random.Next(topKCandidates.Count);
                return topKCandidates[randomIndex].Position;
                }
            else // 如果一个可行的位置都没找到
                {
                if (task.CollisionDetails.Count == candidatePoints.Count && candidatePoints.Any())
                    {
                    task.FailureReason = $"所有 {candidatePoints.Count} 个候选位置均与现有障碍物发生碰撞。";
                    }
                else
                    {
                    task.FailureReason = "在定义的搜索区域内未能找到任何有效的候选位置。";
                    }
                return null;
                }
            }

        private ProcessedObstacle CheckForGeometricCollision(Geometry geometryToCheck, STRtree<ProcessedObstacle> staticIndex, List<ProcessedObstacle> dynamicObstacles)
            {
            var candidateObstacles = staticIndex.Query(geometryToCheck.EnvelopeInternal);
            foreach (var obstacle in candidateObstacles) { if (geometryToCheck.Intersects(obstacle.Geometry)) return obstacle; }
            foreach (var obstacle in dynamicObstacles) { if (geometryToCheck.EnvelopeInternal.Intersects(obstacle.Geometry.EnvelopeInternal)) { if (geometryToCheck.Intersects(obstacle.Geometry)) return obstacle; } }
            return null;
            }

        private List<Point3d> GenerateDenseGridPoints(Extents3d originalBounds, double textHeight)
            {
            var points = new List<Point3d>();
            var min = originalBounds.MinPoint;
            var max = originalBounds.MaxPoint;
            var width = max.X - min.X;
            var height = max.Y - min.Y;
            double searchMargin = textHeight * 2;
            var searchAreaMin = new Point3d(min.X - searchMargin - width, min.Y - searchMargin - height, 0);
            var searchAreaMax = new Point3d(max.X + searchMargin, max.Y + searchMargin, 0);
            double step = textHeight * 0.5;
            if (step < 1e-6) return points;
            for (double y = searchAreaMin.Y; y <= searchAreaMax.Y; y += step)
                {
                for (double x = searchAreaMin.X; x <= searchAreaMax.X; x += step)
                    {
                    var candidatePoint = new Point3d(x, y, 0);
                    if (candidatePoint.X >= min.X - width && candidatePoint.X <= max.X &&
                        candidatePoint.Y >= min.Y - height && candidatePoint.Y <= max.Y)
                        {
                        continue;
                        }
                    points.Add(candidatePoint);
                    }
                }
            return points;
            }
        }
    }