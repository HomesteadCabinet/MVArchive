using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MVArchive.Services;

namespace MVArchive.Controls
{
    public partial class LoggingPanel : UserControl
    {
        private LoggingService _loggingService;
        private readonly ObservableCollection<LogEntry> _filteredLogEntries;
        private string _currentLevelFilter = "All Levels";
        private string _currentCategoryFilter = "All Categories";
        private string _currentSearchFilter = string.Empty;

        public LoggingPanel()
        {
            InitializeComponent();
            _filteredLogEntries = new ObservableCollection<LogEntry>();
            dgLogEntries.ItemsSource = _filteredLogEntries;

            // Create a default logging service (will be replaced when SetLoggingService is called)
            _loggingService = new LoggingService(Dispatcher);
            SetupLoggingService();
        }

        public void SetLoggingService(LoggingService loggingService)
        {
            // Unsubscribe from the old service if it exists
            if (_loggingService != null)
            {
                _loggingService.LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
            }

            // Set the new service
            _loggingService = loggingService;

            // Log the change for debugging
            System.Diagnostics.Debug.WriteLine($"LoggingPanel: LoggingService changed. New service has {_loggingService.LogEntries.Count} entries.");

            SetupLoggingService();
        }

        private void SetupLoggingService()
        {
            // Subscribe to collection changes to reflect new logs immediately
            _loggingService.LogEntries.CollectionChanged += LogEntries_CollectionChanged;

            // Setup event handlers
            SetupEventHandlers();

            // Initial status update
            ApplyFilters();
            UpdateStatus();
        }

        private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"LoggingPanel: CollectionChanged - Action: {e.Action}, NewItems: {e.NewItems?.Count ?? 0}");

            // Refresh filtered view and status on any change
            Dispatcher.BeginInvoke(() =>
            {
                ApplyFilters();
                UpdateStatus();
                // Optional: auto-scroll to bottom on new items
                if (e.Action == NotifyCollectionChangedAction.Add && dgLogEntries.Items.Count > 0)
                {
                    dgLogEntries.ScrollIntoView(dgLogEntries.Items[dgLogEntries.Items.Count - 1]);
                }
            });
        }

        public LoggingService LoggingService => _loggingService;

        private void SetupEventHandlers()
        {
            cmbLevelFilter.SelectionChanged += (s, e) =>
            {
                if (cmbLevelFilter.SelectedItem is ComboBoxItem item)
                {
                    _currentLevelFilter = item.Content.ToString() ?? "All Levels";
                    ApplyFilters();
                }
            };

            cmbCategoryFilter.SelectionChanged += (s, e) =>
            {
                if (cmbCategoryFilter.SelectedItem is ComboBoxItem item)
                {
                    _currentCategoryFilter = item.Content.ToString() ?? "All Categories";
                    ApplyFilters();
                }
            };
        }

        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentSearchFilter = txtSearchFilter.Text;
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            _filteredLogEntries.Clear();

            var query = _loggingService.LogEntries.AsEnumerable();

            System.Diagnostics.Debug.WriteLine($"LoggingPanel: ApplyFilters - Total entries: {_loggingService.LogEntries.Count}");

            // Apply level filter
            if (_currentLevelFilter != "All Levels")
            {
                query = query.Where(entry => entry.Level == _currentLevelFilter);
            }

            // Apply category filter
            if (_currentCategoryFilter != "All Categories")
            {
                query = query.Where(entry => entry.Category == _currentCategoryFilter);
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(_currentSearchFilter))
            {
                var searchLower = _currentSearchFilter.ToLower();
                query = query.Where(entry =>
                    entry.Message.ToLower().Contains(searchLower) ||
                    entry.Details.ToLower().Contains(searchLower) ||
                    entry.Category.ToLower().Contains(searchLower)
                );
            }

            foreach (var entry in query)
            {
                _filteredLogEntries.Add(entry);
            }

            System.Diagnostics.Debug.WriteLine($"LoggingPanel: ApplyFilters - Filtered entries: {_filteredLogEntries.Count}");

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            txtLogCount.Text = $"Entries: {_loggingService.LogEntries.Count}";
            txtFilteredCount.Text = $"Filtered: {_filteredLogEntries.Count}";

            txtLogStatus.Text = "Ready";
        }

        private void BtnClearLog_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all log entries?",
                "Clear Log",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _loggingService.Clear();
                _filteredLogEntries.Clear();
                UpdateStatus();
            }
        }

        private void BtnExportLog_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Export Log",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"MVArchive_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    _loggingService.ExportToFile(saveFileDialog.FileName);
                    MessageBox.Show(
                        $"Log exported successfully to:\n{saveFileDialog.FileName}",
                        "Export Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to export log:\n{ex.Message}",
                        "Export Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        public void RefreshFilters()
        {
            ApplyFilters();
        }

        public void RefreshDisplay()
        {
            // Force refresh of the log display
            ApplyFilters();
            UpdateStatus();
        }

        public void AddTestLogEntry()
        {
            // Add a test log entry to verify the system is working
            var testEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "TEST",
                Category = "Debug",
                Message = "Test log entry from LoggingPanel",
                Details = "This is a test entry to verify logging is working"
            };

            _filteredLogEntries.Add(testEntry);
            UpdateStatus();
        }
    }
}
