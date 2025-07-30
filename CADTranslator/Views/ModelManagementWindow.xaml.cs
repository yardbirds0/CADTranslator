// 文件路径: CADTranslator/Views/ModelManagementWindow.xaml.cs

using CADTranslator.ViewModels;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;
// 【删除】不再需要 MessageBox 和 MessageBoxResult 的 using 引用

namespace CADTranslator.Views
    {
    public partial class ModelManagementWindow : FluentWindow
        {
        // 【修改】获取ViewModel的方式保持不变，但内容已更新
        private readonly ModelManagementViewModel _viewModel;

        public ModelManagementWindow(ModelManagementViewModel viewModel)
            {
            InitializeComponent();
            _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            }
        protected override async void OnClosing(CancelEventArgs e)
            {
            // 如果窗口已经通过点击按钮的方式设置了DialogResult，则直接关闭
            if (DialogResult.HasValue)
                {
                base.OnClosing(e);
                return;
                }

            // 否则，执行ViewModel中的关闭逻辑
            e.Cancel = true; // 先阻止窗口关闭

            var result = await _viewModel.RequestCloseAsync();

            if (result.HasValue) // 如果返回不是null (即用户没有点取消)
                {
                this.DialogResult = result.Value; // 设置DialogResult
                this.Close(); // 再次调用Close，这次因为DialogResult有值，会直接关闭
                }
            }
        }
    }