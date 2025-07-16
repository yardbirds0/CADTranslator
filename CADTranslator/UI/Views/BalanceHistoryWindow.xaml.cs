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

            // 确保传入的viewModel不为空，并将其赋给私有字段和DataContext
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            }
        }
    }