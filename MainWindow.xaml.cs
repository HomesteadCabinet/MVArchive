using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MVArchive.Models;
using MVArchive.Services;

namespace MVArchive
{
  public partial class MainWindow : Window
  {
    private readonly DatabaseService _databaseService;
    private readonly DispatcherTimer _statusTimer;
    private ArchiveConfig? _archiveConfig; // New field for archive configuration
    private LoggingService _loggingService;

    public MainWindow()
    {
      InitializeComponent();
      _databaseService = new DatabaseService();

      // Create a default logging service since we'll get the configured one from ArchiveConfigWindow
      _loggingService = new LoggingService(Dispatcher);

      // Setup status timer to clear status messages after 5 seconds
      _statusTimer = new DispatcherTimer
      {
        Interval = TimeSpan.FromSeconds(5)
      };
      _statusTimer.Tick += (s, e) =>
      {
        txtStatus.Text = "Ready";
        _statusTimer.Stop();
      };

      // Load data when window loads
      Loaded += MainWindow_Loaded;

      _loggingService.LogSystem("MainWindow initialized", "Application started successfully");

      // Initialize archive config from runtime service
      _archiveConfig = ConfigService.Instance.Current;
      ConfigService.Instance.ConfigurationChanged += (_, cfg) => _archiveConfig = cfg;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
      // Setup the LoggingPanel with the current logging service
      if (loggingPanel != null)
      {
        loggingPanel.SetLoggingService(_loggingService);
      }

      await TestConnectionAsync();
      if (txtConnectionStatus.Text.Contains("Connected"))
      {
        await LoadProjectsAsync();
      }
    }

    private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
      await TestConnectionAsync();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
      await LoadProjectsAsync();
    }

