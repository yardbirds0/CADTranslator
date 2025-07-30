using CADTranslator.ViewModels;
using Wpf.Ui.Controls;

namespace CADTranslator.Views
    {
    public partial class ApiDefinitionWindow : FluentWindow
        {
        // 【修改】保留 ViewModel 属性，但构造函数逻辑简化
        public ApiDefinitionViewModel ViewModel => DataContext as ApiDefinitionViewModel;

        public ApiDefinitionWindow(ApiDefinitionViewModel viewModel)
            {
            InitializeComponent();
            DataContext = viewModel;
            }

        // 【删除】移除 SaveButton_Click 和 CancelButton_Click 两个事件处理器方法
        }
    }