// 1. 确保 using 声明中包含了 Wpf.Ui.Controls
using CADTranslator.UI.ViewModels;
using System;
using System.Windows.Controls;
using System.Windows.Input;
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
            }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            {
            if (this.DataContext is TranslatorViewModel viewModel && dataGridBlocks.SelectedItem != null)
                {
                if (viewModel.EditCommand.CanExecute(dataGridBlocks.SelectedItem))
                    {
                    viewModel.EditCommand.Execute(dataGridBlocks.SelectedItem);
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

    }
    }