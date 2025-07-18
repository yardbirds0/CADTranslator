// 文件路径: CADTranslator/UI/Views/ApiDefinitionWindow.xaml.cs

using CADTranslator.UI.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace CADTranslator.UI.Views
    {
    public partial class ApiDefinitionWindow : FluentWindow
        {
        public ApiDefinitionViewModel ViewModel { get; }

        public ApiDefinitionWindow(ApiDefinitionViewModel viewModel)
            {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = ViewModel;
            }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
            {
            // 验证逻辑可以放在这里
            if (string.IsNullOrWhiteSpace(ViewModel.DisplayName))
                {
                // 使用WPF-UI的MessageBox
                var mb = new MessageBox
                    {
                    Title = "验证失败",
                    Content = "API显示名称不能为空。",
                    CloseButtonText = "确定"
                    };
                mb.ShowDialogAsync();
                return;
                }

            this.DialogResult = true;
            }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            {
            this.DialogResult = false;
            }
        }
    }