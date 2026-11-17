using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace StudyShell.Services
{
    public enum NotificationKind { Info, Success, Warning, Error }

    public class NotificationItem
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Kind { get; set; } = "Info";
    }

    /// <summary>Неоновые всплывающие уведомления с очередью и историей.</summary>
    public class NotificationService
    {
        private readonly LogService _log;
        private readonly Queue<NotificationItem> _queue = new();
        private readonly List<Notifications.ToastWindow> _visible = new();
        private readonly List<NotificationItem> _history = new();
        private const int MaxHistory = 100;

        public event EventHandler HistoryChanged;

        public NotificationService(LogService log)
        {
            _log = log;
            LoadHistory();
        }

        public IReadOnlyList<NotificationItem> History => _history;

        public bool IsEnabled => App.Settings?.Data.NotificationsEnabled ?? true;

        public void Show(string title, string message, NotificationKind kind = NotificationKind.Info)
        {
            if (!IsEnabled) { StoreHistory(title, message, kind); return; }
            _queue.Enqueue(new NotificationItem { Title = title, Message = message, Kind = kind.ToString() });
            StoreHistory(title, message, kind);
            Application.Current?.Dispatcher.BeginInvoke(new Action(ProcessQueue));
        }

        private void ProcessQueue()
        {
            try
            {
                while (_queue.Count > 0 && _visible.Count < 3)
                {
                    var item = _queue.Dequeue();
                    var toast = new Notifications.ToastWindow(item);
                    _visible.Add(toast);
                    toast.Closed += (_, _) =>
                    {
                        _visible.Remove(toast);
                        ProcessQueue();
                    };
                    toast.Show();
                }
            }
            catch (Exception ex)
            {
                _log.Error("Показ уведомления", ex);
            }
        }

        public void CloseAll()
        {
            foreach (var t in _visible.ToList()) t.Close();
            _queue.Clear();
        }

        public int VisibleCount(Notifications.ToastWindow self)
            => _visible.Count(t => t != self && t.IsVisible);

        public void ClearHistory()
        {
            _history.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            SaveHistory();
        }

        private void StoreHistory(string title, string message, NotificationKind kind)
        {
            _history.Insert(0, new NotificationItem { Title = title, Message = message, Kind = kind.ToString() });
            while (_history.Count > MaxHistory) _history.RemoveAt(_history.Count - 1);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            SaveHistory();
        }

        private string HistoryPath => Path.Combine(App.DataDir, "notifications.json");

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryPath))
                {
                    var list = JsonSerializer.Deserialize<List<NotificationItem>>(File.ReadAllText(HistoryPath));
                    if (list != null)
                    {
                        _history.AddRange(list);
                        while (_history.Count > MaxHistory) _history.RemoveAt(_history.Count - 1);
                    }
                }
            }
            catch (Exception ex) { _log.Error("История уведомлений", ex); }
        }

        private void SaveHistory()
        {
            try
            {
                File.WriteAllText(HistoryPath, JsonSerializer.Serialize(_history.Take(MaxHistory)));
            }
            catch (Exception ex) { _log.Error("Сохранение истории уведомлений", ex); }
        }
    }
}
