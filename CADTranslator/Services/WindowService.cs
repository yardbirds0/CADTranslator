// 文件路径: CADTranslator/Services/WindowService.cs
using CADTranslator.UI.ViewModels;
using CADTranslator.UI.Views;
using System;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace CADTranslator.Services
    {
    /// <summary>
    /// IWindowService接口的具体实现。
    /// 这个类知道如何操作实际的WPF窗口和控件，它作为View和ViewModel之间的桥梁。
    /// </summary>
    public class WindowService : IWindowService
        {
        private readonly Window _owner;

        /// <summary>
        /// 构造函数，接收一个Window实例作为所有对话框的“父窗口”。
        /// 这确保了新打开的窗口总是在主窗口的前面。
        /// </summary>
        /// <param name="owner">主窗口实例 (通常是TranslatorWindow)</param>
        public WindowService(Window owner)
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
        }
    }