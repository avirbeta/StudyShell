using System;
using System.IO;
using System.Text;

namespace StudyShell.Services
{
    public class LogService
    {
        private readonly object _lock = new();
        private readonly string _path;

        public LogService(string path)
        {
            _path = path;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                {
                    File.Copy(path, path + ".old", true);
                    File.Delete(path);
                }
            }
            catch { }
        }

        public void Info(string message) => Write("INFO", message, null);
        public void Warn(string message) => Write("WARN", message, null);
        public void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private void Write(string level, string message, Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(" [").Append(level).Append("] ").AppendLine(message);
            if (ex != null) sb.AppendLine(ex.ToString());
            lock (_lock)
            {
                try { File.AppendAllText(_path, sb.ToString(), Encoding.UTF8); }
                catch { }
            }
        }
    }
}
