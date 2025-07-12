using System.Windows;
namespace CADTranslator.UI.Views
    {
    public partial class EditWindow : Window
    {
        public string EditedText { get; private set; }
        public EditWindow(string initialText)
        {
            InitializeComponent();
            MainTextBox.Text = initialText;
            MainTextBox.Focus();
            MainTextBox.SelectAll();
        }
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            EditedText = MainTextBox.Text;
            DialogResult = true;
        }
    }
}