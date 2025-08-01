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
                    }

                if (_draggedTask == null) return;

                // 【核心修正】记录拖动开始时的初始状态
                _initialMousePosition = Mouse.GetPosition(this); // 记录鼠标相对于整个窗口的初始位置
                _initialCadAnchorPoint = _draggedTask.CurrentUserPosition.Value; // 记录控件的初始CAD锚点

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

            // ▼▼▼ 【最终修正】修正类型不匹配的编译错误 ▼▼▼

            // 1. 获取鼠标在屏幕上的当前绝对位置 (不变)
            var currentMousePosition = Mouse.GetPosition(this);

            // 2. 计算总的屏幕位移向量 (不变)
            var screenDelta = Point.Subtract(currentMousePosition, _initialMousePosition);

            // 3. 将屏幕位移向量，反向变换回CAD世界坐标系下的位移向量 (不变)
            var inverseTransform = _transformMatrix;
            inverseTransform.Invert();
            var cadDeltaWpf = inverseTransform.Transform(new Vector(screenDelta.X, screenDelta.Y));

            // 4. 【关键修正】将WPF的Vector类型，转换为AutoCAD的Vector3d类型
            var cadDelta = new Vector3d(cadDeltaWpf.X, cadDeltaWpf.Y, 0);

            // 5. 【关键】现在，我们可以安全地将 Point3d 和 Vector3d 相加了
            var newCadAnchorPoint = _initialCadAnchorPoint + cadDelta;
            _draggedTask.CurrentUserPosition = newCadAnchorPoint;
            _draggedTask.IsManuallyMoved = true;

            // 6. 用这个100%正确的CAD锚点，来重新计算并设置UI元素在屏幕上的最终位置 (不变)
            var wpfRenderOrigin = GetRenderTransformOriginFromAlignment(_draggedTask.HorizontalMode, _draggedTask.VerticalMode);
            var screenAnchorPoint = _transformMatrix.Transform(new WinPoint(newCadAnchorPoint.X, newCadAnchorPoint.Y));

            Canvas.SetLeft(containerGrid, screenAnchorPoint.X - containerGrid.Width * wpfRenderOrigin.X);
            Canvas.SetTop(containerGrid, screenAnchorPoint.Y - containerGrid.Height * wpfRenderOrigin.Y);

            // ▲▲▲ 修改结束 ▲▲▲

            // (后续的碰撞检测代码完全保持不变)
            if (!_translatedSizeCache.TryGetValue(_draggedTask.ObjectId, out var size))
                {
                size = new Size(_draggedTask.Bounds.Width(), _draggedTask.Bounds.Height());
                }
            var currentBounds = _draggedTask.GetTranslatedBounds(useUserPosition: true, accurateSize: size);
            var ntsPolygon = Services.CAD.GeometryConverter.ToNtsPolygon(currentBounds);
            var candidates = _obstacleIndex.Query(ntsPolygon.EnvelopeInternal);
            NtsGeometry collidingObstacle = candidates.FirstOrDefault(obs => obs.Intersects(ntsPolygon));
            if (collidingObstacle == null)
                {
                foreach (var otherTask in _originalTargets)
                    {
                    if (otherTask == _draggedTask || !otherTask.CurrentUserPosition.HasValue) continue;
                    var otherBounds = otherTask.GetTranslatedBounds(useUserPosition: true);
                    var otherPolygon = Services.CAD.GeometryConverter.ToNtsPolygon(otherBounds);
                    if (ntsPolygon.Intersects(otherPolygon))
                        {
                        _obstacleIdMap.TryGetValue(otherTask.ObjectId, out collidingObstacle);
                        break;
                        }
                    }
                }
            if (_highlightedObstaclePath != null)
                {
                _highlightedObstaclePath.Fill = Brushes.LightGray;
                _highlightedObstaclePath.Stroke = Brushes.DarkGray;
                _highlightedObstaclePath.Opacity = 0.5;
                _highlightedObstaclePath = null;
                }
            if (containerGrid.Children.OfType<Thumb>().FirstOrDefault(t => t.Name == "MoveThumb") is Thumb moveThumb)
                {
                var border = (moveThumb.Template.FindName("ThumbBorder", moveThumb) as Border);
                if (collidingObstacle != null)
                    {
                    border.Background = new SolidColorBrush(Color.FromArgb(180, 255, 99, 71));
                    border.BorderBrush = Brushes.Red;
                    var collidingPath = FindPathByGeometry(collidingObstacle);
                    if (collidingPath != null)
                        {
                        collidingPath.Fill = new SolidColorBrush(Color.FromArgb(180, 255, 99, 71));
                        collidingPath.Stroke = Brushes.Red;
                        collidingPath.Opacity = 1.0;
                        _highlightedObstaclePath = collidingPath;
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
                // 步骤 1: 获取最终的WPF像素宽度
                double finalWpfWidth = containerGrid.Width;

                // 步骤 2: 创建一个从WPF像素到CAD图形单位的转换矩阵
                var inverseTransform = _transformMatrix;
                inverseTransform.Invert(); // Invert()会修改矩阵自身，现在它可以将WPF点转换为CAD点

                // 步骤 3: 计算出新的最大宽度（in CAD units）
                // 我们通过转换一个宽度向量来获得纯粹的缩放比例，避免位移影响
                var cadWidthVector = inverseTransform.Transform(new Vector(finalWpfWidth, 0));
                double newMaxWidthInCadUnits = cadWidthVector.Length;

                // 步骤 4: 使用正确的CAD单位宽度，调用后台进行文本重排和精确测量
                ReflowAndRemeasureTask(task, newMaxWidthInCadUnits);

                // 步骤 5: 从缓存中获取最新的、精确的CAD尺寸
                Size newAccurateCadSize = _translatedSizeCache[task.ObjectId];

                // 步骤 6: 【核心修复】使用原始的、正向的变换矩阵，将精确的CAD尺寸转换回WPF像素尺寸，并更新UI
                // 注意：我们不再使用已经Invert过的矩阵，而是用原始的_transformMatrix的属性
                double scaleX = _transformMatrix.M11;
                double scaleY = Math.Abs(_transformMatrix.M22); // Y轴是反的，取绝对值

                containerGrid.Width = newAccurateCadSize.Width * scaleX;
                containerGrid.Height = newAccurateCadSize.Height * scaleY;

                // 步骤 7: 更新文本框中的换行，以匹配最终的计算结果
                if (containerGrid.Children.OfType<Thumb>().FirstOrDefault(t => t.Name == "MoveThumb") is Thumb moveThumb && moveThumb.Template.FindName("ThumbBorder", moveThumb) is Border border)
                    {
                    if (border.Child is Viewbox viewbox && viewbox.Child is TextBlock textBlock)
                        {
                        textBlock.Text = task.TranslatedText; // task.TranslatedText 已在ReflowAndRemeasureTask中被更新
                        }
                    }
                // 步骤 8: 获取这个任务在CAD世界中的、100%正确的锚点
                var cadAnchorPoint = task.CurrentUserPosition.Value;

                // 步骤 9: 将这个CAD锚点，转换到WPF的屏幕坐标
                var screenAnchorPoint = _transformMatrix.Transform(new WinPoint(cadAnchorPoint.X, cadAnchorPoint.Y));

                // 步骤 10: 根据CAD的对齐模式，获取WPF中对应的旋转/变换中心
                var wpfRenderOrigin = GetRenderTransformOriginFromAlignment(task.HorizontalMode, task.VerticalMode);

                // 步骤 11: 【关键】根据屏幕锚点、新的控件尺寸、以及旋转中心，重新计算并设置Canvas.Left和Canvas.Top
                Canvas.SetLeft(containerGrid, screenAnchorPoint.X - containerGrid.Width * wpfRenderOrigin.X);
                Canvas.SetTop(containerGrid, screenAnchorPoint.Y - containerGrid.Height * wpfRenderOrigin.Y);

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
            // (前面的1-7步，创建Grid和Thumb的代码保持不变)
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

            // ▼▼▼ 【最终修正】从这里开始，是全新的、使用TransformGroup的定位逻辑 ▼▼▼

            // 8. 获取译文的真实尺寸 (不变)
            if (!_translatedSizeCache.TryGetValue(task.ObjectId, out var size))
                {
                size = new Size(task.Bounds.Width(), task.Bounds.Height());
                }

            // 9. 计算WPF控件的尺寸 (不变)
            var p1_size = _transformMatrix.Transform(new WinPoint(0, 0));
            var p2_size = _transformMatrix.Transform(new WinPoint(size.Width, size.Height));
            containerGrid.Width = Math.Abs(p2_size.X - p1_size.X);
            containerGrid.Height = Math.Abs(p2_size.Y - p1_size.Y);

            // 10. 【关键】创建并设置TransformGroup
            var transformGroup = new TransformGroup();
            // a. 创建平移变换，并给它命名，方便在拖动时找到它
            var translateTransform = new TranslateTransform(0, 0);
            var rotateTransform = new RotateTransform(-task.Rotation * 180 / Math.PI);
            // c. 将旋转和平移都加入到组中
            transformGroup.Children.Add(rotateTransform);
            transformGroup.Children.Add(translateTransform);
            // d. 将整个组应用到控件的RenderTransform
            containerGrid.RenderTransform = transformGroup;

            // 11. 【关键】根据CAD对齐模式，设置WPF的旋转/变换中心 (不变)
            var wpfRenderOrigin = GetRenderTransformOriginFromAlignment(task.HorizontalMode, task.VerticalMode);
            containerGrid.RenderTransformOrigin = wpfRenderOrigin;

            // 12. 【关键】精确定位 (不变)
            var cadAnchorPoint = task.CurrentUserPosition.Value;
            var screenAnchorPoint = _transformMatrix.Transform(new WinPoint(cadAnchorPoint.X, cadAnchorPoint.Y));
            Canvas.SetLeft(containerGrid, screenAnchorPoint.X - containerGrid.Width * wpfRenderOrigin.X);
            Canvas.SetTop(containerGrid, screenAnchorPoint.Y - containerGrid.Height * wpfRenderOrigin.Y);

            // ▲▲▲ 修改结束 ▲▲▲

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

        }
    }



public static class LayoutTaskExtensionsForView
    {
    /// <summary>
    /// 根据给定的精确尺寸，计算任务在特定位置的边界框。
    /// </summary>
    public static Extents3d GetTranslatedBounds(this LayoutTask task, bool useUserPosition = false, Size? accurateSize = null)
        {
        // 步骤 1: 确定定位的基准点。这个点现在代表的是“左上角”
        Point3d? topLeft = useUserPosition ? task.CurrentUserPosition : task.AlgorithmPosition;

        // 如果没有有效的定位点，直接返回原始边界
        if (!topLeft.HasValue) return task.Bounds;

        // 步骤 2: 确定要使用的尺寸。
        // 【核心修正】如果外部没有提供精确尺寸，我们不再错误地使用原文尺寸，
        // 而是也回退使用原文的尺寸，保持行为一致性（虽然这在某些情况下仍不完美，但在拖拽场景下accurateSize总会提供）
        double width = accurateSize?.Width ?? task.Bounds.Width();
        double height = accurateSize?.Height ?? task.Bounds.Height();

        // 步骤 3:【核心修正】根据“左上角”坐标，正确计算出Extents3d所需的“左下角”和“右上角”
        // CAD坐标系中，Y轴向上
        Point3d minPoint = new Point3d(topLeft.Value.X, topLeft.Value.Y - height, topLeft.Value.Z); // 左下角 (Y值变小)
        Point3d maxPoint = new Point3d(topLeft.Value.X + width, topLeft.Value.Y, topLeft.Value.Z);     // 右上角 (Y值不变)

        return new Extents3d(minPoint, maxPoint);
        }
    }