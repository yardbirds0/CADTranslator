using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Tools.CAD.Jigs;
using CADTranslator.ViewModels;
using NetTopologySuite.Index.Strtree;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using TextBlock = System.Windows.Controls.TextBlock;
using WinPoint = System.Windows.Point;

namespace CADTranslator.Views
    {
    public partial class TestResultWindow : Window
        {
        #region --- 字段与属性 ---

        private readonly Document _doc;
        private readonly List<Tuple<Extents3d, string>> _obstaclesForReport;
        private readonly List<NtsGeometry> _preciseObstacles;
        private readonly string _preciseReport;
        private readonly Dictionary<ObjectId, NtsGeometry> _obstacleIdMap;
        private readonly STRtree<NtsGeometry> _obstacleIndex = new STRtree<NtsGeometry>();
        private readonly TestResultViewModel _viewModel;
        private readonly List<LayoutTask> _originalTargets;
        private readonly List<Entity> _rawObstacles;

        private readonly Dictionary<ObjectId, Size> _textSizeCache = new Dictionary<ObjectId, Size>();
        private readonly Dictionary<ObjectId, Size> _translatedSizeCache = new Dictionary<ObjectId, Size>();



        private Matrix _transformMatrix;
        private bool _isDrawing = false;
        private LayoutTask _draggedTask = null;
        private Path _highlightedObstaclePath = null;
        private readonly List<Path> _highlightedObstaclePaths = new List<Path>();
        private WinPoint _lastMousePosition;
        private Matrix _canvasMatrix;
        private bool _isLoaded = false;
        private bool _canvasInitialized = false;
        private WinPoint _initialMousePosition;
        private Point3d _initialCadAnchorPoint;
        #endregion

        #region --- 构造函数与加载事件 ---

        public TestResultWindow(List<LayoutTask> targets, List<Entity> rawObstacles, List<Tuple<Extents3d, string>> obstaclesForReport, List<NtsGeometry> preciseObstacles, string preciseReport, Dictionary<ObjectId, NtsGeometry> obstacleIdMap, (int rounds, double bestScore, double worstScore) summary)
            {
            InitializeComponent();
            _viewModel = new TestResultViewModel(this, targets, rawObstacles, summary);
            DataContext = _viewModel;

            _doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            _originalTargets = targets;
            _rawObstacles = rawObstacles;
            _obstaclesForReport = obstaclesForReport;
            _preciseReport = preciseReport;
            _preciseObstacles = preciseObstacles;
            _obstacleIdMap = obstacleIdMap;

            foreach (var obstacle in _preciseObstacles)
                {
                _obstacleIndex.Insert(obstacle.EnvelopeInternal, obstacle);
                }

            this.Loaded += (s, e) => TestResultWindow_Loaded(s, e, summary);
            this.SizeChanged += (s, e) => DrawLayout();
            this.ContentRendered += TestResultWindow_ContentRendered;
            }


        private void TestResultWindow_Loaded(object sender, RoutedEventArgs e, (int rounds, double bestScore, double worstScore) summary)
            {
            _canvasMatrix = CanvasTransform.Matrix;
            RoundsSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(Slider_DragCompleted));

            var obstaclesReport = new StringBuilder();

            obstaclesReport.AppendLine($"共分析 {_obstaclesForReport.Count} 个初始障碍物 (基于边界框)：");
            obstaclesReport.AppendLine("========================================");
            _obstaclesForReport.ForEach(obs => obstaclesReport.AppendLine($"--- [类型: {obs.Item2}] Min: {obs.Item1.MinPoint}, Max: {obs.Item1.MaxPoint}"));
            ObstaclesTextBox.Text = obstaclesReport.ToString();
            PreciseObstaclesTextBox.Text = _preciseReport;
            _isLoaded = true;

            PreCacheAllTextSizes();
            _canvasInitialized = false;
            DrawLayout();
            }
        #endregion

        #region --- 核心重算逻辑
        public void ForceRedraw()
            {
            _canvasInitialized = false;
            DrawLayout();
            }
        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
            {
            if (_viewModel.RecalculateLayoutCommand.CanExecute(null))
                {
                _viewModel.RecalculateLayoutCommand.Execute(null);
                }
            }

        #endregion

        #region --- 核心绘图逻辑 ---
        private void DrawLayout()
            {
            if (_isDrawing || PreviewCanvas.ActualWidth == 0) return;
            _isDrawing = true;

            try
                {
                if (!_canvasInitialized)
                    {
                    var worldEnvelope = new NetTopologySuite.Geometries.Envelope();
                    _preciseObstacles.ForEach(g => worldEnvelope.ExpandToInclude(g.EnvelopeInternal));
                    _originalTargets.ForEach(t =>
                    {
                        var b = t.Bounds;
                        worldEnvelope.ExpandToInclude(new NetTopologySuite.Geometries.Coordinate(b.MinPoint.X, b.MinPoint.Y));
                        worldEnvelope.ExpandToInclude(new NetTopologySuite.Geometries.Coordinate(b.MaxPoint.X, b.MaxPoint.Y));
                    });

                    CalculateTransform(worldEnvelope);
                    PreviewCanvas.Children.Clear();

                    foreach (var geometry in _preciseObstacles)
                        {
                        var path = CreateWpfPath(geometry, Brushes.LightGray, Brushes.DarkGray, 0.5);
                        path.Opacity = 0.5;
                        PreviewCanvas.Children.Add(path);
                        }

                    foreach (var task in _originalTargets)
                        {
                        var textBlockWrapper = CreateTextBlockWrapper(task, isTranslated: false);
                        PreviewCanvas.Children.Add(textBlockWrapper);
                        }

                    foreach (var task in _originalTargets.Where(t => t.CurrentUserPosition.HasValue))
                        {
                        var thumbContainer = CreateDraggableThumb(task);
                        PreviewCanvas.Children.Add(thumbContainer);
                        }
                    _canvasInitialized = true;
                    }

                UpdateVisualStyles();
                }
            finally
                {
                _isDrawing = false;
                }
            }
        #endregion

        #region --- 尺寸调整与拖动事件处理 ---

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
            {
            if (sender is Thumb thumb)
                {
                // 尝试从Grid的DataContext获取task
                if (VisualTreeHelper.GetParent(thumb) is Grid containerGrid)
                    {
                    _draggedTask = containerGrid.DataContext as LayoutTask;
                    var a = Canvas.GetLeft(containerGrid);
                    var b = Canvas.GetTop(containerGrid);
                    }

                if (_draggedTask == null) return;

                // 【核心修正】记录拖动开始时的初始状态
                // 1. 记录鼠标相对于整个窗口的初始位置
                _initialMousePosition = Mouse.GetPosition(this);
                // 2. 记录控件的初始CAD锚点 (这是我们的“几何真理”)
                _initialCadAnchorPoint = _draggedTask.CurrentUserPosition.Value;

                ReportListView.SelectedItem = _draggedTask;
                thumb.CaptureMouse();
                e.Handled = true;

                var uiElement = FindUIElementByTask(_draggedTask, isThumb: true);
                if (uiElement != null) Panel.SetZIndex(uiElement, 100);
                }
            }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_draggedTask == null) return;
            var containerGrid = FindUIElementByTask(_draggedTask, isThumb: true) as Grid;
            if (containerGrid == null) return;

            // (步骤 1-5: 定位逻辑保持我们之前修复好的状态，完全不变)
            var currentMousePosition = Mouse.GetPosition(this);
            var screenDelta = Point.Subtract(currentMousePosition, _initialMousePosition);
            var inverseTransform = _transformMatrix;
            inverseTransform.Invert();
            var cadDeltaWpf = inverseTransform.Transform(new Vector(screenDelta.X, screenDelta.Y));
            var cadDelta = new Vector3d(cadDeltaWpf.X, cadDeltaWpf.Y, 0);
            var newCadAnchorPoint = _initialCadAnchorPoint + cadDelta;
            _draggedTask.CurrentUserPosition = newCadAnchorPoint;
            _draggedTask.IsManuallyMoved = true;
            var cadTopLeft = GetCadTopLeftFromCurrentUserPosition(_draggedTask);
            var screenTopLeft = _transformMatrix.Transform(new WinPoint(cadTopLeft.X, cadTopLeft.Y));
            Canvas.SetLeft(containerGrid, screenTopLeft.X);
            Canvas.SetTop(containerGrid, screenTopLeft.Y);

            // (步骤 6: 碰撞检测的核心逻辑保持不变)
            if (!_translatedSizeCache.TryGetValue(_draggedTask.ObjectId, out var size))
            {
                size = new Size(_draggedTask.Bounds.Width(), _draggedTask.Bounds.Height());
            }
            var currentBounds = _draggedTask.GetTranslatedBounds(useUserPosition: true, accurateSize: size);
            var ntsPolygon = Services.CAD.GeometryConverter.ToNtsPolygon(currentBounds);
            var candidates = _obstacleIndex.Query(ntsPolygon.EnvelopeInternal);

            var collidingGeometries = candidates.Where(obs => obs.Intersects(ntsPolygon)).ToList();
            foreach (var otherTask in _originalTargets)
            {
                if (otherTask == _draggedTask || !otherTask.CurrentUserPosition.HasValue) continue;
                var otherBounds = otherTask.GetTranslatedBounds(useUserPosition: true);
                var otherPolygon = Services.CAD.GeometryConverter.ToNtsPolygon(otherBounds);
                if (ntsPolygon.Intersects(otherPolygon))
                {
                    if (_obstacleIdMap.TryGetValue(otherTask.ObjectId, out var collidingGeom))
                    {
                        collidingGeometries.Add(collidingGeom);
                    }
                }
            }

            // 【核心修正 #1】先将上一帧所有高亮的对象，根据我们的“记账本”恢复原色
            foreach (var path in _highlightedObstaclePaths)
            {
                path.Fill = Brushes.LightGray;
                path.Stroke = Brushes.DarkGray;
                path.Opacity = 0.5;
            }
            // 清空“记账本”，为本轮高亮做准备
            _highlightedObstaclePaths.Clear();

            // 【核心修正 #2】根据本次的碰撞结果，决定是高亮新的碰撞体还是设置默认颜色
            if (containerGrid.Children.OfType<Thumb>().FirstOrDefault(t => t.Name == "MoveThumb") is Thumb moveThumb)
            {
                var border = (moveThumb.Template.FindName("ThumbBorder", moveThumb) as Border);
                if (collidingGeometries.Any())
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(180, 255, 99, 71));
                    border.BorderBrush = Brushes.Red;

                    // 遍历所有新碰撞的几何体
                    foreach (var collidingGeom in collidingGeometries)
                    {
                        var collidingPath = FindPathByGeometry(collidingGeom);
                        if (collidingPath != null)
                        {
                            // 标红
                            collidingPath.Fill = new SolidColorBrush(Color.FromArgb(180, 255, 99, 71));
                            collidingPath.Stroke = Brushes.Red;
                            collidingPath.Opacity = 1.0;
                            // 【核心修正 #3】将标红的对象加入到我们的“记账本”中
                            _highlightedObstaclePaths.Add(collidingPath);
                        }
                    }
                }
                else
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(180, 147, 112, 219));
                    border.BorderBrush = Brushes.DarkViolet;
                }
            }
        }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
            {
            if (_draggedTask != null)
                {
                var uiElement = FindUIElementByTask(_draggedTask, isThumb: true);
                if (uiElement != null) Panel.SetZIndex(uiElement, 50);
                }
            _draggedTask = null;
            }

        private void Thumb_Resize_DragDelta(object sender, DragDeltaEventArgs e)
            {
            if (sender is Thumb resizeThumb && VisualTreeHelper.GetParent(resizeThumb) is Grid containerGrid && containerGrid.DataContext is LayoutTask task)
                {
                // 实时改变容器宽度
                double newWidthInWpf = containerGrid.Width + e.HorizontalChange;
                if (newWidthInWpf <= 20) return;
                containerGrid.Width = newWidthInWpf;

                if (containerGrid.Children.OfType<Thumb>().FirstOrDefault(t => t.Name == "MoveThumb") is Thumb moveThumb && moveThumb.Template.FindName("ThumbBorder", moveThumb) is Border border)
                    {
                    if (border.Child is Viewbox viewbox && viewbox.Child is TextBlock textBlock)
                        {
                        // 【核心修正】调用我们新的、纯WPF的换行方法，实现流畅预览
                        string rawText = task.PristineTranslatedText.Replace("\n", " ").Replace("\r", " ");
                        string newWrappedText = GetWpfWrappedText(rawText, newWidthInWpf, textBlock);

                        // 实时更新UI上的文本
                        textBlock.Text = newWrappedText;
                        }
                    }
                }
            }

        private void Thumb_Resize_DragCompleted(object sender, DragCompletedEventArgs e)
            {
            if (sender is Thumb resizeThumb && VisualTreeHelper.GetParent(resizeThumb) is Grid containerGrid && containerGrid.DataContext is LayoutTask task)
                {
                // 步骤 1 & 2: 获取新的CAD宽度 (逻辑不变)
                double finalWpfWidth = containerGrid.Width;
                var inverseTransform = _transformMatrix;
                inverseTransform.Invert();
                var cadWidthVector = inverseTransform.Transform(new Vector(finalWpfWidth, 0));
                double newMaxWidthInCadUnits = cadWidthVector.Length;

                // 步骤 3 & 4: 重新计算文本换行和精确尺寸 (逻辑不变)
                ReflowAndRemeasureTask(task, newMaxWidthInCadUnits);
                Size newAccurateCadSize = _translatedSizeCache[task.ObjectId];

                // 步骤 5: 【核心】坚守原则：不修改锚点，只标记状态
                task.IsManuallyMoved = true;

                // 步骤 6: 使用新尺寸更新UI控件的大小 (逻辑不变)
                double scaleX = _transformMatrix.M11;
                double scaleY = Math.Abs(_transformMatrix.M22);
                containerGrid.Width = newAccurateCadSize.Width * scaleX;
                containerGrid.Height = newAccurateCadSize.Height * scaleY;

                // 步骤 7: 更新UI文本内容 (逻辑不变)
                if (containerGrid.Children.OfType<Thumb>().FirstOrDefault(t => t.Name == "MoveThumb") is Thumb moveThumb && moveThumb.Template.FindName("ThumbBorder", moveThumb) is Border border)
                    {
                    if (border.Child is Viewbox viewbox && viewbox.Child is TextBlock textBlock)
                        {
                        textBlock.Text = task.TranslatedText;
                        }
                    }

                // 步骤 8: 【最终修正的定位逻辑】
                // a. 获取那个绝对不变的几何锚点的屏幕坐标
                var screenAnchorPoint = _transformMatrix.Transform(new WinPoint(task.CurrentUserPosition.Value.X, task.CurrentUserPosition.Value.Y));

                // b. 获取锚点在UI控件内的相对位置 (这个逻辑只对单行正确，但我们只用它来处理第一行)
                var wpfRenderOrigin = GetRenderTransformOriginFromAlignment(task.HorizontalMode, task.VerticalMode);

                // c. 【关键】计算出第一行文字的高度在屏幕上的像素值
                double firstLineHeightInPixels = task.Height * scaleY;

                // d. 计算出UI控件的视觉顶部Y坐标。
                //    这个公式的意义是：从锚点的屏幕Y坐标开始，减去它在第一行内部的相对偏移量。
                //    这样无论总高度如何变化，这个“顶部”的计算结果都是恒定的！
                double newCanvasTop = screenAnchorPoint.Y - (firstLineHeightInPixels * wpfRenderOrigin.Y);

                // e. 使用计算出的正确坐标来定位控件
                Canvas.SetLeft(containerGrid, screenAnchorPoint.X - containerGrid.Width * wpfRenderOrigin.X); // X轴定位不变
                Canvas.SetTop(containerGrid, newCanvasTop);
                }
            }

        private void ReflowAndRemeasureTask(LayoutTask task, double newMaxWidth)
            {
            using (var tr = _doc.TransactionManager.StartTransaction())
                {
                var tokenizer = new Regex(@"[\u4e00-\u9fa5]|([a-zA-Z0-9.-]+)|\s+|[^\s\u4e00-\u9fa5a-zA-Z0-9.-]", RegexOptions.Compiled);

                string rawText = task.PristineTranslatedText.Replace("\n", " ").Replace("\r", " ");
                var tokens = tokenizer.Matches(rawText).Cast<Match>().Select(m => m.Value).ToList();

                // 步骤 1: 【逻辑不变】计算出文本应该被分成哪些行
                var wrappedLines = SmartLayoutJig.GetWrappedLines(tokens, newMaxWidth, newMaxWidth, task.Height, task.WidthFactor, task.TextStyleId);
                string newWrappedText = string.Join("\n", wrappedLines);

                // 更新Task的数据模型
                task.TranslatedText = newWrappedText;
                task.IsManuallyMoved = true;

                // 步骤 2: 【核心修改】调用我们全新的、基于多行模拟的尺寸测量方法
                Size newAccurateSize = GetAccurateMultiLineSize(wrappedLines, task, tr);

                // 步骤 3: 将新的精确尺寸，更新回我们的尺寸缓存中
                _translatedSizeCache[task.ObjectId] = newAccurateSize;

                tr.Commit();
                }
            }

        #endregion

        #region --- UI交互与辅助方法 ---

        private void ReportListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
            if (this.IsLoaded)
                {
                UpdateVisualStyles();
                }
            }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
            {
            if (ReportListView.SelectedItem is LayoutTask selectedTask && selectedTask.IsManuallyMoved)
                {
                selectedTask.CurrentUserPosition = selectedTask.AlgorithmPosition;
                selectedTask.IsManuallyMoved = false;

                // 【修复BUG】重置时，也需要将文本和尺寸恢复到算法的原始状态
                ReflowAndRemeasureTask(selectedTask, selectedTask.Bounds.Width());
                _canvasInitialized = false; // 强制重绘
                DrawLayout();
                UpdateVisualStyles();
                }
            }

        private void DrawSelectionHighlight()
            {
            if (ReportListView.SelectedItem is not LayoutTask selectedTask) return;

            var originalBounds = selectedTask.Bounds;
            var center = originalBounds.GetCenter();
            double searchHalfWidth = (originalBounds.Width() / 2.0) * 5;
            double searchHalfHeight = (originalBounds.Height() / 2.0) * selectedTask.SearchRangeFactor;
            var searchBounds = new Extents3d(
                new Point3d(center.X - searchHalfWidth, center.Y - searchHalfHeight, 0),
                new Point3d(center.X + searchHalfWidth, center.Y + searchHalfHeight, 0)
            );
            var searchRect = CreateRectangle(searchBounds, new SolidColorBrush(Color.FromArgb(20, 100, 100, 100)), Brushes.Gray, 1);
            searchRect.StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 4 };
            Panel.SetZIndex(searchRect, -10);
            PreviewCanvas.Children.Add(searchRect);

            var culpritIds = new HashSet<ObjectId>(selectedTask.CollisionDetails.Values);
            foreach (var culpritId in culpritIds)
                {
                if (_obstacleIdMap.TryGetValue(culpritId, out var culpritGeom))
                    {
                    var culpritPath = FindPathByGeometry(culpritGeom);
                    if (culpritPath != null)
                        {
                        culpritPath.Fill = new SolidColorBrush(Color.FromArgb(100, 255, 0, 0));
                        culpritPath.Stroke = Brushes.Red;
                        culpritPath.Opacity = 1.0;
                        Panel.SetZIndex(culpritPath, 20);
                        }
                    }
                }

            var originalTextBlockWrapper = FindUIElementByTask(selectedTask, isThumb: false);
            if (originalTextBlockWrapper is Viewbox viewbox && viewbox.Child is Border border)
                {
                border.Background = new SolidColorBrush(Color.FromArgb(128, 255, 255, 0));
                }

            var thumb = FindUIElementByTask(selectedTask, isThumb: true);
            if (thumb is Thumb t && t.Template.FindName("ThumbBorder", t) is Border thumbBorder)
                {
                thumbBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Gold, BlurRadius = 15, ShadowDepth = 0, Opacity = 1 };
                }
            }



        private Path FindPathByGeometry(NtsGeometry geometry)
            {
            return PreviewCanvas.Children.OfType<Path>().FirstOrDefault(p =>
            {
                if (p.DataContext is NtsGeometry pathGeometry)
                    {
                    return pathGeometry.Equals(geometry);
                    }
                return false;
            });
            }

        private FrameworkElement FindUIElementByTask(LayoutTask task, bool isThumb)
            {
            // 【修复BUG】isThumb现在代表寻找译文框的容器Grid
            var elementType = isThumb ? typeof(Grid) : typeof(Viewbox);
            return PreviewCanvas.Children.OfType<FrameworkElement>()
                .FirstOrDefault(fe => fe.DataContext == task && fe.GetType() == elementType);
            }
        private FrameworkElement CreateTextBlockWrapper(LayoutTask task, bool isTranslated)
            {
            var textBlock = new TextBlock
                {
                Text = isTranslated ? task.TranslatedText : task.OriginalText,
                Foreground = Brushes.DimGray,
                Opacity = 0.8
                };

            var border = new Border { Child = textBlock, Background = Brushes.Transparent };
            var viewbox = new Viewbox { Child = border, DataContext = task, IsHitTestVisible = false };
            viewbox.RenderTransformOrigin = new Point(0.5, 0.5); // 1. 设置旋转中心为控件中心
            var rotation = -task.Rotation * 180 / Math.PI;       // 2. 获取CAD角度并翻转方向
            viewbox.RenderTransform = new RotateTransform(rotation); // 3. 应用旋转
            // ▼▼▼ 【核心修改】创建原文框时，也使用我们缓存的精确尺寸 ▼▼▼
            if (!_textSizeCache.TryGetValue(task.ObjectId, out var size))
                {
                // 如果缓存中找不到（理论上不会发生），则回退到旧的估算方式
                size = new Size(task.Bounds.Width(), task.Bounds.Height());
                }
            // ▲▲▲ 修改结束 ▲▲▲

            var worldBottomLeft = new WinPoint(task.Bounds.MinPoint.X, task.Bounds.MinPoint.Y);
            var worldTopRight = new WinPoint(task.Bounds.MinPoint.X + size.Width, task.Bounds.MinPoint.Y + size.Height);

            var screenP1 = _transformMatrix.Transform(worldBottomLeft);
            var screenP2 = _transformMatrix.Transform(worldTopRight);

            viewbox.Width = Math.Abs(screenP2.X - screenP1.X);
            viewbox.Height = Math.Abs(screenP2.Y - screenP1.Y);

            var centerPoint = _transformMatrix.Transform(new WinPoint(task.Bounds.GetCenter().X, task.Bounds.GetCenter().Y));
            Canvas.SetLeft(viewbox, centerPoint.X - viewbox.Width / 2);
            Canvas.SetTop(viewbox, centerPoint.Y - viewbox.Height / 2);

            return viewbox;
            }
        private void UpdateVisualStyles()
            {
            var selectedTask = ReportListView.SelectedItem as LayoutTask;

            foreach (var element in PreviewCanvas.Children.OfType<FrameworkElement>())
                {
                if (element.DataContext is not LayoutTask task) continue;

                if (element is Viewbox viewbox && viewbox.Child is Border originalTextBorder)
                    {
                    originalTextBorder.Background = (task == selectedTask)
                        ? new SolidColorBrush(Color.FromArgb(128, 255, 255, 0))
                        : Brushes.Transparent;
                    }
                else if (element is Grid containerGrid && containerGrid.Children.OfType<Thumb>().FirstOrDefault(t => t.Name == "MoveThumb") is Thumb moveThumb)
                    {
                    if (moveThumb.Template.FindName("ThumbBorder", moveThumb) is Border thumbBorder)
                        {
                        var defaultFill = new SolidColorBrush(Color.FromArgb(180, 144, 238, 144));
                        var defaultStroke = Brushes.Green;
                        var fadedFill = new SolidColorBrush(Color.FromArgb(50, 200, 200, 200));
                        var fadedStroke = Brushes.LightGray;
                        var movedFill = new SolidColorBrush(Color.FromArgb(180, 147, 112, 219));
                        var movedStroke = Brushes.DarkViolet;
                        var fadedMovedFill = new SolidColorBrush(Color.FromArgb(80, 147, 112, 219));
                        var fadedMovedStroke = new SolidColorBrush(Color.FromArgb(255, 190, 170, 220));

                        if (selectedTask != null)
                            {
                            if (task == selectedTask)
                                {
                                thumbBorder.Background = task.IsManuallyMoved ? movedFill : defaultFill;
                                thumbBorder.BorderBrush = task.IsManuallyMoved ? movedStroke : defaultStroke;
                                thumbBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Gold, BlurRadius = 15, ShadowDepth = 0, Opacity = 1 };
                                }
                            else
                                {
                                thumbBorder.Background = task.IsManuallyMoved ? fadedMovedFill : fadedFill;
                                thumbBorder.BorderBrush = task.IsManuallyMoved ? fadedMovedStroke : fadedStroke;
                                thumbBorder.Effect = null;
                                }
                            }
                        else
                            {
                            thumbBorder.Background = task.IsManuallyMoved ? movedFill : defaultFill;
                            thumbBorder.BorderBrush = task.IsManuallyMoved ? movedStroke : defaultStroke;
                            thumbBorder.Effect = null;
                            }
                        }
                    }
                }
            }

        #endregion

        #region --- 绘图元素创建 ---

        private FrameworkElement CreateDraggableThumb(LayoutTask task)
            {
            // (创建Grid和Thumb的代码保持不变)
            var containerGrid = new Grid { DataContext = task };
            var moveThumb = new Thumb { DataContext = task, Cursor = Cursors.SizeAll, Name = "MoveThumb" };
            var template = new ControlTemplate(typeof(Thumb));
            var border = new FrameworkElementFactory(typeof(Border), "ThumbBorder");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            var viewbox = new FrameworkElementFactory(typeof(Viewbox));
            var textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetValue(TextBlock.TextProperty, task.TranslatedText);
            textBlock.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            textBlock.SetValue(TextBlock.MarginProperty, new Thickness(2));
            viewbox.AppendChild(textBlock);
            border.AppendChild(viewbox);
            template.VisualTree = border;
            moveThumb.Template = template;
            moveThumb.DragStarted += Thumb_DragStarted;
            moveThumb.DragDelta += Thumb_DragDelta;
            moveThumb.DragCompleted += Thumb_DragCompleted;
            var resizeThumb = new Thumb { DataContext = task, Width = 8, Cursor = Cursors.SizeWE, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.5, Background = Brushes.CornflowerBlue, Name = "ResizeThumb" };
            resizeThumb.DragDelta += Thumb_Resize_DragDelta;
            resizeThumb.DragCompleted += Thumb_Resize_DragCompleted;
            containerGrid.Children.Add(moveThumb);
            containerGrid.Children.Add(resizeThumb);

            // 步骤 8: 获取译文的真实尺寸
            if (!_translatedSizeCache.TryGetValue(task.ObjectId, out var size))
                {
                size = new Size(task.Bounds.Width(), task.Bounds.Height());
                }

            // 步骤 9: 计算WPF控件的尺寸
            var p1_size = _transformMatrix.Transform(new WinPoint(0, 0));
            var p2_size = _transformMatrix.Transform(new WinPoint(size.Width, size.Height));
            containerGrid.Width = Math.Abs(p2_size.X - p1_size.X);
            containerGrid.Height = Math.Abs(p2_size.Y - p1_size.Y);

            // 步骤 10: 【最终修正的定位逻辑】
            // a. 应用旋转变换。旋转中心总是几何中心，这与定位无关，只负责“姿态”。
            containerGrid.RenderTransform = new RotateTransform(-task.Rotation * 180 / Math.PI);
            containerGrid.RenderTransformOrigin = new Point(0.5, 0.5);

            // b. 根据固定的锚点和当前尺寸，计算出视觉左上角的精确CAD坐标
            var cadTopLeft = GetCadTopLeftFromCurrentUserPosition(task);

            // c. 将这个CAD坐标转换为屏幕像素坐标
            var screenTopLeft = _transformMatrix.Transform(new WinPoint(cadTopLeft.X, cadTopLeft.Y));

            // d. 直接将UI控件的左上角“钉”在这个精确的位置上
            Canvas.SetLeft(containerGrid, screenTopLeft.X);
            Canvas.SetTop(containerGrid, screenTopLeft.Y);

            Panel.SetZIndex(containerGrid, 50);
            containerGrid.Visibility = System.Windows.Visibility.Collapsed;
            containerGrid.Visibility = System.Windows.Visibility.Visible;

            return containerGrid;
            }

        private Path CreateWpfPath(NtsGeometry geometry, Brush fill, Brush stroke, double strokeThickness)
            {
            var path = new Path
                {
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Fill = fill,
                DataContext = geometry
                };
            if (geometry.IsSimple && geometry.IsValid)
                {
                if (geometry is NetTopologySuite.Geometries.Polygon polygon)
                    {
                    path.Fill = fill;
                    var figure = new PathFigure { StartPoint = ConvertPoint(polygon.ExteriorRing.Coordinates[0]), IsClosed = true };
                    for (int i = 1; i < polygon.ExteriorRing.Coordinates.Length; i++) { figure.Segments.Add(new System.Windows.Media.LineSegment(ConvertPoint(polygon.ExteriorRing.Coordinates[i]), true)); }
                    path.Data = new PathGeometry(new[] { figure });
                    }
                else if (geometry is NetTopologySuite.Geometries.LineString lineString)
                    {
                    if (lineString.IsClosed) path.Fill = fill;
                    var figure = new PathFigure { StartPoint = ConvertPoint(lineString.Coordinates[0]), IsClosed = lineString.IsClosed };
                    for (int i = 1; i < lineString.Coordinates.Length; i++) { figure.Segments.Add(new System.Windows.Media.LineSegment(ConvertPoint(lineString.Coordinates[i]), true)); }
                    path.Data = new PathGeometry(new[] { figure });
                    }
                }
            return path;
            }

        private Rectangle CreateRectangle(Extents3d bounds, Brush fill, Brush stroke, double strokeThickness)
            {
            var p1 = _transformMatrix.Transform(new Point(bounds.MinPoint.X, bounds.MinPoint.Y));
            var p2 = _transformMatrix.Transform(new Point(bounds.MaxPoint.X, bounds.MaxPoint.Y));
            var rect = new Rectangle { Width = Math.Abs(p2.X - p1.X), Height = Math.Abs(p2.Y - p1.Y), Fill = fill, Stroke = stroke, StrokeThickness = strokeThickness };
            Canvas.SetLeft(rect, Math.Min(p1.X, p2.X));
            Canvas.SetTop(rect, Math.Min(p1.Y, p2.Y));
            return rect;
            }

        private Point ConvertPoint(NetTopologySuite.Geometries.Coordinate coord)
            {
            return _transformMatrix.Transform(new Point(coord.X, coord.Y));
            }

        private void CalculateTransform(NetTopologySuite.Geometries.Envelope worldEnvelope)
            {
            if (worldEnvelope.IsNull || worldEnvelope.Width < 1e-6 || worldEnvelope.Height < 1e-6)
                { _transformMatrix = Matrix.Identity; return; }
            double canvasWidth = PreviewCanvas.ActualWidth * 0.9;
            double canvasHeight = PreviewCanvas.ActualHeight * 0.9;
            var scale = Math.Min(canvasWidth / worldEnvelope.Width, canvasHeight / worldEnvelope.Height);

            var baseTransform = Matrix.Identity;
            baseTransform.Translate(-worldEnvelope.Centre.X, -worldEnvelope.Centre.Y);
            baseTransform.Scale(scale, -scale);
            baseTransform.Translate(PreviewCanvas.ActualWidth / 2, PreviewCanvas.ActualHeight / 2);

            _transformMatrix = baseTransform;
            _transformMatrix.Append(_canvasMatrix);
            }
        #endregion

        #region --- 画布平移与缩放事件 ---

        private void PreviewCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
            {
            var scaleFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            var mousePosition = e.GetPosition(sender as IInputElement);

            _canvasMatrix.ScaleAt(scaleFactor, scaleFactor, mousePosition.X, mousePosition.Y);
            CanvasTransform.Matrix = _canvasMatrix;

            DrawLayout();
            }

        private void CanvasBorder_MouseDown(object sender, MouseButtonEventArgs e)
            {
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed)
                {
                _lastMousePosition = e.GetPosition(sender as IInputElement);
                (sender as UIElement)?.CaptureMouse();
                PreviewCanvas.Cursor = Cursors.ScrollAll;
                }
            else if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
                {
                if (ReportListView.SelectedItem != null)
                    {
                    ReportListView.SelectedItem = null;
                    }
                }
            }

        private void CanvasBorder_MouseUp(object sender, MouseButtonEventArgs e)
            {
            if (e.ChangedButton == MouseButton.Middle)
                {
                (sender as UIElement)?.ReleaseMouseCapture();
                PreviewCanvas.Cursor = Cursors.Arrow;
                }
            }

        private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
            {
            if (e.MiddleButton == MouseButtonState.Pressed && (sender as UIElement).IsMouseCaptured)
                {
                var currentMousePosition = e.GetPosition(sender as IInputElement);
                var delta = Point.Subtract(currentMousePosition, _lastMousePosition);

                _canvasMatrix.Translate(delta.X, delta.Y);
                CanvasTransform.Matrix = _canvasMatrix;

                _lastMousePosition = currentMousePosition;

                DrawLayout();
                }
            }

        private void PreviewCanvas_MouseMiddleButtonDown(object sender, MouseButtonEventArgs e)
            {
            if (e.ButtonState == MouseButtonState.Pressed)
                {
                _lastMousePosition = e.GetPosition(sender as IInputElement);
                (sender as UIElement)?.CaptureMouse();
                PreviewCanvas.Cursor = Cursors.ScrollAll;
                }
            }

        private void PreviewCanvas_MouseMiddleButtonUp(object sender, MouseButtonEventArgs e)
            {
            (sender as UIElement)?.ReleaseMouseCapture();
            PreviewCanvas.Cursor = Cursors.Arrow;
            }

        #endregion

        #region --- "内存CAD渲染" 核心技术 ---
        private void PreCacheAllTextSizes()
            {
            _textSizeCache.Clear();
            _translatedSizeCache.Clear();
            using (var tr = _doc.TransactionManager.StartTransaction())
                {
                foreach (var task in _originalTargets)
                    {
                    // 测量并缓存原文尺寸
                    _textSizeCache[task.ObjectId] = GetAccurateTextSize(task.OriginalText, task, tr);

                    // 【核心修正】实现您的方案第一步：测量完整的、单行的译文的真实尺寸
                    string singleLineTranslatedText = task.PristineTranslatedText.Replace("\n", " ").Replace("\r", " ");
                    _translatedSizeCache[task.ObjectId] = GetAccurateTextSize(singleLineTranslatedText, task, tr);
                    }
                tr.Commit();
                }
            }

        /// <summary>
        /// 【核心新增】使用多个单行DBText来精确模拟和测量多行文本块的最终边界。
        /// </summary>
        /// <param name="lines">要测量的文本行列表</param>
        /// <param name="templateTask">提供样式信息的模板</param>
        /// <param name="tr">活动的事务</param>
        /// <returns>一个包含最大宽度和总高度的Size对象</returns>
        private Size GetAccurateMultiLineSize(List<string> lines, LayoutTask templateTask, Transaction tr)
            {
            if (lines == null || !lines.Any())
                {
                return new Size(0, 0);
                }

            double maxWidth = 0;
            double totalHeight = 0;
            double singleLineHeight = 0;

            // 遍历每一行，分别测量
            foreach (var line in lines)
                {
                // 调用我们原来的单行测量方法来获取每一行的尺寸
                var lineSize = GetAccurateTextSize(line, templateTask, tr);

                // 记录下遇到的最大宽度
                if (lineSize.Width > maxWidth)
                    {
                    maxWidth = lineSize.Width;
                    }

                // 记录下单行的高度（理论上它们应该是一样的）
                if (singleLineHeight == 0)
                    {
                    singleLineHeight = lineSize.Height;
                    }
                }

            // 计算总高度。这里我们用一个简化的行距（1.5倍）来模拟最终布局。
            // 这个值可以在后续优化，但目前已经能很好地反映高度变化。
            if (lines.Count > 0 && singleLineHeight > 0)
                {
                totalHeight = (singleLineHeight * 1.5 * (lines.Count - 1)) + singleLineHeight;
                }

            return new Size(maxWidth, totalHeight);
            }

        /// <summary>
        /// 【职责不变】这个方法现在只负责测量单行文本的精确尺寸。
        /// </summary>
        private Size GetAccurateTextSize(string text, LayoutTask templateTask, Transaction tr)
            {
            if (string.IsNullOrEmpty(text))
                {
                return new Size(0, 0);
                }

            try
                {
                using (var tempText = new DBText())
                    {
                    tempText.TextString = text;
                    tempText.Height = templateTask.Height;
                    tempText.WidthFactor = templateTask.WidthFactor;
                    tempText.TextStyleId = templateTask.TextStyleId;
                    tempText.Oblique = templateTask.Oblique;
                    tempText.HorizontalMode = TextHorizontalMode.TextLeft;
                    tempText.VerticalMode = TextVerticalMode.TextBase;
                    tempText.Position = Point3d.Origin;

                    var extents = tempText.GeometricExtents;
                    if (extents != null)
                        {
                        return new Size(extents.MaxPoint.X - extents.MinPoint.X, extents.MaxPoint.Y - extents.MinPoint.Y);
                        }
                    }
                }
            catch (Exception)
                {
                return new Size(templateTask.Bounds.Width(), templateTask.Bounds.Height());
                }

            return new Size(templateTask.Bounds.Width(), templateTask.Bounds.Height());
            }
        #endregion

        private string GetWpfWrappedText(string text, double maxWidth, TextBlock textBlock)
            {
            var lines = new List<string>();
            var words = text.Split(' ');
            if (!words.Any()) return string.Empty;

            var currentLine = new StringBuilder();

            foreach (var word in words)
                {
                var testLine = currentLine.Length > 0 ? $"{currentLine} {word}" : word;

                // 使用WPF的FormattedText来测量字符串在UI上的渲染宽度
                var formattedText = new FormattedText(
                    testLine,
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, // 【核心修复】使用类型名来限定
                    new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                    textBlock.FontSize,
                    Brushes.Black,
                    new NumberSubstitution(),
                    1.0);

                if (formattedText.WidthIncludingTrailingWhitespace > maxWidth && currentLine.Length > 0)
                    {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLine.Append(word);
                    }
                else
                    {
                    if (currentLine.Length > 0) currentLine.Append(" ");
                    currentLine.Append(word);
                    }
                }
            lines.Add(currentLine.ToString());

            return string.Join("\n", lines);
            }

        private void TestResultWindow_ContentRendered(object sender, EventArgs e)
            {
            // 确保是在所有初始化完成后执行
            if (!_isLoaded || !_canvasInitialized) return;

            // 1. 强制画布和所有子元素重新计算布局
            PreviewCanvas.UpdateLayout();

            // 2. 通知WPF，整个画布的视觉呈现已失效，需要完全重绘
            PreviewCanvas.InvalidateVisual();
            }

        private Point GetRenderTransformOriginFromAlignment(TextHorizontalMode hMode, TextVerticalMode vMode)
            {
            double x = 0.5; // default to center
            double y = 0.5; // default to middle

            // --- 水平对齐 ---
            switch (hMode)
                {
                case TextHorizontalMode.TextLeft:
                    x = 0.0;
                    break;
                case TextHorizontalMode.TextCenter:
                case TextHorizontalMode.TextAlign:
                case TextHorizontalMode.TextMid:
                    x = 0.5;
                    break;
                case TextHorizontalMode.TextRight:
                case TextHorizontalMode.TextFit:
                    x = 1.0;
                    break;
                }

            // --- 垂直对齐 ---
            switch (vMode)
                {
                case TextVerticalMode.TextBase:
                    y = 1.0;
                    break;
                case TextVerticalMode.TextBottom:
                    y = 1.0;
                    break;
                case TextVerticalMode.TextVerticalMid:
                    y = 0.5;
                    break;
                case TextVerticalMode.TextTop:
                    y = 0.0;
                    break;
                }

            return new Point(x, y);
            }
        /// <summary>
        /// 【新增的辅助方法】根据当前的几何锚点、尺寸和对齐方式，计算出文本框视觉左上角的CAD世界坐标。
        /// </summary>
        /// 
        private Point3d GetCadTopLeftFromCurrentUserPosition(LayoutTask task)
            {
            var anchor = task.CurrentUserPosition.Value;
            if (!_translatedSizeCache.TryGetValue(task.ObjectId, out var size))
                {
                size = new Size(task.Bounds.Width(), task.Bounds.Height());
                }

            // 使用整个文本块的最大宽度来计算水平偏移
            double width = size.Width;
            // 【核心修正】只使用第一行文字的固定高度来计算垂直偏移
            double firstLineHeight = task.Height;

            double offsetX = 0;
            double offsetY = 0;

            // --- 水平偏移 (逻辑不变) ---
            switch (task.HorizontalMode)
                {
                case TextHorizontalMode.TextCenter:
                case TextHorizontalMode.TextAlign:
                case TextHorizontalMode.TextMid:
                    offsetX = -width / 2.0;
                    break;
                case TextHorizontalMode.TextRight:
                case TextHorizontalMode.TextFit:
                    offsetX = -width;
                    break;
                }

            // --- 垂直偏移 (使用正确的高度) ---
            switch (task.VerticalMode)
                {
                case TextVerticalMode.TextBase:
                case TextVerticalMode.TextBottom:
                    offsetY = firstLineHeight; // 从第一行底部到顶部，偏移一个行高
                    break;
                case TextVerticalMode.TextVerticalMid:
                    offsetY = firstLineHeight / 2.0; // 从第一行中心到顶部，偏移半个行高
                    break;
                    // 如果是顶部对齐(TextTop)，则无需垂直偏移，offsetY保持为0
                }

            var offsetVector = new Vector3d(offsetX, offsetY, 0);
            // 将偏移向量旋转到与文字相同的角度
            var rotatedOffset = offsetVector.TransformBy(Matrix3d.Rotation(task.Rotation, Vector3d.ZAxis, Point3d.Origin));

            // 最终的左上角位置 = 锚点 + 旋转后的偏移向量
            return anchor + rotatedOffset;
            }

        /// <summary>
        /// 【新增的辅助方法】根据“固定”的左上角位置、新的尺寸和对齐方式，反向计算出新的几何锚点。
        /// </summary>
        private Point3d GetNewAnchorPointFromTopLeft(Point3d topLeft, Size newSize, TextHorizontalMode hMode, TextVerticalMode vMode, double rotation)
            {
            // 这个逻辑与 GetCadTopLeftFromCurrentUserPosition 完全相反
            double newWidth = newSize.Width;
            double newHeight = newSize.Height;

            double offsetX = 0;
            double offsetY = 0;

            // 水平偏移现在使用新的宽度
            switch (hMode)
                {
                case TextHorizontalMode.TextCenter:
                case TextHorizontalMode.TextAlign:
                case TextHorizontalMode.TextMid:
                    offsetX = -newWidth / 2.0;
                    break;
                case TextHorizontalMode.TextRight:
                case TextHorizontalMode.TextFit:
                    offsetX = -newWidth;
                    break;
                }

            // 垂直偏移现在使用新的高度
            switch (vMode)
                {
                case TextVerticalMode.TextBase:
                case TextVerticalMode.TextBottom:
                    offsetY = newHeight;
                    break;
                case TextVerticalMode.TextVerticalMid:
                    offsetY = newHeight / 2.0;
                    break;
                }

            var offsetVector = new Vector3d(offsetX, offsetY, 0);
            var rotatedOffset = offsetVector.TransformBy(Matrix3d.Rotation(rotation, Vector3d.ZAxis, Point3d.Origin));

            // 新的锚点 = 左上角 - 旋转后的偏移向量
            return topLeft - rotatedOffset;
            }

        }

    }

