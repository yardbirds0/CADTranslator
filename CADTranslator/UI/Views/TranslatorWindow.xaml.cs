// 1. 确保 using 声明中包含了 Wpf.Ui.Controls
using CADTranslator.UI.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Specialized;
using Wpf.Ui.Controls;

namespace CADTranslator.UI.Views
    {
    // 2. 这里的基类必须是 FluentWindow，不是 System.Windows.Window
    public partial class TranslatorWindow : FluentWindow
        {
        public TranslatorWindow()
            {
            InitializeComponent();
            var viewModel = new TranslatorViewModel(this);
            this.DataContext = viewModel;

            if (viewModel.StatusLog is INotifyCollectionChanged collection)
                {
                collection.CollectionChanged += LogItems_CollectionChanged;
                }
            }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            {
            // 检查事件源是否在 DataGridCell 内
            var dependencyObject = (DependencyObject)e.OriginalSource;
            while (dependencyObject != null && !(dependencyObject is DataGridCell))
                {
                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
                }

            if (dependencyObject is DataGridCell cell)
                {
                // 获取单元格所在的列
                var column = cell.Column;

                // 如果列的表头是“原文”，才执行编辑命令
                if (column != null && column.Header?.ToString() == "原文")
                    {
                    if (this.DataContext is TranslatorViewModel viewModel && dataGridBlocks.SelectedItem != null)
                        {
                        if (viewModel.EditCommand.CanExecute(dataGridBlocks.SelectedItem))
                            {
                            viewModel.EditCommand.Execute(dataGridBlocks.SelectedItem);
                            }
                        }
                    // 阻止事件继续传播，防止其他列（如译文的TextBox）的默认双击行为被触发
                    e.Handled = true;
                    }
                }
            }

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 当日志文本变化时，自动滚动到底部
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }
        private void LogItemsControl_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // 当日志集合添加新项时，自动滚动到底部
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // 使用Dispatcher确保在UI线程上执行
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (LogScrollViewer.Content is ItemsPresenter itemsPresenter)
                    {
                        // 强制更新布局以获取正确的滚动范围
                        itemsPresenter.UpdateLayout();
                        LogScrollViewer.ScrollToEnd();
                    }
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
            // 找到DataGrid内部的ScrollViewer
            var scrollViewer = FindScrollViewer(dataGridBlocks);

            if (scrollViewer != null)
                {
                // 根据滚轮方向，手动控制ScrollViewer滚动一小行
                if (e.Delta > 0) // 向上滚动
                    {
                    scrollViewer.LineUp();
                    }
                else // 向下滚动
                    {
                    scrollViewer.LineDown();
                    }

                // 标记事件已处理，阻止默认的快速滚动行为
                e.Handled = true;
                }
            }


        private void LogItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
            {
            // 当日志集合有新项被添加时
            if (e.Action == NotifyCollectionChangedAction.Add)
                {
                // 使用Dispatcher确保操作在UI线程上安全执行
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    // 滚动到 ScrollViewer 的最底部
                    LogScrollViewer.ScrollToEnd();
                }));
                }
            }


        }
    }