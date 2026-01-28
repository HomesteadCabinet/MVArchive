using System;
using System.Windows;
using System.Windows.Controls;
using MVArchive.Models;
using MVArchive.Services;
using WinForms = System.Windows.Forms;

namespace MVArchive
{
    public partial class ArchiveConfigWindow : Window
    {
        public ArchiveConfig Configuration { get; set; }
        private readonly LoggingService _loggingService;

        public LoggingService LoggingService => _loggingService;

        public ArchiveConfigWindow()
        {
            InitializeComponent();

            // Initialize logging service BEFORE loading UI-bound values that reference it
            _loggingService = new LoggingService(Dispatcher);
            SetupLoggingEventHandlers();

            // Load current configuration from runtime service, falling back to defaults
            Configuration = ConfigService.Instance.Current;
            LoadConfigurationIntoUI(Configuration);
        }

        private void SetupLoggingEventHandlers()
        {
            chkEnableLogging.Checked += (s, e) => _loggingService.IsEnabled = true;
            chkEnableLogging.Unchecked += (s, e) => _loggingService.IsEnabled = false;

            cmbMaxEntries.SelectionChanged += (s, e) =>
            {
                if (cmbMaxEntries.SelectedItem is ComboBoxItem item &&
                    int.TryParse(item.Content.ToString(), out int maxEntries))
                {
                    _loggingService.MaxEntries = maxEntries;
                }
            };

            // Set initial MaxEntries value
            _loggingService.MaxEntries = GetMaxEntriesFromUI();
        }

        private void LoadConfigurationIntoUI(ArchiveConfig cfg)
        {
            txtSourceHost.Text = cfg.SourceHost;
            txtSourcePort.Text = cfg.SourcePort;
            txtSourceDatabase.Text = cfg.SourceDatabase;
            txtSourceUser.Text = cfg.SourceUser;
            txtSourcePassword.Password = cfg.SourcePassword;

            txtDestHost.Text = cfg.DestinationHost;
            txtDestPort.Text = cfg.DestinationPort;
            txtDestDatabase.Text = cfg.DestinationDatabase;
            txtDestUser.Text = cfg.DestinationUser;
            txtDestPassword.Password = cfg.DestinationPassword;

            chkDryRun.IsChecked = cfg.IsDryRun;
            chkOverwriteExisting.IsChecked = cfg.OverwriteExisting;
            chkSkipCatalogTables.IsChecked = cfg.SkipCatalogTables;

            txtFactoryDatabasePath.Text = cfg.FactoryDatabasePath;
            txtProjectFilesDestinationPath.Text = cfg.ProjectFilesDestinationPath;

            // Set the MaxEntries ComboBox selection
            SetMaxEntriesSelection(cfg.MaxEntries);

            // Update the LoggingService MaxEntries property
            _loggingService.MaxEntries = cfg.MaxEntries;
        }

        private void SetMaxEntriesSelection(int maxEntries)
        {
            foreach (ComboBoxItem item in cmbMaxEntries.Items)
            {
                if (int.TryParse(item.Content.ToString(), out int value) && value == maxEntries)
                {
                    cmbMaxEntries.SelectedItem = item;
                    break;
                }
            }
        }

        private int GetMaxEntriesFromUI()
        {
            if (cmbMaxEntries.SelectedItem is ComboBoxItem item && int.TryParse(item.Content.ToString(), out int value))
            {
                return value;
            }
            return 500; // Default fallback
        }


        private async void BtnTestConnections_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                btnTestConnections.IsEnabled = false;
                txtConnectionStatus.Text = "Testing connections...";

                var config = GetConfigurationFromUI();
                var archiveService = new Services.ArchiveService(config);

                var connectionsOk = await archiveService.TestConnectionsAsync();

                if (connectionsOk)
                {
                    txtConnectionStatus.Text = "✓ Both connections successful!";
                    txtConnectionStatus.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    txtConnectionStatus.Text = "✗ Connection test failed. Check your settings.";
                    txtConnectionStatus.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                txtConnectionStatus.Text = $"✗ Connection test error: {ex.Message}";
                txtConnectionStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                btnTestConnections.IsEnabled = true;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Configuration = GetConfigurationFromUI();
                // Update runtime configuration so it exists immediately without keeping the window open
                ConfigService.Instance.Update(Configuration);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Configuration Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnBrowseFactoryPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var dialog = new WinForms.FolderBrowserDialog
                {
                    Description = "Select Factory Database folder",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false
                };
                if (!string.IsNullOrWhiteSpace(txtFactoryDatabasePath.Text))
                {
                    dialog.InitialDirectory = txtFactoryDatabasePath.Text;
                }
                var result = dialog.ShowDialog();
                if (result == WinForms.DialogResult.OK)
                {
                    txtFactoryDatabasePath.Text = dialog.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBrowseDestinationPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var dialog = new WinForms.FolderBrowserDialog
                {
                    Description = "Select Project Files Destination folder",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };
                if (!string.IsNullOrWhiteSpace(txtProjectFilesDestinationPath.Text))
                {
                    dialog.InitialDirectory = txtProjectFilesDestinationPath.Text;
                }
                var result = dialog.ShowDialog();
                if (result == WinForms.DialogResult.OK)
                {
                    txtProjectFilesDestinationPath.Text = dialog.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ArchiveConfig GetConfigurationFromUI()
        {
            return new ArchiveConfig
            {
                SourceHost = txtSourceHost.Text.Trim(),
                SourcePort = txtSourcePort.Text.Trim(),
                SourceDatabase = txtSourceDatabase.Text.Trim(),
                SourceUser = txtSourceUser.Text.Trim(),
                SourcePassword = txtSourcePassword.Password,
                DestinationHost = txtDestHost.Text.Trim(),
                DestinationPort = txtDestPort.Text.Trim(),
                DestinationDatabase = txtDestDatabase.Text.Trim(),
                DestinationUser = txtDestUser.Text.Trim(),
                DestinationPassword = txtDestPassword.Password,
                IsDryRun = chkDryRun.IsChecked ?? true,
                OverwriteExisting = chkOverwriteExisting.IsChecked ?? false,
                SkipCatalogTables = chkSkipCatalogTables.IsChecked ?? false,
                FactoryDatabasePath = txtFactoryDatabasePath.Text.Trim(),
                ProjectFilesDestinationPath = txtProjectFilesDestinationPath.Text.Trim(),
                MaxEntries = GetMaxEntriesFromUI() // Read the selected value
            };
        }
    }
}
