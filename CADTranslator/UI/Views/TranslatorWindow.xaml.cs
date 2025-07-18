// 文件路径: CADTranslator/UI/Views/TranslatorWindow.xaml.cs
using CADTranslator.Services; // ◄◄◄ 确保引入Services命名空间
using CADTranslator.UI.ViewModels;
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Autodesk.AutoCAD.ApplicationServices; // ◄◄◄ 【新增】引入AutoCAD服务

namespace CADTranslator.UI.Views
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
                var column = cell.Column;

                if (column != null && column.Header?.ToString() == "原文")
                    {
                    if (this.DataContext is TranslatorViewModel viewModel && dataGridBlocks.SelectedItem != null)
                        {
                        if (viewModel.EditCommand.CanExecute(dataGridBlocks.SelectedItem))
                            {
                            viewModel.EditCommand.Execute(dataGridBlocks.SelectedItem);
                            }
                        }
                    e.Handled = true;
                    }
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
        }
    }