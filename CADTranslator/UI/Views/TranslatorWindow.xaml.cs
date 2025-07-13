// 1. 确保 using 声明中包含了 Wpf.Ui.Controls
using Wpf.Ui.Controls;
using CADTranslator.UI.ViewModels;
using System.Windows.Input;

namespace CADTranslator.UI.Views
    {
    // 2. 这里的基类必须是 FluentWindow，不是 System.Windows.Window
    public partial class TranslatorWindow : FluentWindow
        {
        public TranslatorWindow()
            {
            InitializeComponent();

            var viewModel = new TranslatorViewModel();
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
        }
    }