using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD; // ▼▼▼ 【核心修正】确保这个命名空间被引用！ ▼▼▼
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CADTranslator.Services.CAD
    {
    public class SemanticAnalyzer
        {
        private readonly Editor _editor;
        private readonly List<LayoutTask> _originalTargets;
        private readonly List<Entity> _rawObstacles;
        private readonly Dictionary<LayoutTask, Extents3d> _targetBounds;

        private enum EndpointType { Free, DiagonalConnection, ComplexConnection, Unknown }

        public SemanticAnalyzer(List<LayoutTask> targets, List<Entity> rawObstacles)
            {
            _editor = Application.DocumentManager.MdiActiveDocument.Editor;
            _originalTargets = targets;
            _rawObstacles = rawObstacles;
            _targetBounds = targets.ToDictionary(t => t, t => t.Bounds);
            }

        public List<LayoutTask> AnalyzeAndGroup()
            {
            var finalTasks = new List<LayoutTask>();
            var remainingTasks = new List<LayoutTask>(_originalTargets);

            RecognizeAndGroupDrawingTitles(remainingTasks, finalTasks);
            RecognizeAndGroupLeaders(remainingTasks, finalTasks);

            finalTasks.AddRange(remainingTasks);

            return finalTasks.OrderByDescending(t => t.Bounds.MinPoint.Y)
                             .ThenBy(t => t.Bounds.MinPoint.X)
                             .ToList();
            }

        private void RecognizeAndGroupLeaders(List<LayoutTask> remainingTasks, List<LayoutTask> finalTasks)
            {
            var potentialLeaders = _rawObstacles.OfType<Line>()
                .Where(l => l.Length > 5 && Math.Abs(l.Delta.Y) < l.Length * 0.1)
                .ToList();

            foreach (var leaderCandidate in potentialLeaders)
                {
                var startPointType = AnalyzeEndpoint(leaderCandidate.StartPoint, leaderCandidate.ObjectId);
                var endPointType = AnalyzeEndpoint(leaderCandidate.EndPoint, leaderCandidate.ObjectId);
                bool isIndexLine = (startPointType == EndpointType.Free && endPointType == EndpointType.DiagonalConnection) || (startPointType == EndpointType.DiagonalConnection && endPointType == EndpointType.Free);

                if (isIndexLine)
                    {
                    var leaderBounds = leaderCandidate.GeometricExtents;
                    double searchHeight = _originalTargets.FirstOrDefault()?.Height * 3 ?? 7.5;
                    var textsAbove = FindTextsInZone(leaderBounds, searchHeight, isAbove: true, tasksToSearch: remainingTasks).Where(t => IsAssociatedText(t, leaderBounds)).ToList();
                    var textsBelow = FindTextsInZone(leaderBounds, searchHeight, isAbove: false, tasksToSearch: remainingTasks).Where(t => IsAssociatedText(t, leaderBounds)).ToList();

                    // 【核心修改】在合并任务时，将 leaderCandidate 传入
                    if (textsAbove.Any())
                        {
                        var mergedTask = MergeTasks(textsAbove, "索引(上)", leaderCandidate);
                        finalTasks.Add(mergedTask);
                        textsAbove.ForEach(t => remainingTasks.Remove(t));
                        }
                    if (textsBelow.Any())
                        {
                        var mergedTask = MergeTasks(textsBelow, "索引(下)", leaderCandidate);
                        finalTasks.Add(mergedTask);
                        textsBelow.ForEach(t => remainingTasks.Remove(t));
                        }
                    }
                }
            }

        private void RecognizeAndGroupDrawingTitles(List<LayoutTask> remainingTasks, List<LayoutTask> finalTasks)
            {
            var topLines = _rawObstacles.OfType<Polyline>()
                .Where(p => p.ConstantWidth > 0 && p.Length > 10 && Math.Abs(p.EndPoint.Y - p.StartPoint.Y) < p.Length * 0.1)
                .ToList();

            var bottomLines = _rawObstacles.OfType<Entity>()
                .Where(e => (e is Line || (e is Polyline p && p.ConstantWidth == 0)) && (e as Curve).GetLength() > 10)
                .ToList();

            foreach (var topLine in topLines)
                {
                var topBounds = topLine.GeometricExtents;

                var partnerLine = bottomLines
                    .FirstOrDefault(bottomLine =>
                    {
                        var bottomBounds = bottomLine.GeometricExtents;
                        if (bottomBounds.MinPoint.X > bottomBounds.MaxPoint.X) return false;
                        bool isBelow = topBounds.MinPoint.Y > bottomBounds.MaxPoint.Y && (topBounds.MinPoint.Y - bottomBounds.MaxPoint.Y) < ((topLine.ConstantWidth) + (_originalTargets.FirstOrDefault()?.Height ?? 2.5) * 5);
                        bool isAligned = Math.Abs((topBounds.MinPoint.X + topBounds.MaxPoint.X) / 2 - (bottomBounds.MinPoint.X + bottomBounds.MaxPoint.X) / 2) < 5;
                        double topLength = topLine.GetLength();
                        double bottomLength = (bottomLine as Curve).GetLength();
                        bool isSimilarLength = Math.Abs(topLength - bottomLength) < Math.Max(topLength, bottomLength) * 0.1;
                        return isBelow && isAligned && isSimilarLength;
                    });

                if (partnerLine != null)
                    {
                    var bottomBounds = partnerLine.GeometricExtents;

                    // a. 【规则 #2】认领“图名文字”：只寻找最接近上眉线的单行文字
                    var titleSearchHeight = (topLine.ConstantWidth) + (_originalTargets.FirstOrDefault()?.Height ?? 2.5) * 1.5; // 定义一个狭窄的搜索高度
                    var titleZone = new Extents3d(
                        new Point3d(Math.Min(topBounds.MinPoint.X, bottomBounds.MinPoint.X), topBounds.MaxPoint.Y, 0),
                        new Point3d(Math.Max(topBounds.MaxPoint.X, bottomBounds.MaxPoint.X), topBounds.MaxPoint.Y + titleSearchHeight,0)
                    );
                    var titleText = FindTextsInZone(titleZone, tasksToSearch: remainingTasks)
                                    .OrderBy(t => Math.Abs(t.Bounds.MinPoint.Y - topBounds.MaxPoint.Y))
                                    .FirstOrDefault(); // 只取最接近的那一个

                    // b. 【新增】认领“图名说明文字”
                    var descriptionSearchHeight = (_originalTargets.FirstOrDefault()?.Height ?? 2.5) * 5; // 搜索5倍行高
                    var descriptionTexts = FindTextsInZone(bottomBounds, descriptionSearchHeight, isAbove: false, tasksToSearch: remainingTasks);

                    var nearbyZone = new Extents3d(
                        new Point3d(Math.Max(topBounds.MaxPoint.X, bottomBounds.MaxPoint.X), bottomBounds.MinPoint.Y - 20, 0),
                        new Point3d(Math.Max(topBounds.MaxPoint.X, bottomBounds.MaxPoint.X) + 100, topBounds.MaxPoint.Y + 20, 0)
                    );
                    var nearbyTexts = FindTextsInZone(nearbyZone, tasksToSearch: _originalTargets);
                    // c. 【规则修正】精确定义比例文字：不能包含中文
                    bool hasScaleHint = nearbyTexts.Any(t => !Regex.IsMatch(t.OriginalText, @"[\u4e00-\u9fa5]") && Regex.IsMatch(t.OriginalText, @"\d+:\d+"));
                    bool hasTallTextHint = titleText != null && titleText.Height >= 400;

                    if (titleText != null && (hasScaleHint || hasTallTextHint))
                        {
                        // 图名标题永远是单行，所以直接添加，无需合并
                        finalTasks.Add(titleText);
                        titleText.SemanticType = "图名(标题)"; // 打上标签
                        remainingTasks.Remove(titleText);

                        // 如果有图名说明，则合并它们
                        if (descriptionTexts.Any())
                            {
                            finalTasks.Add(MergeTasks(descriptionTexts, "图名(说明)"));
                            descriptionTexts.ForEach(t => remainingTasks.Remove(t));
                            }
                        }
                    }
                }
            }


        private bool IsAssociatedText(LayoutTask task, Extents3d leaderBounds)
            {
            var taskBounds = _targetBounds[task];
            return (taskBounds.MaxPoint.X - taskBounds.MinPoint.X) < (leaderBounds.MaxPoint.X - leaderBounds.MinPoint.X) * 1.5;
            }

        private EndpointType AnalyzeEndpoint(Point3d point, ObjectId selfId)
            {
            double tolerance = 1e-6;
            var corner1 = new Point3d(point.X - tolerance, point.Y - tolerance, 0);
            var corner2 = new Point3d(point.X + tolerance, point.Y + tolerance, 0);
            var selectionResult = _editor.SelectCrossingWindow(corner1, corner2);
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value.Count <= 1) return EndpointType.Free;
            bool hasDiagonalConnection = false;
            using (var tr = Application.DocumentManager.MdiActiveDocument.Database.TransactionManager.StartTransaction())
                {
                foreach (var id in selectionResult.Value.GetObjectIds())
                    {
                    if (id == selfId) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is Line connectedLine)
                        {
                        if (Math.Abs(connectedLine.Delta.Y) > connectedLine.Length * 0.1 && Math.Abs(connectedLine.Delta.X) > connectedLine.Length * 0.1)
                            {
                            hasDiagonalConnection = true;
                            }
                        else { return EndpointType.ComplexConnection; }
                        }
                    else { return EndpointType.ComplexConnection; }
                    }
                tr.Commit();
                }
            return hasDiagonalConnection ? EndpointType.DiagonalConnection : EndpointType.Unknown;
            }

        private List<LayoutTask> FindTextsInZone(Extents3d zone, List<LayoutTask> tasksToSearch)
            {
            return tasksToSearch.Where(task =>
            {
                var taskCenter = new Point3d((_targetBounds[task].MinPoint.X + _targetBounds[task].MaxPoint.X) / 2, (_targetBounds[task].MinPoint.Y + _targetBounds[task].MaxPoint.Y) / 2, 0);
                return taskCenter.X >= zone.MinPoint.X && taskCenter.X <= zone.MaxPoint.X && taskCenter.Y >= zone.MinPoint.Y && taskCenter.Y <= zone.MaxPoint.Y;
            }).ToList();
            }

        private List<LayoutTask> FindTextsInZone(Extents3d zone, double height, bool isAbove, List<LayoutTask> tasksToSearch)
            {
            double zoneMinX = zone.MinPoint.X;
            double zoneMaxX = zone.MaxPoint.X;
            double zoneY = isAbove ? zone.MaxPoint.Y : zone.MinPoint.Y;
            return tasksToSearch.Where(task =>
            {
                var taskBounds = _targetBounds[task];
                bool horizontalOverlap = taskBounds.MinPoint.X < zoneMaxX && taskBounds.MaxPoint.X > zoneMinX;
                bool verticalProximity = isAbove ? taskBounds.MinPoint.Y >= zoneY && taskBounds.MinPoint.Y < zoneY + height : taskBounds.MaxPoint.Y <= zoneY && taskBounds.MaxPoint.Y > zoneY - height;
                return horizontalOverlap && verticalProximity;
            }).ToList();
            }

        private LayoutTask MergeTasks(List<LayoutTask> tasks, string groupType, Line leaderLine = null)
            {
            if (tasks == null || !tasks.Any()) return null;
            var sortedTasks = tasks.OrderByDescending(t => _targetBounds[t].MinPoint.Y).ThenBy(t => _targetBounds[t].MinPoint.X).ToList();
            var mergedText = new StringBuilder();
            foreach (var task in sortedTasks) { mergedText.Append(task.OriginalText.Trim() + " "); }
            var mergedBounds = new Extents3d();
            foreach (var task in sortedTasks) { mergedBounds.AddExtents(_targetBounds[task]); }
            var templateTask = sortedTasks.First();
            var mergedTask = new LayoutTask(templateTask, mergedText.ToString().Trim(), mergedBounds);
            mergedTask.SemanticType = groupType;
            // 【核心修改】保存关联的索引线
            mergedTask.AssociatedLeader = leaderLine;
            return mergedTask;
            }
        }
    }