public static class LayoutTaskExtensionsForView
    {
    /// <summary>
    /// 根据给定的精确尺寸，计算任务在特定位置的边界框。
    /// </summary>
    public static Extents3d GetTranslatedBounds(this LayoutTask task, bool useUserPosition = false, Size? accurateSize = null)
        {
        // 步骤 1: 获取正确的几何锚点。
        Point3d? anchorPoint = useUserPosition ? task.CurrentUserPosition : task.AlgorithmPosition;
        if (!anchorPoint.HasValue)
            {
            return task.Bounds;
            }

        // 步骤 2: 确定正确的尺寸。
        double width = accurateSize?.Width ?? task.Bounds.Width();
        double height = accurateSize?.Height ?? task.Bounds.Height(); // 这是整个多行文本框的总高度
        double firstLineHeight = task.Height; // 这是第一行文字的固定高度

        // 步骤 3: 【核心】精确计算从“几何锚点”到“视觉左上角”的偏移量。
        //         此逻辑与我们之前验证过的 GetCadTopLeftFromCurrentUserPosition 方法完全一致。
        double offsetX = 0;
        double offsetY = 0;

        // 根据总宽度计算水平偏移
        switch (task.HorizontalMode)
            {
            case TextHorizontalMode.TextCenter:
            case TextHorizontalMode.TextAlign:
            case TextHorizontalMode.TextMid:
                offsetX = -width / 2.0;
                break;
            case TextHorizontalMode.TextRight:
            case TextHorizontalMode.TextFit:
                offsetX = -width;
                break;
            }

        // 【关键】根据第一行的高度计算垂直偏移
        switch (task.VerticalMode)
            {
            case TextVerticalMode.TextBase:
            case TextVerticalMode.TextBottom:
                offsetY = firstLineHeight;
                break;
            case TextVerticalMode.TextVerticalMid:
                offsetY = firstLineHeight / 2.0;
                break;
            }

        var offsetVector = new Vector3d(offsetX, offsetY, 0);
        // 将偏移向量旋转到与文字相同的角度
        var rotatedOffset = offsetVector.TransformBy(Matrix3d.Rotation(task.Rotation, Vector3d.ZAxis, Point3d.Origin));

        // 步骤 4: 计算出100%正确的“视觉左上角”的CAD坐标。
        Point3d topLeft = anchorPoint.Value + rotatedOffset;

        // 步骤 5: 根据正确的左上角和总尺寸，创建最终的、与视觉匹配的碰撞检测框。
        //         为了生成一个Extents3d（轴对齐边界框），我们需要计算旋转后矩形的四个角点。
        var topRight = topLeft + new Vector3d(width, 0, 0).TransformBy(Matrix3d.Rotation(task.Rotation, Vector3d.ZAxis, Point3d.Origin));
        var bottomLeft = topLeft + new Vector3d(0, -height, 0).TransformBy(Matrix3d.Rotation(task.Rotation, Vector3d.ZAxis, Point3d.Origin));
        var bottomRight = topRight + (bottomLeft - topLeft);

        // Extents3d 会自动计算能包围这四个角点的、最小的、没有旋转的矩形。
        var bounds = new Extents3d();
        bounds.AddPoint(topLeft);
        bounds.AddPoint(topRight);
        bounds.AddPoint(bottomLeft);
        bounds.AddPoint(bottomRight);

        return bounds;
        }
    }
