// 文件路径: CADTranslator/Services/WindowService.cs
using CADTranslator.ViewModels;
using CADTranslator.Views;
using System;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace CADTranslator.Services.UI
    {
    /// <summary>
    /// IWindowService接口的具体实现。
    /// 这个类知道如何操作实际的WPF窗口和控件，它作为View和ViewModel之间的桥梁。
    /// </summary>
    public class WindowService : IWindowService
        {
        private Window _owner;

        public WindowService(Window owner)
            {
            _owner = owner;
            }

        // 【新增】实现接口中定义的新方法
        public void Initialize(Window owner)
            {
            _owner = owner;
            }

        public Task ShowInformationDialogAsync(string title, string content, string closeButtonText = "确定")
            {
            var messageBox = new MessageBox
                {
                Title = title,
                Content = content,
                CloseButtonText = closeButtonText,
                // 【关键】使用主窗口的资源字典，确保样式统一
                Resources = _owner.Resources
                };
            return messageBox.ShowDialogAsync();
            }

        public Task<MessageBoxResult> ShowConfirmationDialogAsync(string title, string content, string primaryButtonText, string closeButtonText = "取消")
            {
            var messageBox = new MessageBox
                {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = closeButtonText,
                Resources = _owner.Resources
                };
            return messageBox.ShowDialogAsync();
            }

        public bool? ShowModelManagementDialog(ModelManagementViewModel viewModel)
            {
            var window = new ModelManagementWindow(viewModel) { Owner = _owner };
            return window.ShowDialog();
            }

        public void ShowBalanceHistoryDialog(BalanceHistoryViewModel viewModel)
            {
            var window = new BalanceHistoryWindow(viewModel) { Owner = _owner };
            window.ShowDialog();
            }

        public (bool? DialogResult, string EditedText) ShowEditDialog(string initialText)
            {
            var window = new EditWindow(initialText) { Owner = _owner };
            var dialogResult = window.ShowDialog();
            return (dialogResult, window.EditedText);
            }

        public void HideMainWindow()
            {
            _owner?.Hide();
            }

        public void ShowMainWindow()
            {
            _owner?.Show();
            }

        public void MinimizeMainWindow()
            {
            if (_owner != null)
                {
                _owner.WindowState = WindowState.Minimized;
                }
            }

        public void ActivateMainWindow()
            {
            if (_owner != null)
                {
                if (!_owner.IsVisible)
                    {
                    _owner.Show();
                    }
                if (_owner.WindowState == WindowState.Minimized)
                    {
                    _owner.WindowState = WindowState.Normal;
                    }
                _owner.Activate();
                }
            }

        public void InvokeOnUIThread(Action action)
            {
            // 使用owner窗口的Dispatcher来执行操作
            _owner?.Dispatcher.Invoke(action);
            }
        public void ScrollToGridItem(object item)
            {
            // 安全地在UI线程上执行
            _owner?.Dispatcher.Invoke(() =>
            {
                // 检查item是否为空，以及owner是否是我们预期的TranslatorWindow
                if (item != null && _owner is TranslatorWindow ownerWindow)
                    {
                    // 直接访问在XAML中命名的DataGrid控件
                    var dataGrid = ownerWindow.dataGridBlocks;
                    if (dataGrid != null)
                        {
                        // 调用WPF DataGrid内置的滚动方法
                        dataGrid.ScrollIntoView(item);
                        }
                    }
            });
            }

        public void ShowUsageHistoryDialog(UsageHistoryViewModel viewModel)
            {
            var window = new UsageHistoryWindow(viewModel) { Owner = _owner };
            window.ShowDialog();
            }

        }
    }