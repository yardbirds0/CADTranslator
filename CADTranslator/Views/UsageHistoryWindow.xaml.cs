// 文件路径: CADTranslator/Views/UsageHistoryWindow.xaml.cs
// 【这是一个新文件】

using CADTranslator.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace CADTranslator.Views
    {
    public partial class UsageHistoryWindow : FluentWindow
        {
        public UsageHistoryWindow(UsageHistoryViewModel viewModel)
            {
            InitializeComponent();
            DataContext = viewModel;
            }

        // 这个方法用于在自动生成列时，调整列宽和样式
        private void HistoryDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
            {
            // 为所有列设置一个最小宽度
            e.Column.MinWidth = 100;
            switch (e.PropertyName)
                {
                case "时间":
                    e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
                    break;
                case "吞吐量(字/秒)":
                    e.Column.MinWidth = 120; // 设置一个稍大的最小宽度
                    e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
                    break;
                case "失败原因":
                    e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star); // 占据剩余空间
                    break;
                // 对于这些文本较长的列，让它们根据内容和表头自动调整，并给予更大的最小宽度
                case "模型":
                case "提示词":
                    e.Column.MinWidth = 120; // 设置一个稍大的最小宽度
                    e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
                    break;
                // 其他所有列都使用Auto模式，确保表头能完整显示
                default:
                    e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
                    break;
                }
            }
        }
    }