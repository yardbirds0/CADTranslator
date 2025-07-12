using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CADTranslator.AutoCAD.Commands;
using CADTranslator.Services;
using CADTranslator.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CADTranslator.UI.Views
    {
    public partial class TranslatorWindow : Window
    {
        public TranslatorWindow()
        {
            InitializeComponent();
            var viewModel = new TranslatorViewModel();
            this.DataContext = viewModel;
        }

        // --- 核心功能按钮 ---



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





        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            WindowBlurHelper.EnableBlur(this);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosing(CancelEventArgs e)
            {
            if (this.DataContext is TranslatorViewModel viewModel)
                {
                viewModel.SaveSettingsCommand.Execute(null);
                }
            base.OnClosing(e);
            }
        }
}