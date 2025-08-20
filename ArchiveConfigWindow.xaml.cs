using System;
using System.Windows;
using System.Windows.Controls;
using MVArchive.Models;
using MVArchive.Services;

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
            // Load current configuration from runtime service, falling back to defaults
            Configuration = ConfigService.Instance.Current;
            LoadConfigurationIntoUI(Configuration);
            LoadRelatedTablesInfo();

            // Initialize logging service
            _loggingService = new LoggingService(Dispatcher);
            SetupLoggingEventHandlers();
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
        }

        private void LoadRelatedTablesInfo()
        {
            var relatedTables = new[]
            {
                "Projects (Main table)",
                "Locations (Project locations)",
                "Products (Project products)",
                "Subassemblies (Project subassemblies)",
                "Hardware (Project hardware)",
                "Edgebanding (Project edgebanding)",
                "AutoCADDrawings (Project drawings)",
                "TiffDrawings (Project TIFF drawings)",
                "CutPartsFiles (Project cut parts files)",
                "DoorWizardFiles (Project door wizard files)",
                "EdgebandFiles (Project edgeband files)",
                "GlobalFiles (Project global files)",
                "HardwareFiles (Project hardware files)",
                "ProjectWizardFiles (Project wizard files)",
                "Activities (Project activities)",
                "Bundles (Project bundles)",
                "BundleItems (Project bundle items)",
                "BluePrintViews (Project blueprint views)",
                "FaceFrameImages (Project face frame images)",
                "Correspondence (Project correspondence)",
                "PurchaseOrders (Project purchase orders)",
                "WorkOrders (Project work orders)",
                "WorkOrderBatches (Project work order batches)",
                "WorkOrderItems (Project work order items)"
            };

            txtRelatedTables.Text = string.Join("\n• ", relatedTables);
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
                IsDryRun = chkDryRun.IsChecked ?? true
            };
        }
    }
}
