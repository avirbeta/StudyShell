using System;
using System.Threading;
using System.Windows;

namespace StudyShell
{
    public static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static void Main(string[] args)
        {
            App.StartupArgs = args ?? Array.Empty<string>();

            _mutex = new Mutex(true, "StudyShell_1.0_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Neon Shell уже запущен.",
                    "Neon Shell", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var app = new App();
            app.InitializeComponent();
            app.Run();

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
