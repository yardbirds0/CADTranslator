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

            // 增加一个检查，确保传入的viewModel不为空
            _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            }


        private void SaveButton_Click(object sender, RoutedEventArgs e)
            {
            this.DialogResult = true;
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
                var result = MessageBox.Show("模型列表已修改，是否保存？", "确认保存", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    {
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