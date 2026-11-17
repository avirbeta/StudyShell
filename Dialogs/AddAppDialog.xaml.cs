using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace StudyShell.Dialogs
{
    public partial class AddAppDialog : Window
    {
        public string AppPath => PathBox.Text.Trim();
        public string AppName => string.IsNullOrWhiteSpace(NameBox.Text)
            ? Path.GetFileNameWithoutExtension(AppPath)
            : NameBox.Text.Trim();
        public string Arguments => ArgsBox.Text;

        public AddAppDialog()
        {
            InitializeComponent();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите приложение",
                Filter = "Программы|*.exe;*.lnk;*.bat;*.cmd|Все файлы|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                PathBox.Text = dlg.FileName;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AppPath) || !File.Exists(AppPath))
            {
                MessageBox.Show("Укажите существующий файл приложения.",
                    "Study Shell", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
