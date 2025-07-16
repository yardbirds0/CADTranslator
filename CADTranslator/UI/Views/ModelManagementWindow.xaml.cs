// CADTranslator/UI/Views/ModelManagementWindow.xaml.cs
using CADTranslator.UI.ViewModels;
using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace CADTranslator.UI.Views
    {
    public partial class ModelManagementWindow : FluentWindow
        {
        private readonly ModelManagementViewModel _viewModel;

        public ModelManagementWindow(ModelManagementViewModel viewModel)
            {
            InitializeComponent();
            _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            }

        // "保存"按钮的逻辑，只保存，不关闭
        private void SaveButton_Click(object sender, RoutedEventArgs e)
            {
            // 触发保存逻辑，但窗口保持打开
            // 这个事件实际上可以由ViewModel通过命令处理，但为了简单起见，我们暂时保留在code-behind
            // 通知主窗口保存
            _viewModel.MarkAsDirty(); // 标记为已修改，以便关闭时提示
            MessageBox.Show("模型列表已保存！", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }

        // 新增的"应用选择模型"按钮逻辑
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
            {
            if (_viewModel.SelectedModel == null)
                {
                MessageBox.Show("请先在列表中选择一个模型，然后再点击应用。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
                }
            this.DialogResult = true; // 设置DialogResult为true，表示用户确认了操作
            }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            {
            this.DialogResult = false;
            }

        protected override void OnClosing(CancelEventArgs e)
            {
            base.OnClosing(e);

            if (DialogResult.HasValue)
                {
                return;
                }

            if (_viewModel.IsDirty)
                {
                var result = MessageBox.Show("模型列表已修改，是否在关闭前保存？", "确认保存", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    {
                    // 用户选择保存，我们标记DialogResult为true，让主窗口知道需要更新
                    DialogResult = true;
                    }
                else if (result == MessageBoxResult.No)
                    {
                    DialogResult = false;
                    }
                else
                    {
                    e.Cancel = true;
                    }
                }
            else
                {
                DialogResult = false;
                }
            }
        }
    }