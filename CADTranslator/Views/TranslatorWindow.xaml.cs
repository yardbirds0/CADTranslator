// 文件路径: CADTranslator/UI/Views/TranslatorWindow.xaml.cs
using CADTranslator.Services; // ◄◄◄ 确保引入Services命名空间
using CADTranslator.ViewModels;
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using CADTranslator.Services.UI;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Services.Translation; // ◄◄◄ 【新增】引入AutoCAD服务

namespace CADTranslator.Views
    {
    public partial class TranslatorWindow : FluentWindow
        {
        public TranslatorWindow()
            {
            InitializeComponent();

            // ▼▼▼ 【核心修改】在这里创建所有服务实例 ▼▼▼
            // 1. 创建UI服务
            IWindowService windowService = new WindowService(this);

            // 2. 创建与CAD环境相关的服务
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            IAdvancedTextService advancedTextService = new AdvancedTextService(doc);
            ICadLayoutService cadLayoutService = new CadLayoutService(doc);

            // 3. 创建与数据和逻辑相关的服务
            ISettingsService settingsService = new SettingsService();
            ApiRegistry apiRegistry = new ApiRegistry();

            // 4. 将所有创建好的服务，注入到ViewModel的构造函数中
            var viewModel = new TranslatorViewModel(
                windowService,
                settingsService,
                advancedTextService,
                cadLayoutService,
                apiRegistry
            );

            // 5. 设置数据上下文
            this.DataContext = viewModel;

            // (下面的代码保持不变)
            if (viewModel.StatusLog is INotifyCollectionChanged collection)
                {
                collection.CollectionChanged += LogItems_CollectionChanged;
                }
            }

        // (所有事件处理器，如DataGrid_MouseDoubleClick等，保持不变)
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            {
            var dependencyObject = (DependencyObject)e.OriginalSource;
            while (dependencyObject != null && !(dependencyObject is DataGridCell))
                {
                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
                }

            if (dependencyObject is DataGridCell cell)
                {
                // var column = cell.Column; // 这一行也可以一并删除，因为它不再被使用
                }
            }

        private void LogItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
            {
            if (e.Action == NotifyCollectionChangedAction.Add)
                {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    LogScrollViewer.ScrollToEnd();
                }));
                }
            }

        private ScrollViewer FindScrollViewer(DependencyObject d)
            {
            if (d is ScrollViewer)
                return d as ScrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                {
                var child = VisualTreeHelper.GetChild(d, i);
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
                }
            return null;
            }

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
            {
            var scrollViewer = FindScrollViewer(dataGridBlocks);

            if (scrollViewer != null)
                {
                if (e.Delta > 0)
                    {
                    scrollViewer.LineUp();
                    }
                else
                    {
                    scrollViewer.LineDown();
                    }
                e.Handled = true;
                }
            }

        private void RootNavigationView_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            {
            // 如果侧边栏已经是展开状态，则不做任何处理
            if (RootNavigationView.IsPaneOpen)
                {
                return;
                }

            // 查找被点击的 NavigationViewItem
            if (e.OriginalSource is DependencyObject depObj)
                {
                var item = FindParent<NavigationViewItem>(depObj);

                // 如果确实点击了一个菜单项
                if (item != null)
                    {
                    // 手动展开侧边栏
                    RootNavigationView.IsPaneOpen = true;

                    // 手动展开被点击的菜单项
                    item.IsExpanded = true;

                    // 阻止事件继续传播，以防止默认的Flyout弹出
                    e.Handled = true;
                    }
                }
            }

        private void SettingsPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            {
            // 将事件的原始来源转换为一个依赖对象，以便在可视化树中遍历
            var currentElement = e.OriginalSource as DependencyObject;

            // 从被点击的最深层元素开始，向上遍历可视化树
            while (currentElement != null)
                {
                // 如果遍历到了我们挂载事件的StackPanel本身，就停止
                if (currentElement == sender)
                    {
                    break;
                    }

                // --- 这是“白名单”检查 ---
                if (currentElement is ComboBox ||
                    currentElement is ComboBoxItem ||
                    currentElement is System.Windows.Controls.TextBox ||
                    currentElement is System.Windows.Controls.PasswordBox ||
                    currentElement is System.Windows.Controls.Button ||
                    currentElement is CheckBox ||
                    currentElement is RadioButton ||
                    currentElement is Slider ||
                    currentElement is Wpf.Ui.Controls.NumberBox ||
                    currentElement is Wpf.Ui.Controls.ToggleSwitch // <--- 【请在这里添加这一行】
                   )
                    {
                    return; // 放行事件
                    }

                // 继续向上移动一个节点
                currentElement = VisualTreeHelper.GetParent(currentElement);
                }

            e.Handled = true;
            }

        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
            {
            DependencyObject parentObject = child;

            while (parentObject != null)
                {
                // 检查当前父元素是否是我们想要的类型
                if (parentObject is T parent)
                    {
                    return parent;
                    }

                // 继续向上查找
                parentObject = VisualTreeHelper.GetParent(parentObject);
                }
            return null;
            }

        private void TranslatorWindow_Loaded(object sender, RoutedEventArgs e)
            {
            // 当窗口完全加载后，再设置导航栏为展开状态。
            // 因为此时所有控件都已就绪，所以不会有任何启动动画。
            RootNavigationView.IsPaneOpen = true;
            }

        private void RootNavigationView_PaneOpened(object sender, RoutedEventArgs e)
            {
            // 当用户手动展开导航栏时，将列宽设置为展开宽度
            NavColumn.Width = new GridLength(320);
            }

        private void RootNavigationView_PaneClosed(object sender, RoutedEventArgs e)
            {
            // 当用户手动收起导航栏时，将列宽设置为紧凑宽度
            NavColumn.Width = new GridLength(48);

            // ▼▼▼ 从这里开始是新增的代码 ▼▼▼
            // 遍历导航栏中的所有主菜单项
            foreach (var item in RootNavigationView.MenuItems)
                {
                // 检查这个项是不是一个可以展开/折叠的 NavigationViewItem
                if (item is Wpf.Ui.Controls.NavigationViewItem navigationViewItem)
                    {
                    // 如果是，就命令它折叠起来
                    navigationViewItem.IsExpanded = false;
                    }
                }
            }
        }
    }