    // New method to open archive configuration window
    private void BtnArchiveConfig_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        _loggingService.LogInfo("Archive", "Opening archive configuration window");
        var configWindow = new ArchiveConfigWindow();
        // Preload window with current runtime config
        if (_archiveConfig != null)
        {
          configWindow.Configuration = _archiveConfig;
        }
        if (configWindow.ShowDialog() == true)
        {
          // Update the logging service with the one from ArchiveConfigWindow
          _loggingService = configWindow.LoggingService;

          // Update the LoggingPanel to use the same service
          loggingPanel.SetLoggingService(_loggingService);

          // Persist to runtime service immediately
          _archiveConfig = configWindow.Configuration;
          ConfigService.Instance.Update(_archiveConfig!);
          txtArchiveStatus.Text = "Archive: Configured";
          txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Green;

          // Enable archive buttons
          btnArchiveSelected.IsEnabled = true;
          btnArchiveAll.IsEnabled = true;

          _loggingService.LogInfo("Archive", "Archive configuration saved",
            $"Source: {_archiveConfig.SourceDatabase}, Destination: {_archiveConfig.DestinationDatabase}, Dry Run: {_archiveConfig.IsDryRun}");
        }
        else
        {
          // If user closed without Save, keep existing runtime config; still allow usage if present
          if (_archiveConfig != null)
          {
            btnArchiveSelected.IsEnabled = true;
            btnArchiveAll.IsEnabled = true;
          }
        }
      }
      catch (Exception ex)
      {
        _loggingService.LogError("Archive", "Error opening archive configuration", ex.ToString());
        MessageBox.Show($"Error opening archive configuration: {ex.Message}",
                "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    // New method to archive selected project
    private async void BtnArchiveSelected_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var selectedProject = dgProjects.SelectedItem as Project;
        if (selectedProject == null)
        {
          MessageBox.Show("Please select a project to archive.", "No Project Selected",
                  MessageBoxButton.OK, MessageBoxImage.Information);
          return;
        }

        // Pull latest runtime config; do not block if window is open/closed
        _archiveConfig = ConfigService.Instance.Current;
        if (_archiveConfig == null)
        {
          _loggingService.LogWarning("Archive", "Archive attempted without configuration");
          MessageBox.Show("Please configure archive settings first.", "Archive Not Configured",
                  MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        var result = MessageBox.Show(
          $"Are you sure you want to archive project '{selectedProject.Name}'?\n\n" +
          $"This will copy all project data and related records to the archive database.\n" +
          $"Dry Run: {(_archiveConfig.IsDryRun ? "Yes (no deletion)" : "No (will delete from source)")}",
          "Confirm Archive",
          MessageBoxButton.YesNo,
          _archiveConfig.IsDryRun ? MessageBoxImage.Question : MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
          btnArchiveSelected.IsEnabled = false;
          btnArchiveAll.IsEnabled = false;
          txtArchiveStatus.Text = "Archive in progress...";
          txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Orange;

          try
          {
            _loggingService.LogInfo("Archive", $"Starting archive for project: {selectedProject.Name}",
              $"Project ID: {selectedProject.Id}, LinkID: {selectedProject.LinkID}");

            // Check if LinkID is available
            if (string.IsNullOrEmpty(selectedProject.LinkID))
            {
              throw new InvalidOperationException($"Project {selectedProject.Name} has no LinkID");
            }

            // Use the LinkID for the archive operation
            await ArchiveProjectAsync(selectedProject.LinkID);

            txtArchiveStatus.Text = "Archive completed successfully!";
            txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Green;

            _loggingService.LogInfo("Archive", "Project archive completed successfully",
              $"Project: {selectedProject.Name}");

            // Refresh the projects list
            await LoadProjectsAsync();
          }
          catch (Exception ex)
          {
            txtArchiveStatus.Text = $"Archive failed: {ex.Message}";
            txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Red;

            _loggingService.LogError("Archive", "Project archive failed", ex.ToString());

            MessageBox.Show($"Archive failed: {ex.Message}", "Archive Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
          }
          finally
          {
            btnArchiveSelected.IsEnabled = true;
            btnArchiveAll.IsEnabled = true;
          }
        }
      }
      catch (Exception ex)
      {
        _loggingService.LogError("Archive", "Error in archive button click", ex.ToString());
        MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    // New method to archive all projects
    private async void BtnArchiveAll_Click(object sender, RoutedEventArgs e)
    {
      _archiveConfig = ConfigService.Instance.Current;
      if (_archiveConfig == null)
      {
        _loggingService.LogWarning("Archive", "Bulk archive attempted without configuration");
        MessageBox.Show("Please configure archive settings first.", "Archive Not Configured",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }

      var result = MessageBox.Show(
        $"Are you sure you want to archive ALL projects?\n\n" +
        $"This will copy all project data and related records to the archive database.\n" +
        $"{(_archiveConfig.IsDryRun ? "DRY RUN: Source data will NOT be deleted." : "Source data WILL be deleted after archiving.")}\n\n" +
        $"This operation may take a long time depending on the amount of data.",
        "Confirm Archive All", MessageBoxButton.YesNo, MessageBoxImage.Warning);

      if (result == MessageBoxResult.Yes)
      {
        _loggingService.LogInfo("Archive", "Starting bulk archive for all projects");
        await ArchiveAllProjectsAsync();
      }
    }

    // Helper method for single project archiving
    private async Task ArchiveProjectAsync(string projectId)
    {
      try
      {
        btnArchiveSelected.IsEnabled = false;
        btnArchiveAll.IsEnabled = false;
        txtArchiveStatus.Text = "Archive: In Progress...";
        txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Orange;

        var archiveService = new ArchiveService(_archiveConfig!, _loggingService);
        var progressWindow = new ArchiveProgressWindow(archiveService);

        progressWindow.Owner = this;
        progressWindow.Show();

        await progressWindow.StartArchiveAsync(projectId);

        txtArchiveStatus.Text = "Archive: Completed";
        txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Green;

        // Refresh the projects list
        await LoadProjectsAsync();
      }
      catch (Exception ex)
      {
        txtArchiveStatus.Text = "Archive: Failed";
        txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Red;
        _loggingService.LogError("Archive", "Project archive failed", ex.ToString());
        MessageBox.Show($"Archive failed: {ex.Message}", "Archive Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
      }
      finally
      {
        btnArchiveSelected.IsEnabled = true;
        btnArchiveAll.IsEnabled = true;
      }
    }

    // Helper method for all projects archiving
    private async Task ArchiveAllProjectsAsync()
    {
      try
      {
        btnArchiveSelected.IsEnabled = false;
        btnArchiveAll.IsEnabled = false;
        txtArchiveStatus.Text = "Archive All: In Progress...";
        txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Orange;

        var archiveService = new ArchiveService(_archiveConfig!, _loggingService);
        var progressWindow = new ArchiveProgressWindow(archiveService);

        progressWindow.Owner = this;
        progressWindow.Show();

        await progressWindow.StartArchiveAsync();

        txtArchiveStatus.Text = "Archive All: Completed";
        txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Green;

        // Refresh the projects list
        await LoadProjectsAsync();
      }
      catch (Exception ex)
      {
        txtArchiveStatus.Text = "Archive All: Failed";
        txtArchiveStatus.Foreground = System.Windows.Media.Brushes.Red;
        _loggingService.LogError("Archive", "Bulk archive failed", ex.ToString());
        MessageBox.Show($"Archive all failed: {ex.Message}", "Archive Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
      }
      finally
      {
        btnArchiveSelected.IsEnabled = true;
        btnArchiveAll.IsEnabled = true;
      }
    }

    private async Task TestConnectionAsync()
    {
      try
      {
        btnTestConnection.IsEnabled = false;
        txtConnectionStatus.Text = "Testing connection...";
        txtStatus.Text = "Testing database connection...";

        _loggingService.LogInfo("Connection", "Testing database connection");

        var isConnected = await _databaseService.TestConnectionAsync();

        if (isConnected)
        {
          txtConnectionStatus.Text = "Connection Status: Connected";
          txtConnectionStatus.Foreground = System.Windows.Media.Brushes.Green;
          txtStatus.Text = "Database connection successful!";
          _loggingService.LogInfo("Connection", "Database connection successful");
        }
        else
        {
          txtConnectionStatus.Text = "Connection Status: Failed";
          txtConnectionStatus.Foreground = System.Windows.Media.Brushes.Red;
          txtStatus.Text = "Database connection failed!";
          _loggingService.LogError("Connection", "Database connection failed");
        }
      }
      catch (Exception ex)
      {
        txtConnectionStatus.Text = "Connection Status: Error";
        txtConnectionStatus.Foreground = System.Windows.Media.Brushes.Red;
        txtStatus.Text = $"Connection error: {ex.Message}";
        _loggingService.LogError("Connection", "Database connection error", ex.ToString());
      }
      finally
      {
        btnTestConnection.IsEnabled = true;
        _statusTimer.Start();
      }
    }

    private async Task LoadProjectsAsync()
    {
      try
      {
        btnRefresh.IsEnabled = false;
        txtStatus.Text = "Loading projects...";
        dgProjects.ItemsSource = null;

        _loggingService.LogInfo("Data", "Loading projects from database");

        var projects = await _databaseService.GetAllProjectsAsync();

        dgProjects.ItemsSource = projects;
        txtRecordCount.Text = $"Records: {projects.Count}";
        txtStatus.Text = $"Loaded {projects.Count} projects successfully!";

        _loggingService.LogInfo("Data", $"Successfully loaded {projects.Count} projects");

        if (projects.Count == 0)
        {
          txtStatus.Text = "No projects found in the database.";
          _loggingService.LogWarning("Data", "No projects found in database");
        }
      }
      catch (Exception ex)
      {
        txtStatus.Text = $"Error loading projects: {ex.Message}";
        txtRecordCount.Text = "Records: Error";

        _loggingService.LogError("Data", "Failed to load projects", ex.ToString());

        // Show error message box
        MessageBox.Show($"Error loading projects:\n{ex.Message}",
                  "Database Error",
                  MessageBoxButton.OK,
                  MessageBoxImage.Error);
      }
      finally
      {
        btnRefresh.IsEnabled = true;
        _statusTimer.Start();
      }
    }
  }
}
