using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private ArchiveConfig? _archiveConfig;
    private LoggingService _loggingService;
    private List<Project> _allProjects = new List<Project>();
    private readonly ObservableCollection<Project> _selectedForArchive = new ObservableCollection<Project>();
    private readonly ObservableCollection<Project> _availableFiltered = new ObservableCollection<Project>();
    private readonly ObservableCollection<Project> _destinationFiltered = new ObservableCollection<Project>();

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
      loggingPanel?.SetLoggingService(_loggingService);
      dgAvailable.ItemsSource = _availableFiltered;
      dgSelected.ItemsSource = _destinationFiltered;
      await TestConnectionAsync();
      await LoadProjectsAsync();
    }

    private void Filter_Changed(object sender, EventArgs e)
    {
      ApplyFilters();
    }

    private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
    {
      txtFilterName.Text = string.Empty;
      dpFilterDateFrom.SelectedDate = null;
      dpFilterDateTo.SelectedDate = null;
      ApplyFilters();
    }

    private void ApplyFilters()
    {
      var nameFilter = (txtFilterName?.Text ?? string.Empty).Trim();
      var dateFrom = dpFilterDateFrom?.SelectedDate;
      var dateTo = dpFilterDateTo?.SelectedDate;
      // When Dry Run is ON, do NOT hide already-archived projects (user wants to see them in the source list).
      // When Dry Run is OFF, exclude source projects that are already in the destination (archive) by LinkID.
      var isDryRun = _archiveConfig?.IsDryRun ?? true;
      var destinationLinkIds = isDryRun
        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(
          _selectedForArchive.Select(p => p.LinkID).Where(id => !string.IsNullOrEmpty(id)).Cast<string>(),
          StringComparer.OrdinalIgnoreCase);

      _availableFiltered.Clear();
      foreach (var p in _allProjects)
      {
        if (!string.IsNullOrEmpty(p.LinkID) && destinationLinkIds.Contains(p.LinkID))
          continue;
        if (!MatchesNameAndDateFilter(p, nameFilter, dateFrom, dateTo))
          continue;
        _availableFiltered.Add(p);
      }

      _destinationFiltered.Clear();
      foreach (var p in _selectedForArchive)
      {
        if (!MatchesNameAndDateFilter(p, nameFilter, dateFrom, dateTo))
          continue;
        _destinationFiltered.Add(p);
      }
      UpdateStatusCounts();
    }

    private static bool MatchesNameAndDateFilter(Project p, string nameFilter, DateTime? dateFrom, DateTime? dateTo)
    {
      if (!string.IsNullOrEmpty(nameFilter) &&
          (string.IsNullOrEmpty(p.Name) || p.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0))
        return false;
      if (dateFrom.HasValue && (p.ScheduledStartDate == null || p.ScheduledStartDate.Value.Date < dateFrom.Value.Date))
        return false;
      if (dateTo.HasValue && (p.ScheduledStartDate == null || p.ScheduledStartDate.Value.Date > dateTo.Value.Date))
        return false;
      return true;
    }

    private void UpdateStatusCounts()
    {
      txtRecordCount.Text = $"Available: {_availableFiltered.Count} | In archive: {_destinationFiltered.Count}";
    }

    private static string? BuildDestinationConnectionString(ArchiveConfig? config)
    {
      if (config == null || string.IsNullOrWhiteSpace(config.DestinationDatabase))
        return null;
      var host = string.IsNullOrWhiteSpace(config.DestinationHost) ? "localhost" : config.DestinationHost;
      var port = string.IsNullOrWhiteSpace(config.DestinationPort) ? "1433" : config.DestinationPort;
      var user = config.DestinationUser ?? "sa";
      var password = config.DestinationPassword ?? "";
      return $"Server={host},{port};Database={config.DestinationDatabase};User Id={user};Password={password};TrustServerCertificate=true;";
    }

    private async Task LoadDestinationProjectsAsync()
    {
      _archiveConfig = ConfigService.Instance.Current;
      var connStr = BuildDestinationConnectionString(_archiveConfig);
      if (string.IsNullOrWhiteSpace(connStr))
      {
        _selectedForArchive.Clear();
        ApplyFilters();
        return;
      }
      try
      {
        var destProjects = await _databaseService.GetAllProjectsFromConnectionAsync(connStr);
        _selectedForArchive.Clear();
        foreach (var p in destProjects)
          _selectedForArchive.Add(p);
        ApplyFilters();
        _loggingService.LogInfo("Data", $"Loaded {destProjects.Count} projects from destination (archive)");
      }
      catch (Exception ex)
      {
        _loggingService.LogWarning("Data", "Could not load destination projects", ex.Message);
        _selectedForArchive.Clear();
        ApplyFilters();
      }
    }

    private void BtnMoveRight_Click(object sender, RoutedEventArgs e)
    {
      // Move right = archive selected from left pane
      BtnArchiveSelected_Click(sender, e);
    }

    private void BtnMoveLeft_Click(object sender, RoutedEventArgs e)
    {
      // Right pane is read-only from destination; move left has no effect
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

          btnArchiveSelected.IsEnabled = true;

          _loggingService.LogInfo("Archive", "Archive configuration saved",
            $"Source: {_archiveConfig.SourceDatabase}, Destination: {_archiveConfig.DestinationDatabase}, Dry Run: {_archiveConfig.IsDryRun}");
        }
        else
        {
          // If user closed without Save, keep existing runtime config; still allow usage if present
          if (_archiveConfig != null)
            btnArchiveSelected.IsEnabled = true;
        }
      }
      catch (Exception ex)
      {
        _loggingService.LogError("Archive", "Error opening archive configuration", ex.ToString());
        MessageBox.Show($"Error opening archive configuration: {ex.Message}",
                "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private async void BtnArchiveSelected_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        // Archive projects selected in the left pane (available / source)
        var selectedItems = dgAvailable.SelectedItems?.Cast<Project>().ToList() ?? new List<Project>();
        if (selectedItems.Count == 0)
        {
          MessageBox.Show("Please select one or more projects in the left pane (Available) to archive.", "No Project Selected",
                  MessageBoxButton.OK, MessageBoxImage.Information);
          return;
        }

        _archiveConfig = ConfigService.Instance.Current;
        if (_archiveConfig == null)
        {
          _loggingService.LogWarning("Archive", "Archive attempted without configuration");
          MessageBox.Show("Please configure archive settings first.", "Archive Not Configured",
                  MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        var confirmMsg = selectedItems.Count == 1
          ? $"Are you sure you want to archive project '{selectedItems[0].Name}'?\n\n"
          : $"Are you sure you want to archive {selectedItems.Count} selected projects?\n\n";

        var result = MessageBox.Show(
          confirmMsg +
          $"This will copy all project data and related records to the archive database.\n" +
          $"Dry Run: {(_archiveConfig.IsDryRun ? "Yes (no deletion)" : "No (will delete from source)")}",
          "Confirm Archive",
          MessageBoxButton.YesNo,
          _archiveConfig.IsDryRun ? MessageBoxImage.Question : MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
          btnArchiveSelected.IsEnabled = false;

          try
          {
            var linkIds = selectedItems.Select(p => p.LinkID).Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToList();
            if (linkIds.Count != selectedItems.Count)
            {
              throw new InvalidOperationException("One or more selected projects have no LinkID");
            }

            var archiveService = new ArchiveService(_archiveConfig!, _loggingService, ShowOverwriteConfirmation);
            var progressWindow = new ArchiveProgressWindow(archiveService);
            progressWindow.Owner = this;
            progressWindow.Show();

            await progressWindow.StartArchiveAsync(linkIds);

            await LoadDestinationProjectsAsync();
            ApplyFilters();
            await LoadProjectsAsync();
          }
          catch (Exception ex)
          {
            _loggingService.LogError("Archive", "Project archive failed", ex.ToString());

            MessageBox.Show($"Archive failed: {ex.Message}", "Archive Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
          }
          finally
          {
            btnArchiveSelected.IsEnabled = true;
          }
        }
      }
      catch (Exception ex)
      {
        _loggingService.LogError("Archive", "Error in archive button click", ex.ToString());
        MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private bool ShowOverwriteConfirmation(string message)
    {
      // This method will be called from a background thread, so we need to invoke on the UI thread
      return Dispatcher.Invoke(() =>
      {
        var result = MessageBox.Show(
          message,
          "Overwrite Confirmation",
          MessageBoxButton.YesNo,
          MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
      });
    }

    private async Task TestConnectionAsync()
    {
      try
      {
        btnTestConnection.IsEnabled = false;
        txtStatus.Text = "Testing database connection...";

        _loggingService.LogInfo("Connection", "Testing database connection");

        var isConnected = await _databaseService.TestConnectionAsync();

        if (isConnected)
        {
          txtStatus.Text = "Database connection successful!";
          _loggingService.LogInfo("Connection", "Database connection successful");
        }
        else
        {
          txtStatus.Text = "Database connection failed!";
          _loggingService.LogError("Connection", "Database connection failed");
        }
      }
      catch (Exception ex)
      {
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

        _loggingService.LogInfo("Data", "Loading projects from database");

        _allProjects = await _databaseService.GetAllProjectsAsync();
        await LoadDestinationProjectsAsync();
        ApplyFilters();
        txtStatus.Text = $"Loaded {_allProjects.Count} projects successfully!";

        _loggingService.LogInfo("Data", $"Successfully loaded {_allProjects.Count} projects");

        if (_allProjects.Count == 0)
        {
          txtStatus.Text = "No projects found in the database.";
          _loggingService.LogWarning("Data", "No projects found in database");
        }
      }
      catch (Exception ex)
      {
        txtStatus.Text = $"Error loading projects: {ex.Message}";
        _allProjects = new List<Project>();
        await LoadDestinationProjectsAsync();
        ApplyFilters();

        _loggingService.LogError("Data", "Failed to load projects", ex.ToString());

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

    private async void BtnClearArchive_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        _archiveConfig = ConfigService.Instance.Current;
        if (_archiveConfig == null || string.IsNullOrWhiteSpace(_archiveConfig.DestinationDatabase))
        {
          _loggingService.LogWarning("Archive", "Clear archive attempted without configuration");
          MessageBox.Show("Please configure archive settings first.", "Archive Not Configured",
                  MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        // First confirmation
        var firstWarning = MessageBox.Show(
          $"WARNING: You are about to delete ALL data from the archive database:\n\n" +
          $"Database: {_archiveConfig.DestinationDatabase}\n" +
          $"Host: {_archiveConfig.DestinationHost ?? "localhost"}\n\n" +
          $"This action cannot be undone!\n\n" +
          $"Are you sure you want to continue?",
          "Clear Archive Database - First Warning",
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning);

        if (firstWarning != MessageBoxResult.Yes)
        {
          _loggingService.LogInfo("Archive", "Clear archive cancelled by user (first warning)");
          return;
        }

        // Second confirmation
        var secondWarning = MessageBox.Show(
          $"FINAL WARNING!\n\n" +
          $"This will permanently delete ALL data from:\n{_archiveConfig.DestinationDatabase}\n\n" +
          $"Type 'YES' to confirm (this is your last chance):",
          "Clear Archive Database - Final Confirmation",
          MessageBoxButton.YesNo,
          MessageBoxImage.Stop);

        if (secondWarning != MessageBoxResult.Yes)
        {
          _loggingService.LogInfo("Archive", "Clear archive cancelled by user (second warning)");
          return;
        }

        btnClearArchive.IsEnabled = false;
        txtStatus.Text = "Clearing archive database...";

        try
        {
          var connStr = BuildDestinationConnectionString(_archiveConfig);
          if (string.IsNullOrWhiteSpace(connStr))
          {
            throw new InvalidOperationException("Could not build destination connection string");
          }

          _loggingService.LogWarning("Archive", $"Clearing all data from archive database: {_archiveConfig.DestinationDatabase}");

          await _databaseService.ClearAllDataAsync(connStr);

          txtStatus.Text = "Archive database cleared successfully!";
          _loggingService.LogInfo("Archive", "Archive database cleared successfully");

          MessageBox.Show("Archive database has been cleared successfully.", "Clear Complete",
                  MessageBoxButton.OK, MessageBoxImage.Information);

          // Refresh the destination data grid
          await LoadDestinationProjectsAsync();
          ApplyFilters();
        }
        catch (Exception ex)
        {
          _loggingService.LogError("Archive", "Failed to clear archive database", ex.ToString());
          txtStatus.Text = $"Error clearing archive: {ex.Message}";

          MessageBox.Show($"Failed to clear archive database:\n{ex.Message}", "Clear Archive Error",
                  MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
          btnClearArchive.IsEnabled = true;
          _statusTimer.Start();
        }
      }
      catch (Exception ex)
      {
        _loggingService.LogError("Archive", "Error in clear archive button click", ex.ToString());
        MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
  }
}
