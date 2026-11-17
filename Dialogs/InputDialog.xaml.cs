using System.Windows;

namespace StudyShell.Dialogs
{
    public partial class InputDialog : Window
    {
        public string Value => ValueBox.Text;

        public InputDialog(string title, string prompt, string initial)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            ValueBox.Text = initial ?? "";
            ValueBox.Focus();
            ValueBox.SelectAll();
        }

        private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
