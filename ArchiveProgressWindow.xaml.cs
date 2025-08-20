using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MVArchive.Models;
using MVArchive.Services;

namespace MVArchive
{
    public partial class ArchiveProgressWindow : Window
    {
        private readonly ArchiveService _archiveService;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly DispatcherTimer _timer;
        private DateTime _startTime;

        public ArchiveProgressWindow(ArchiveService archiveService)
        {
            InitializeComponent();
            _archiveService = archiveService;
            _cancellationTokenSource = new CancellationTokenSource();

            // Setup timer for elapsed time
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _startTime;
            txtElapsedTime.Text = $"Elapsed: {elapsed:hh\\:mm\\:ss}";
        }

        public async Task StartArchiveAsync(string? projectId = null)
        {
            try
            {
                _startTime = DateTime.Now;
                _timer.Start();

                btnCancel.IsEnabled = true;
                btnClose.IsEnabled = false;

                var progress = new Progress<ArchiveProgress>(UpdateProgress);

                ArchiveProgress result;

                if (!string.IsNullOrEmpty(projectId))
                {
                    // Archive single project
                    LogMessage($"Starting archive for project {projectId}");
                    result = await _archiveService.ArchiveProjectAsync(projectId, progress);
                }
                else
                {
                    // Archive all projects
                    LogMessage("Starting archive for all projects");
                    result = await _archiveService.ArchiveAllProjectsAsync(progress);
                }

                // Archive completed
                _timer.Stop();
                btnCancel.IsEnabled = false;
                btnClose.IsEnabled = true;

                if (result.IsComplete)
                {
                    LogMessage($"Archive completed successfully: {result.Status}");
                    txtOverallStatus.Text = "Archive completed successfully!";
                    txtOverallStatus.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    LogMessage($"Archive failed: {result.Status}");
                    txtOverallStatus.Text = "Archive failed!";
                    txtOverallStatus.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("Archive was cancelled by user");
                txtOverallStatus.Text = "Archive cancelled by user";
                txtOverallStatus.Foreground = System.Windows.Media.Brushes.Orange;
            }
            catch (Exception ex)
            {
                LogMessage($"Archive error: {ex.Message}");
                txtOverallStatus.Text = "Archive failed with error";
                txtOverallStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                _timer.Stop();
                btnCancel.IsEnabled = false;
                btnClose.IsEnabled = true;
            }
        }

        private void UpdateProgress(ArchiveProgress progress)
        {
            // Update overall progress
            if (progress.TotalProjects > 0)
            {
                pbOverall.Maximum = progress.TotalProjects;
                pbOverall.Value = progress.ProjectsProcessed;
                txtOverallProgress.Text = $"{progress.ProjectsProcessed} of {progress.TotalProjects} projects completed";
            }

            // Update current table progress
            if (progress.TotalRecords > 0)
            {
                pbCurrentTable.Maximum = progress.TotalRecords;
                pbCurrentTable.Value = progress.CurrentRecord;
                txtCurrentTableProgress.Text = $"{progress.CurrentRecord} of {progress.TotalRecords} records processed";
            }

            // Update current table
            if (!string.IsNullOrEmpty(progress.CurrentTable))
            {
                txtCurrentTable.Text = progress.CurrentTable;
            }

            // Update current project
            if (!string.IsNullOrEmpty(progress.CurrentProject))
            {
                txtCurrentProject.Text = progress.CurrentProject;
            }

            // Update project progress
            if (progress.TotalTables > 0)
            {
                txtProjectProgress.Text = $"{progress.TablesProcessed} of {progress.TotalTables} tables processed";
            }

            // Update status
            txtStatus.Text = progress.Status;

            // Log status changes
            if (!string.IsNullOrEmpty(progress.Status))
            {
                LogMessage(progress.Status);
            }
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";

            Dispatcher.Invoke(() =>
            {
                txtLog.Text += logEntry + Environment.NewLine;
                // Auto-scroll to bottom
                var scrollViewer = txtLog.Parent as System.Windows.Controls.ScrollViewer;
                scrollViewer?.ScrollToBottom();
            });
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to cancel the archive operation?",
                                       "Cancel Archive",
                                       MessageBoxButton.YesNo,
                                       MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _cancellationTokenSource.Cancel();
                LogMessage("Archive cancellation requested by user");
                btnCancel.IsEnabled = false;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cancellationTokenSource.Cancel();
            _timer.Stop();
            base.OnClosed(e);
        }
    }
}
