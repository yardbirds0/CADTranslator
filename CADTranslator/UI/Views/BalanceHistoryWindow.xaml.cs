// 文件路径: CADTranslator/UI/Views/BalanceHistoryWindow.xaml.cs

using CADTranslator.Models;
using CADTranslator.Services;
using CADTranslator.UI.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace CADTranslator.UI.Views
    {
    public partial class BalanceHistoryWindow : FluentWindow
        {
        private readonly BalanceHistoryViewModel _viewModel;
        private readonly SettingsService _settingsService = new SettingsService();
        private readonly AppSettings _appSettings;

        public BalanceHistoryWindow(BalanceHistoryViewModel viewModel)
            {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            _appSettings = _settingsService.LoadSettings();
            }

        private void DataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
            {
            string originalColumnName = e.PropertyName;

            var mappingEntry = _appSettings.FriendlyNameMappings
                .FirstOrDefault(kvp => kvp.Value.Aliases.Contains(originalColumnName));

            string friendlyName = mappingEntry.Value?.DefaultFriendlyName ?? originalColumnName;

            e.Column.Header = friendlyName;


            if (originalColumnName == "查询时间")
                {
                e.Column.Width = new DataGridLength(180);
                }
            else
                {
                e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
                }
            e.Column.MinWidth = 80;
            }

        /// <summary>
        /// 【新增】关闭按钮的点击事件处理器
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
            {
            this.Close();
            }
        }
    }