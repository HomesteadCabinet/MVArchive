using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace MVArchive.Services
{
    public class LogEntry : INotifyPropertyChanged
    {
        private DateTime _timestamp;
        private string _level = string.Empty;
        private string _category = string.Empty;
        private string _message = string.Empty;
        private string _details = string.Empty;

        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        public string Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        public string Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public string Details
        {
            get => _details;
            set => SetProperty(ref _details, value);
        }

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");
        public string FullMessage => $"[{Level}] {Category}: {Message}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class LoggingService : INotifyPropertyChanged
    {
        private readonly ObservableCollection<LogEntry> _logEntries;
        private readonly Dispatcher _dispatcher;
        private bool _isEnabled = true;
        private int _maxEntries = 1000;

        public LoggingService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _logEntries = new ObservableCollection<LogEntry>();
        }

        public ObservableCollection<LogEntry> LogEntries => _logEntries;
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
        public int MaxEntries { get => _maxEntries; set => SetProperty(ref _maxEntries, value); }

        public void LogInfo(string category, string message, string? details = null)
        {
            AddLogEntry("INFO", category, message, details);
        }

        public void LogWarning(string category, string message, string? details = null)
        {
            AddLogEntry("WARN", category, message, details);
        }

        public void LogError(string category, string message, string? details = null)
        {
            AddLogEntry("ERROR", category, message, details);
        }

        public void LogDebug(string category, string message, string? details = null)
        {
            AddLogEntry("DEBUG", category, message, details);
        }

        public void LogDatabase(string operation, string query, string? parameters = null, TimeSpan? duration = null)
        {
            var details = $"Query: {query}";
            if (!string.IsNullOrEmpty(parameters))
                details += $"\nParameters: {parameters}";
            if (duration.HasValue)
                details += $"\nDuration: {duration.Value.TotalMilliseconds:F2}ms";

            AddLogEntry("DB", "Database", operation, details);
        }

        public void LogArchive(string operation, string details, int? recordsProcessed = null, int? totalRecords = null)
        {
            var message = operation;
            if (recordsProcessed.HasValue && totalRecords.HasValue)
                message += $" ({recordsProcessed}/{totalRecords})";

            AddLogEntry("ARCHIVE", "Archive", message, details);
        }

        public void LogSystem(string operation, string? details = null)
        {
            AddLogEntry("SYSTEM", "System", operation, details);
        }

        private void AddLogEntry(string level, string category, string message, string? details)
        {
            Console.WriteLine($"{level} {category} {message} {details}");
            if (!IsEnabled) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message,
                Details = details ?? string.Empty
            };

            _dispatcher.BeginInvoke(() =>
            {
                _logEntries.Add(entry);

                // Limit the number of log entries to prevent memory issues
                while (_logEntries.Count > MaxEntries)
                {
                    _logEntries.RemoveAt(0);
                }
            });
        }

        public void Clear()
        {
            _dispatcher.BeginInvoke(() => _logEntries.Clear());
        }

        public void ExportToFile(string filePath)
        {
            try
            {
                var lines = new List<string>();
                foreach (var entry in _logEntries)
                {
                    lines.Add($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Category}: {entry.Message}");
                    if (!string.IsNullOrEmpty(entry.Details))
                    {
                        lines.Add($"  Details: {entry.Details}");
                    }
                }
                System.IO.File.WriteAllLines(filePath, lines);
                LogSystem("Log exported successfully", $"Exported {_logEntries.Count} entries to {filePath}");
            }
            catch (Exception ex)
            {
                LogError("Export", "Failed to export log", ex.ToString());
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
