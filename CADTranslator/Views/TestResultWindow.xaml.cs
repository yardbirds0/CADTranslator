using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Tools.CAD.Jigs;
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
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using TextBlock = System.Windows.Controls.TextBlock;
using WinPoint = System.Windows.Point;

namespace CADTranslator.Views
    {
    public partial class TestResultWindow : Window, INotifyPropertyChanged
        {
        #region --- 字段与属性 ---

        private readonly Document _doc;
        private readonly List<LayoutTask> _originalTargets;
        private readonly List<Entity> _rawObstacles;
        private readonly List<Tuple<Extents3d, string>> _obstaclesForReport;
        private readonly List<NtsGeometry> _preciseObstacles;
        private readonly string _preciseReport;
        private readonly Dictionary<ObjectId, NtsGeometry> _obstacleIdMap;
        private readonly STRtree<NtsGeometry> _obstacleIndex = new STRtree<NtsGeometry>();

        private readonly Dictionary<ObjectId, Size> _textSizeCache = new Dictionary<ObjectId, Size>();
        private readonly Dictionary<ObjectId, Size> _translatedSizeCache = new Dictionary<ObjectId, Size>();

        private readonly SettingsService _settingsService = new SettingsService();
        private AppSettings _settings;

        private Matrix _transformMatrix;
        private bool _isDrawing = false;
        private bool _isRecalculating = false;
        private LayoutTask _draggedTask = null;
        private Path _highlightedObstaclePath = null;
        private int _numberOfRounds;
        private double _currentSearchRangeFactor;
        private WinPoint _lastMousePosition;
        private Matrix _canvasMatrix;
        private bool _isLoaded = false;
        private bool _canvasInitialized = false;

        public ObservableCollection<int> RoundOptions { get; set; }
        public ObservableCollection<double> SearchRangeOptions { get; set; }

        public int NumberOfRounds
            {
            get => _numberOfRounds;
            set { if (SetField(ref _numberOfRounds, value)) { if (_isLoaded) { SaveSettings(); } } }
            }
        public double CurrentSearchRangeFactor
            {
            get => _currentSearchRangeFactor;
            set
                {
                if (SetField(ref _currentSearchRangeFactor, value))
                    {
                    SaveSettings();
                    foreach (var task in _originalTargets)
                        {
                        task.SearchRangeFactor = _currentSearchRangeFactor;
                        }
                    }
                }
            }
        #endregion

        #region --- 构造函数与加载事件 ---

        public TestResultWindow(List<LayoutTask> targets, List<Entity> rawObstacles, List<Tuple<Extents3d, string>> obstaclesForReport, List<NtsGeometry> preciseObstacles, string preciseReport, Dictionary<ObjectId, NtsGeometry> obstacleIdMap, (int rounds, double bestScore, double worstScore) summary)
            {
            InitializeComponent();
            DataContext = this;
            _doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            // 【修复BUG】在构造函数中，立刻将传入的译文，保存到我们新的“原始译文”属性中
            _originalTargets = targets.Select(t =>
            {
                var newTask = new LayoutTask(t);
                newTask.PristineTranslatedText = newTask.TranslatedText; // 保存原始译文
                return newTask;
            }).ToList();

            _settings = _settingsService.LoadSettings();
            NumberOfRounds = _settings.TestNumberOfRounds;
            CurrentSearchRangeFactor = _settings.TestSearchRangeFactor;

            _rawObstacles = rawObstacles;
            _obstaclesForReport = obstaclesForReport;
            _preciseReport = preciseReport;
            _preciseObstacles = preciseObstacles;
            _obstacleIdMap = obstacleIdMap;

            SearchRangeOptions = new ObservableCollection<double> { 5.0, 8.0, 10.0, 15.0, 20.0 };
            if (!SearchRangeOptions.Contains(CurrentSearchRangeFactor))
                {
                SearchRangeOptions.Add(CurrentSearchRangeFactor);
                }
            foreach (var task in _originalTargets)
                {
                task.SearchRangeFactor = CurrentSearchRangeFactor;
                }
            foreach (var obstacle in _preciseObstacles)
                {
                _obstacleIndex.Insert(obstacle.EnvelopeInternal, obstacle);
                }

            RoundOptions = new ObservableCollection<int> { 10, 50, 100, 200, 500, 1000 };
            if (NumberOfRounds < RoundsSlider.Minimum)
                {
                NumberOfRounds = (int)RoundsSlider.Minimum;
                }

            this.Loaded += (s, e) => TestResultWindow_Loaded(s, e, summary);
            this.SizeChanged += (s, e) => DrawLayout();
            }

        private void TestResultWindow_Loaded(object sender, RoutedEventArgs e, (int rounds, double bestScore, double worstScore) summary)
            {
            _canvasMatrix = CanvasTransform.Matrix;
            RoundsSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(Slider_DragCompleted));
            RoundsComboBox.LostFocus += RoundsComboBox_LostFocus;
            SearchRangeComboBox.LostFocus += SearchRangeComboBox_LostFocus;
            UpdateSummary(summary);
            RoundsComboBox.Text = this.NumberOfRounds.ToString();

            var obstaclesReport = new StringBuilder();
            obstaclesReport.AppendLine($"共分析 {_obstaclesForReport.Count} 个初始障碍物 (基于边界框)：");
            obstaclesReport.AppendLine("========================================");
            _obstaclesForReport.ForEach(obs => obstaclesReport.AppendLine($"--- [类型: {obs.Item2}] Min: {obs.Item1.MinPoint}, Max: {obs.Item1.MaxPoint}"));
            ObstaclesTextBox.Text = obstaclesReport.ToString();
            PreciseObstaclesTextBox.Text = _preciseReport;

            ReportListView.ItemsSource = _originalTargets;
            RoundsSlider.Value = this.NumberOfRounds;
            RoundsComboBox.Text = this.NumberOfRounds.ToString();
            _isLoaded = true;

            PreCacheAllTextSizes();
            _canvasInitialized = false;
            DrawLayout();
            }
        #endregion

        #region --- 核心重算逻辑
        private async void RecalculateAndRedraw()
            {
            if (!this.IsLoaded || _isRecalculating) return;

            _canvasInitialized = false; // 强制重绘
            DrawLayout();
            _isRecalculating = false;
            SummaryTextBlock.Text = $"正在使用 {NumberOfRounds} 轮次进行新一轮推演，请稍候...";

            var newSummary = await Task.Run(() =>
            {
                foreach (var task in _originalTargets)
                    {
                    task.BestPosition = null;
                    task.AlgorithmPosition = null;
                    task.CurrentUserPosition = null;
                    task.IsManuallyMoved = false;
                    task.FailureReason = null;
                    task.CollisionDetails.Clear();
                    }

                var calculator = new LayoutCalculator();
                return calculator.CalculateLayouts(_originalTargets, _rawObstacles, NumberOfRounds);
            });

            UpdateSummary(newSummary);
            ReportListView.ItemsSource = null;
            ReportListView.ItemsSource = _originalTargets;
            DrawLayout();

            _isRecalculating = false;
            }

        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
            {
            RecalculateAndRedraw();
            }

        private void UpdateSummary((int rounds, double bestScore, double worstScore) summary)
            {
            var summaryText = new StringBuilder();
            summaryText.AppendLine($"总推演轮次: {summary.rounds} 轮");
            summaryText.AppendLine($"最佳布局评分: {summary.bestScore:F2}");
            summaryText.AppendLine($"最差布局评分: {summary.worstScore:F2}");
            SummaryTextBlock.Text = summaryText.ToString();
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
            if (sender is Thumb thumb && thumb.DataContext is LayoutTask task)
                {
                // 【修复BUG】确保从容器Grid上获取DataContext
                if (VisualTreeHelper.GetParent(thumb) is Grid containerGrid)
                    {
                    task = containerGrid.DataContext as LayoutTask;
                    }
                if (task == null) return;

                _draggedTask = task;
                ReportListView.SelectedItem = task;
                thumb.CaptureMouse();
                e.Handled = true;

                var uiElement = FindUIElementByTask(task, isThumb: true);
                if (uiElement != null) Panel.SetZIndex(uiElement, 100);
                }
            }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
            {
            if (_draggedTask == null) return;
            var containerGrid = FindUIElementByTask(_draggedTask, isThumb: true) as Grid;
            if (containerGrid == null) return;

            var newLeft = Canvas.GetLeft(containerGrid) + e.HorizontalChange;
            var newTop = Canvas.GetTop(containerGrid) + e.VerticalChange;
            Canvas.SetLeft(containerGrid, newLeft);
            Canvas.SetTop(containerGrid, newTop);

            var inverseTransform = _transformMatrix;
            inverseTransform.Invert();
            var newWorldTopLeft = inverseTransform.Transform(new WinPoint(newLeft, newTop));

            if (!_translatedSizeCache.TryGetValue(_draggedTask.ObjectId, out var size))
                {
                size = new Size(_draggedTask.Bounds.Width(), _draggedTask.Bounds.Height());
                }

            _draggedTask.CurrentUserPosition = new Point3d(newWorldTopLeft.X, newWorldTopLeft.Y - size.Height, 0);
            _draggedTask.IsManuallyMoved = true;

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
                task.IsManuallyMoved = true;
                string finalWpfText = "";

                // 步骤 3.1: 获取WPF预览最终确定的文本内容
                if (containerGrid.Children.OfType<Thumb>().FirstOrDefault(t => t.Name == "MoveThumb") is Thumb moveThumb && moveThumb.Template.FindName("ThumbBorder", moveThumb) is Border border)
                    {
                    if (border.Child is Viewbox viewbox && viewbox.Child is TextBlock textBlock)
                        {
                        finalWpfText = textBlock.Text;
                        }
                    }

                if (string.IsNullOrEmpty(finalWpfText)) return;

                // 步骤 3.2: 将最终文本按行分割
                var finalLines = finalWpfText.Split('\n').ToList();
                task.TranslatedText = finalWpfText; // 更新数据模型

                // 步骤 3.3: 在CAD后台，为每一行都生成一次“影子文本”，并获取它们的尺寸
                using (var tr = _doc.TransactionManager.StartTransaction())
                    {
                    Size newAccurateSize = GetAccurateMultiLineSize(finalLines, task, tr);
                    _translatedSizeCache[task.ObjectId] = newAccurateSize; // 更新尺寸缓存
                    tr.Commit();
                    }

                // 步骤 4: 在WPF界面进行最后一次更新，刷新文本框的最终精确大小
                var inverseTransform = _transformMatrix;
                inverseTransform.Invert();
                double scaleX = inverseTransform.M11;
                double scaleY = Math.Abs(inverseTransform.M22); // Y轴是反的，取绝对值

                containerGrid.Width = _translatedSizeCache[task.ObjectId].Width * scaleX;
                containerGrid.Height = _translatedSizeCache[task.ObjectId].Height * scaleY;
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
                var wrappedLines = SmartLayoutJig.GetWrappedLines(tokens, newMaxWidth, task.Height, task.WidthFactor, task.TextStyleId);
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

            Canvas.SetLeft(viewbox, Math.Min(screenP1.X, screenP2.X));
            Canvas.SetTop(viewbox, Math.Min(screenP1.Y, screenP2.Y));
            Panel.SetZIndex(viewbox, 20);

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
            // 1. 创建一个新的Grid作为所有UI元素的容器
            var containerGrid = new Grid { DataContext = task };

            // 2. 创建用于移动的主体Thumb (原来的Thumb)
            var moveThumb = new Thumb
                {
                DataContext = task,
                Cursor = Cursors.SizeAll,
                Name = "MoveThumb" // 命名，用于样式和事件
                };

            // --- 为 moveThumb 设置模板 (这部分与您原来的代码几乎一样) ---
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
            // -----------------------------------------------------------------

            // 3. 关联移动事件
            moveThumb.DragStarted += Thumb_DragStarted;
            moveThumb.DragDelta += Thumb_DragDelta;
            moveThumb.DragCompleted += Thumb_DragCompleted;

            // 4. 创建用于调整大小的Thumb (这是一个新增的“手柄”)
            var resizeThumb = new Thumb
                {
                DataContext = task,
                Width = 8, // 手柄宽度
                Cursor = Cursors.SizeWE, // 左右拉伸鼠标样式
                HorizontalAlignment = HorizontalAlignment.Right, // 靠右对齐
                Opacity = 0.5, // 半透明
                Background = Brushes.CornflowerBlue,
                Name = "ResizeThumb"
                };

            // 5. 关联调整大小事件
            resizeThumb.DragDelta += Thumb_Resize_DragDelta;
            resizeThumb.DragCompleted += Thumb_Resize_DragCompleted;

            // 6. 将移动Thumb和调整大小Thumb都添加到Grid中
            containerGrid.Children.Add(moveThumb);
            containerGrid.Children.Add(resizeThumb);

            // 7. 计算并设置整个Grid容器的尺寸和位置
            if (!_translatedSizeCache.TryGetValue(task.ObjectId, out var size))
                {
                size = new Size(task.Bounds.Width(), task.Bounds.Height());
                }

            var bounds = task.GetTranslatedBounds(useUserPosition: true, accurateSize: size);
            var p1 = _transformMatrix.Transform(new WinPoint(bounds.MinPoint.X, bounds.MinPoint.Y));
            var p2 = _transformMatrix.Transform(new WinPoint(bounds.MaxPoint.X, bounds.MaxPoint.Y));

            containerGrid.Width = Math.Abs(p2.X - p1.X);
            containerGrid.Height = Math.Abs(p2.Y - p1.Y);

            var positionInCanvas = _transformMatrix.Transform(new WinPoint(bounds.MinPoint.X, bounds.MaxPoint.Y));
            Canvas.SetLeft(containerGrid, positionInCanvas.X);
            Canvas.SetTop(containerGrid, positionInCanvas.Y);
            Panel.SetZIndex(containerGrid, 50);

            // 8. 返回这个复合控件
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
                totalHeight = singleLineHeight * lines.Count + (singleLineHeight * 0.5) * (lines.Count - 1);
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

        #region --- INotifyPropertyChanged & Settings ---

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
            }
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

        private void SaveSettings()
            {
            _settings.TestNumberOfRounds = this.NumberOfRounds;
            _settings.TestSearchRangeFactor = this.CurrentSearchRangeFactor;
            _settingsService.SaveSettings(_settings);
            }

        private void RoundsComboBox_LostFocus(object sender, RoutedEventArgs e)
            {
            if (sender is ComboBox comboBox)
                {
                if (int.TryParse(comboBox.Text, out int value))
                    {
                    int clampedValue = (int)Math.Max(RoundsSlider.Minimum, Math.Min(RoundsSlider.Maximum, value));
                    if (this.NumberOfRounds != clampedValue)
                        {
                        this.NumberOfRounds = clampedValue;
                        }
                    comboBox.Text = this.NumberOfRounds.ToString();
                    }
                else
                    {
                    comboBox.Text = this.NumberOfRounds.ToString();
                    }
                }
            }
        private void SearchRangeComboBox_LostFocus(object sender, RoutedEventArgs e)
            {
            if (sender is ComboBox comboBox)
                {
                if (double.TryParse(comboBox.Text, out double value) && value > 0)
                    {
                    if (this.CurrentSearchRangeFactor != value)
                        {
                        this.CurrentSearchRangeFactor = value;
                        }
                    }
                comboBox.Text = this.CurrentSearchRangeFactor.ToString("F1");
                }
            }
        #endregion

        #region --- 按钮点击事件 ---
        private void RecalculateButton_Click(object sender, RoutedEventArgs e)
            {
            RecalculateAndRedraw();
            }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
            {
            var tasksToApply = _originalTargets;
            CadBridgeService.LayoutTasksToApply = tasksToApply;
            CadBridgeService.SendCommandToAutoCAD("TEST_APPLY\n");
            this.Close();
            }
        #endregion
        // <summary>
        /// 【核心新增】一个专用于WPF预览的、轻量级的文本换行方法。
        /// 它使用WPF的FormattedText进行近似测量，与AutoCAD完全无关，用于实现流畅的拖动预览。
        /// </summary>
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
        }
    }

public static class LayoutTaskExtensionsForView
    {
    /// <summary>
    /// 根据给定的精确尺寸，计算任务在特定位置的边界框。
    /// </summary>
    public static Extents3d GetTranslatedBounds(this LayoutTask task, bool useUserPosition = false, Size? accurateSize = null)
        {
        Point3d? positionToUse = useUserPosition ? task.CurrentUserPosition : task.AlgorithmPosition;

        if (!positionToUse.HasValue) return task.Bounds;

        // 如果提供了精确尺寸，则使用它；否则回退到旧的估算方式
        double width = accurateSize?.Width ?? task.Bounds.Width();
        double height = accurateSize?.Height ?? task.Bounds.Height();

        return new Extents3d(
            positionToUse.Value,
            new Point3d(positionToUse.Value.X + width, positionToUse.Value.Y + height, 0)
        );
        }
    }
// ▲▲▲ 新增结束 ▲▲▲
