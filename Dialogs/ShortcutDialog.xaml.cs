using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace StudyShell.Dialogs
{
    public partial class ShortcutDialog : Window
    {
        public string TargetPath => PathBox.Text.Trim();
        public string ShortcutName => string.IsNullOrWhiteSpace(NameBox.Text) ? "Новый ярлык" : NameBox.Text.Trim();
        public string Arguments => ArgsBox.Text;

        public ShortcutDialog()
        {
            InitializeComponent();
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите объект для ярлыка",
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
            if (string.IsNullOrWhiteSpace(TargetPath) || !File.Exists(TargetPath) && !Directory.Exists(TargetPath))
            {
                MessageBox.Show("Укажите существующий файл или папку.",
                    "Neon Shell", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
