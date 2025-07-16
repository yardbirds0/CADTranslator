// 文件路径: CADTranslator/UI/Views/BalanceHistoryWindow.xaml.cs

using CADTranslator.UI.ViewModels;
using Wpf.Ui.Controls;
using System; // 需要这个来使用 ArgumentNullException

namespace CADTranslator.UI.Views
    {
    public partial class BalanceHistoryWindow : FluentWindow
        {
        private readonly BalanceHistoryViewModel _viewModel;

        public BalanceHistoryWindow(BalanceHistoryViewModel viewModel)
            {
            InitializeComponent();

            // 确保传入的viewModel不为空
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            // ▼▼▼【核心修正】就是下面这一行代码 ▼▼▼
            // 将接收到的viewModel实例，设置为当前窗口的数据上下文(DataContext)
            DataContext = _viewModel;
            // ▲▲▲ 修正结束 ▲▲▲
            }
        }
    }