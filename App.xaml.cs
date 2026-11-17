using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using StudyShell.Core;
using StudyShell.Services;

namespace StudyShell
{
    /// <summary>
    /// Neon Shell — неоновая игровая оболочка для Windows 10.
    /// Стандартный рабочий стол Windows скрывается, вместо него — неоновый
    /// рабочий стол, панель задач и меню с играми. При выходе Windows
    /// полностью восстанавливается.
    /// </summary>
    public partial class App : Application
    {
        public static string DataDir { get; private set; }
        public static string LogDir { get; private set; }

        public static LogService Log { get; private set; }
        public static SettingsService Settings { get; private set; }
        public static WindowManager WindowManager { get; private set; }
        public static NotificationService Notifications { get; private set; }
        public static InstalledAppsService Apps { get; private set; }
        public static CrashGuardService CrashGuard { get; private set; }

        public static string[] StartupArgs { get; set; } = Array.Empty<string>();

        public static bool AnimationsEnabled =>
            Settings?.Data.AnimationsEnabled ?? true;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                DataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "StudyShell");
                LogDir = Path.Combine(DataDir, "logs");
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(LogDir);

                Log = new LogService(Path.Combine(LogDir, "study.log"));
                Log.Info("=== Study Shell 1.0 запуск ===");

                CrashGuard = new CrashGuardService(DataDir, Log);
                if (CrashGuard.Evaluate(StartupArgs) == CrashDecision.Exit)
                {
                    Shutdown();
                    return;
                }

                Settings = new SettingsService(Path.Combine(DataDir, "settings.json"), Log);
                Settings.Load();
                StudyService.ResumeSession();
                ThemeManager.Apply(Settings);

                DispatcherUnhandledException += OnDispatcherException;
                AppDomain.CurrentDomain.UnhandledException += OnDomainException;
                TaskScheduler.UnobservedTaskException += OnTaskException;

                Apps = new InstalledAppsService(Log);
                Notifications = new NotificationService(Log);

                WindowManager = new WindowManager(Log);
                WindowManager.StartShell();

                CrashGuard.MarkRunning();
                Exit += OnAppExit;

                Apps.LoadAsync();

                Notifications.Show("Neon Shell",
                    "Оболочка запущена. ПКМ по значку в трее — меню, «Выйти в Windows» — восстановление.",
                    NotificationKind.Info);

                if (StudyService.IsActive)
                {
                    Notifications.Show("Время учёбы",
                        "Игры заблокированы. " + StudyService.StatusText + ". Удачи в учёбе!",
                        NotificationKind.Warning);
                }

                // Таймеры режима учёбы: отсчёт сессии + строгая блокировка игр
                var studyTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                studyTick.Tick += (_, _) =>
                {
                    StudyService.Tick();
                    WindowManager?.Desktop?.UpdateStudyWidget();
                };
                studyTick.Start();

                var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                watchdog.Tick += (_, _) => StudyService.WatchdogTick();
                watchdog.Start();

                // Автотест: запуск на несколько секунд с корректным выходом
                if (StartupArgs.Contains("--autotest", StringComparer.OrdinalIgnoreCase))
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        Log.Info("Автотест: корректный выход");
                        WindowManager?.ExitShell();
                    };
                    timer.Start();
                }

                Log.Info("Запуск завершён");
            }
            catch (Exception ex)
            {
                try { Log?.Error("Критическая ошибка запуска", ex); } catch { }
                MessageBox.Show("Neon Shell не удалось запустить:\n\n" + ex.Message,
                    "Neon Shell", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void OnAppExit(object sender, ExitEventArgs e)
        {
            try
            {
                CrashGuard?.ClearRunning();
                Settings?.SaveNow();
                Log?.Info("Завершение работы");
            }
            catch { }
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log?.Error("Необработанное исключение UI", e.Exception);
            try { Notifications?.Show("Ошибка", e.Exception.Message, NotificationKind.Error); }
            catch { }
            e.Handled = true;
        }

        private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Log?.Error("Критическое исключение домена", e.ExceptionObject as Exception);
            try
            {
                CrashGuard?.ClearRunning();
                ShellIntegration.RestoreAll();
            }
            catch { }
        }

        private void OnTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Log?.Error("Исключение фоновой задачи", e.Exception);
            e.SetObserved();
        }
    }
}
