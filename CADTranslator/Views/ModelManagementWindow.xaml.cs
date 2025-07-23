// 文件路径: CADTranslator/UI/Views/ModelManagementWindow.xaml.cs
// 【注意】这是一个完整的文件替换

using CADTranslator.ViewModels;
using System.ComponentModel;
using System.Threading.Tasks; // ◄◄◄ 添加 Task 引用
using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult; // ◄◄◄ 添加 Wpf.Ui.Controls 引用

namespace CADTranslator.Views
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

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
            {
            // 首先，弹出一个内容更明确的提示框，告诉用户保存后窗口会关闭
            var mb = new MessageBox
                {
                Title = "操作成功",
                Content = "模型列表已成功保存！",
                CloseButtonText = "好的"
                };
            mb.Resources = this.Resources; // 确保样式统一
            await mb.ShowDialogAsync();

            // 在用户点击“好的”之后，将 DialogResult 设置为 true。
            // 这是触发主窗口保存逻辑的关键步骤。
            this.DialogResult = true;
            }

        // ▼▼▼ 修改 "应用选择模型" 按钮逻辑 ▼▼▼
        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
            {
            if (_viewModel.SelectedModel == null)
                {
                var mb = new MessageBox
                    {
                    Title = "提示",
                    Content = "请先在列表中选择一个模型，然后再点击应用。",
                    CloseButtonText = "好的"
                    };
                mb.Resources = this.Resources;
                await mb.ShowDialogAsync();
                return;
                }
            this.DialogResult = true;
            }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            {
            this.DialogResult = false;
            }

        // ▼▼▼ 修改 OnClosing 事件处理 ▼▼▼
        protected override async void OnClosing(CancelEventArgs e)
            {
            // 阻止默认的关闭行为，因为我们需要异步处理
            e.Cancel = true;

            if (DialogResult.HasValue && DialogResult.Value)
                {
                // 如果是点击“应用”或“取消”按钮关闭的，直接允许关闭
                // 通过设置一个临时变量来跳出循环
                e.Cancel = false;
                base.OnClosing(e);
                return;
                }

            if (_viewModel.IsDirty)
                {
                var mb = new MessageBox
                    {
                    Title = "确认保存",
                    Content = "模型列表已修改，是否在关闭前保存？",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否",
                    CloseButtonText = "取消"
                    };
                mb.Resources = this.Resources;

                var result = await mb.ShowDialogAsync();

                if (result == MessageBoxResult.Primary) // 用户点击 "是"
                    {
                    DialogResult = true;
                    }
                else if (result == MessageBoxResult.Secondary) // 用户点击 "否"
                    {
                    DialogResult = false;
                    }
                else // 用户点击 "取消" 或关闭对话框
                    {
                    return; // 保持窗口打开
                    }
                }
            else
                {
                DialogResult = false;
                }

            // 真正关闭窗口
            e.Cancel = false;
            base.OnClosing(e);
            }
        }
    }