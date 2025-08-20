using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MVArchive.Models;

namespace MVArchive.Services
{
  public class ArchiveService
  {
    private readonly ArchiveConfig _config;
    private readonly string _sourceConnectionString;
    private readonly string _destinationConnectionString;
    private readonly LoggingService? _loggingService;

    // Tables that are directly linked to projects
    private readonly string[] _projectRelatedTables = {
      "Locations", "Products", "Subassemblies", "Hardware", "Edgebanding",
      "CutPartsFiles", "DoorWizardFiles",
      "EdgebandFiles", "GlobalFiles", "HardwareFiles", "ProjectWizardFiles",
      "Activities",
    };

    // Tables that contain binary data (drawings, files)
    private readonly string[] _binaryDataTables = {
      "AutoCADDrawings", "TiffDrawings", "CutPartsFiles", "DoorWizardFiles",
      "EdgebandFiles", "GlobalFiles", "HardwareFiles", "ProjectWizardFiles",
      "Attachment", "FaceFrameImages", "GlobalImages"
    };

    public ArchiveService(ArchiveConfig config, LoggingService? loggingService = null)
    {
      _config = config;
      _loggingService = loggingService;

      _sourceConnectionString = $"Server={_config.SourceHost},{_config.SourcePort};Database={_config.SourceDatabase};User Id={_config.SourceUser};Password={_config.SourcePassword};TrustServerCertificate=true;";
      _destinationConnectionString = $"Server={_config.DestinationHost},{_config.DestinationPort};Database={_config.DestinationDatabase};User Id={_config.DestinationUser};Password={_config.DestinationPassword};TrustServerCertificate=true;";

      _loggingService?.LogSystem("ArchiveService initialized",
        $"Source: {_config.SourceHost}:{_config.SourcePort}/{_config.SourceDatabase}\n" +
        $"Destination: {_config.DestinationHost}:{_config.DestinationPort}/{_config.DestinationDatabase}\n" +
        $"Dry Run: {_config.IsDryRun}");
    }

    public async Task<ArchiveProgress> ArchiveProjectAsync(string projectLinkId, IProgress<ArchiveProgress>? progress = null)
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Starting project archive", $"Project LinkID: {projectLinkId}");

      var archiveProgress = new ArchiveProgress
      {
        StartTime = DateTime.Now,
        TotalTables = _projectRelatedTables.Length + 1, // +1 for Projects table
        TotalProjects = 1
      };

      try
      {
        // Check if project already exists in destination
        using var sourceConn = new SqlConnection(_sourceConnectionString);
        await sourceConn.OpenAsync();

        var existsQuery = "SELECT COUNT(*) FROM Projects WHERE LinkID = @ProjectLinkId";
        using var existsCmd = new SqlCommand(existsQuery, sourceConn);
        existsCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

        var existsResult = await existsCmd.ExecuteScalarAsync();
        var exists = existsResult != null ? Convert.ToInt32(existsResult) : 0;

        if (exists > 0)
        {
          _loggingService?.LogWarning("Archive", "Project already exists in destination", $"Project LinkID: {projectLinkId}");
          return new ArchiveProgress { Status = "Project already archived", IsComplete = true };
        }

        // Copy the project record
        using var destConn = new SqlConnection(_destinationConnectionString);
        await destConn.OpenAsync();

        var copyQuery = "INSERT INTO dbo.Projects SELECT * FROM Projects WHERE LinkID = @ProjectLinkId";
        using var copyCmd = new SqlCommand(copyQuery, destConn);
        copyCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

        var rowsAffected = await copyCmd.ExecuteNonQueryAsync();

        _loggingService?.LogInfo("Archive", "Project record copied", $"Rows affected: {rowsAffected}");

        // Archive all related data using the LinkID
        await ArchiveRelatedTableAsync("Locations", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Products", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Subassemblies", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Parts", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Hardware", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Edgebanding", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("AutoCADDrawings", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("CutPartsFiles", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("EdgebandFiles", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("HardwareFiles", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("DoorWizardFiles", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("GlobalFiles", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Factory", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Bundles", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("BundleItems", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("WorkOrders", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Activities", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("BluePrintViews", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("FaceFrameImages", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("FaceFrameImagesParts", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("FaceFrameImagesSubassemblies", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Prompts", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("PromptMap", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("PurchaseOrders", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Estimates", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Correspondence", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("EventLogs", projectLinkId, archiveProgress, progress);
        await ArchiveRelatedTableAsync("Attachment", projectLinkId, archiveProgress, progress);

        // Delete from source if not dry run
        if (!_config.IsDryRun)
        {
          _loggingService?.LogWarning("Archive", "Starting source cleanup (not dry run)", $"Project LinkID: {projectLinkId}");
          await DeleteFromSourceAsync(projectLinkId, archiveProgress, progress);
        }
        else
        {
          _loggingService?.LogInfo("Archive", "Dry run completed - no source cleanup", $"Project LinkID: {projectLinkId}");
        }

        stopwatch.Stop();
        archiveProgress.Status = "Archive completed successfully";
        archiveProgress.IsComplete = true;
        archiveProgress.EndTime = DateTime.Now;

        _loggingService?.LogArchive("Project archive completed",
          $"Project LinkID: {projectLinkId}, Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");

        return archiveProgress;
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        archiveProgress.Status = $"Archive failed: {ex.Message}";
        archiveProgress.IsComplete = true;
        archiveProgress.EndTime = DateTime.Now;

        _loggingService?.LogError("Archive", "Project archive failed",
          $"Project LinkID: {projectLinkId}, Error: {ex.Message}, Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
        throw;
      }
    }

    public async Task<ArchiveProgress> ArchiveAllProjectsAsync(IProgress<ArchiveProgress>? progress = null)
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Starting bulk archive", "All projects");

      var archiveProgress = new ArchiveProgress
      {
        StartTime = DateTime.Now,
        TotalTables = _projectRelatedTables.Length + 1,
        TotalProjects = await GetProjectCountAsync()
      };

      try
      {
        var projectIds = await GetProjectIdsAsync();
        _loggingService?.LogInfo("Archive", $"Found {projectIds.Count} projects to archive");

        foreach (var projectId in projectIds)
        {
          archiveProgress.CurrentProject = projectId.ToString();
          archiveProgress.ProjectsProcessed++;
          progress?.Report(archiveProgress);

          _loggingService?.LogArchive($"Processing project {archiveProgress.ProjectsProcessed} of {archiveProgress.TotalProjects}",
            $"Project ID: {projectId}");
          await ArchiveProjectAsync(projectId.ToString(), null); // Don't report progress for individual projects
        }

        stopwatch.Stop();
        archiveProgress.IsComplete = true;
        archiveProgress.EndTime = DateTime.Now;
        archiveProgress.Status = _config.IsDryRun ? "All projects archived (Dry Run)" : "All projects archived and source cleaned";

        _loggingService?.LogArchive("Bulk archive completed",
          $"Total Projects: {archiveProgress.TotalProjects}\n" +
          $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s\n" +
          $"Status: {archiveProgress.Status}");
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        archiveProgress.Status = $"Archive failed: {ex.Message}";
        archiveProgress.EndTime = DateTime.Now;

        _loggingService?.LogError("Archive", $"Bulk archive failed after {stopwatch.Elapsed.TotalSeconds:F2}s",
          $"Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
      }

      progress?.Report(archiveProgress);
      return archiveProgress;
    }

    private async Task ArchiveProjectRecordAsync(Guid projectId, ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      progress.CurrentTable = "Projects";
      progress.Status = $"Archiving project {projectId}";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Archiving project record", $"Project ID: {projectId}");

      using var sourceConn = new SqlConnection(_sourceConnectionString);
      using var destConn = new SqlConnection(_destinationConnectionString);

      await sourceConn.OpenAsync();
      await destConn.OpenAsync();

      // Check if project exists in destination
      var existsQuery = "SELECT COUNT(*) FROM Projects WHERE ID = @ProjectId";
      using var existsCmd = new SqlCommand(existsQuery, destConn);
      existsCmd.Parameters.AddWithValue("@ProjectId", projectId);

      var existsResult = await existsCmd.ExecuteScalarAsync();
      var exists = existsResult != null ? Convert.ToInt32(existsResult) : 0;

      _loggingService?.LogDatabase("Check project exists", existsQuery, $"@ProjectId={projectId}", stopwatch.Elapsed);

      if (exists == 0)
      {
        // Copy project record
        var copyQuery = "INSERT INTO dbo.Projects SELECT * FROM Projects WHERE ID = @ProjectId";
        using var copyCmd = new SqlCommand(copyQuery, destConn);
        copyCmd.Parameters.AddWithValue("@ProjectId", projectId);

        var rowsAffected = await copyCmd.ExecuteNonQueryAsync();
        stopwatch.Stop();

        _loggingService?.LogDatabase("Copy project record", copyQuery, $"@ProjectId={projectId}", stopwatch.Elapsed);
        _loggingService?.LogArchive("Project record copied", $"Rows affected: {rowsAffected}");
      }
      else
      {
        stopwatch.Stop();
        _loggingService?.LogInfo("Archive", "Project already exists in destination", $"Project ID: {projectId}");
      }
    }

    private async Task ArchiveRelatedTableAsync(string tableName, string projectLinkId, ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      progress.Status = $"Archiving {tableName} for project {projectLinkId}";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive($"Starting table archive: {tableName}", $"Project LinkID: {projectLinkId}");

      try
      {
        using var sourceConn = new SqlConnection(_sourceConnectionString);
        using var destConn = new SqlConnection(_destinationConnectionString);

        await sourceConn.OpenAsync();
        await destConn.OpenAsync();

        // Get count of records to archive
        var countQuery = $"SELECT COUNT(*) FROM {tableName} WHERE LinkIDProject = @ProjectLinkId";
        using var countCmd = new SqlCommand(countQuery, sourceConn);
        countCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

        var countResult = await countCmd.ExecuteScalarAsync();
        var recordCount = countResult != null ? Convert.ToInt32(countResult) : 0;

        _loggingService?.LogDatabase("Count records", countQuery, $"@ProjectLinkId={projectLinkId}", stopwatch.Elapsed);

        if (recordCount > 0)
        {
          progress.TotalRecords = recordCount;
          progress.CurrentRecord = 0;

          _loggingService?.LogArchive($"Found {recordCount} records in {tableName}", $"Project LinkID: {projectLinkId}");

          // Check if table exists in destination, if not create it
          await EnsureTableExistsAsync(destConn, tableName, sourceConn);

          // Archive records in batches
          var batchSize = 1000;
          var offset = 0;
          var totalProcessed = 0;

          while (offset < recordCount)
          {
            var batchQuery = $@"
              SELECT * FROM {tableName}
              WHERE LinkIDProject = @ProjectLinkId
              ORDER BY ID
              OFFSET @Offset ROWS
              FETCH NEXT @BatchSize ROWS ONLY";

            using var batchCmd = new SqlCommand(batchQuery, sourceConn);
            batchCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);
            batchCmd.Parameters.AddWithValue("@Offset", offset);
            batchCmd.Parameters.AddWithValue("@BatchSize", batchSize);

            var batchStopwatch = Stopwatch.StartNew();
            using var reader = await batchCmd.ExecuteReaderAsync();
            var dataTable = new DataTable();
            dataTable.Load(reader);
            batchStopwatch.Stop();

            if (dataTable.Rows.Count > 0)
            {
              var bulkCopyStopwatch = Stopwatch.StartNew();
              await BulkCopyToDestinationAsync(destConn, tableName, dataTable);
              bulkCopyStopwatch.Stop();

              totalProcessed += dataTable.Rows.Count;
              _loggingService?.LogDatabase("Batch query", batchQuery,
                $"@ProjectLinkId={projectLinkId}, @Offset={offset}, @BatchSize={batchSize}", batchStopwatch.Elapsed);
              _loggingService?.LogArchive($"Batch processed",
                $"Table: {tableName}, Batch: {dataTable.Rows.Count} records, Total: {totalProcessed}/{recordCount}",
                totalProcessed, recordCount);
            }

            offset += batchSize;
            progress.CurrentRecord = Math.Min(offset, recordCount);
            progressReporter?.Report(progress);
          }

          stopwatch.Stop();
          _loggingService?.LogArchive($"Table {tableName} completed",
            $"Total records: {recordCount}, Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
        }
        else
        {
          stopwatch.Stop();
          _loggingService?.LogInfo("Archive", $"No records found in {tableName}", $"Project LinkID: {projectLinkId}");
        }
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        progress.Status = $"Error archiving {tableName}: {ex.Message}";
        progressReporter?.Report(progress);

        _loggingService?.LogError("Archive", $"Failed to archive table {tableName}",
          $"Project LinkID: {projectLinkId}\nError: {ex.Message}\nDuration: {stopwatch.Elapsed.TotalSeconds:F2}s");
        throw;
      }
    }

    private async Task EnsureTableExistsAsync(SqlConnection destConn, string tableName, SqlConnection sourceConn)
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive($"Checking if table exists: {tableName}", $"Ensuring destination has table {tableName}");

      // Check if table exists in destination
      var tableExistsQuery = @"
        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = 'dbo'";

      using var tableExistsCmd = new SqlCommand(tableExistsQuery, destConn);
      tableExistsCmd.Parameters.AddWithValue("@TableName", tableName);
      var tableExistsResult = await tableExistsCmd.ExecuteScalarAsync();
      var tableExists = tableExistsResult != null ? Convert.ToInt32(tableExistsResult) : 0;

      _loggingService?.LogDatabase("Check table exists", tableExistsQuery, $"@TableName={tableName}", stopwatch.Elapsed);

      if (tableExists == 0)
      {
        _loggingService?.LogArchive($"Creating table: {tableName}", $"Creating destination table if missing");

        // Get table creation script from source
        var createTableQuery = $@"
          SELECT
            'CREATE TABLE dbo.{tableName} (' +
            STRING_AGG(
              QUOTENAME(COLUMN_NAME) + ' ' +
              DATA_TYPE +
              CASE
                WHEN CHARACTER_MAXIMUM_LENGTH IS NOT NULL THEN '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR) + ')'
                WHEN DATA_TYPE IN ('decimal', 'numeric') THEN '(' + CAST(NUMERIC_PRECISION AS VARCHAR) + ',' + CAST(NUMERIC_SCALE AS VARCHAR) + ')'
                ELSE ''
              END +
              CASE WHEN IS_NULLABLE = 'NO' THEN ' NOT NULL' ELSE ' NULL' END +
              CASE WHEN COLUMN_DEFAULT IS NOT NULL THEN ' DEFAULT ' + COLUMN_DEFAULT ELSE '' END,
              ', '
            ) WITHIN GROUP (ORDER BY ORDINAL_POSITION) + ')' as CreateScript
          FROM INFORMATION_SCHEMA.COLUMNS
          WHERE TABLE_NAME = @TableName AND TABLE_SCHEMA = 'dbo'";

        using var createTableCmd = new SqlCommand(createTableQuery, sourceConn);
        createTableCmd.Parameters.AddWithValue("@TableName", tableName);
        var createScript = await createTableCmd.ExecuteScalarAsync() as string;

        if (!string.IsNullOrEmpty(createScript))
        {
          using var createDestTableCmd = new SqlCommand(createScript, destConn);
          await createDestTableCmd.ExecuteNonQueryAsync();

          stopwatch.Stop();
          _loggingService?.LogDatabase("Create table", createScript, $"Table: {tableName}", stopwatch.Elapsed);
          _loggingService?.LogArchive($"Table created successfully: {tableName}", $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
        }
        else
        {
          stopwatch.Stop();
          _loggingService?.LogWarning("Archive", $"Failed to generate create script for table: {tableName}");
        }
      }
      else
      {
        stopwatch.Stop();
        _loggingService?.LogInfo("Archive", $"Table already exists: {tableName}");
      }
    }

    private async Task BulkCopyToDestinationAsync(SqlConnection destConn, string tableName, DataTable dataTable)
    {
      using var bulkCopy = new SqlBulkCopy(destConn)
      {
        DestinationTableName = $"dbo.{tableName}",
        BatchSize = 1000
      };

      // Map all columns
      foreach (DataColumn column in dataTable.Columns)
      {
        if (column.ColumnName != null)
        {
          bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }
      }

      await bulkCopy.WriteToServerAsync(dataTable);
    }

    private async Task DeleteFromSourceAsync(string projectLinkId, ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      progress.Status = "Cleaning source database (not dry run)";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogWarning("Archive", "Starting source cleanup", $"Project LinkID: {projectLinkId}");

      using var sourceConn = new SqlConnection(_sourceConnectionString);
      await sourceConn.OpenAsync();

      // Delete in reverse order to handle foreign key constraints
      var tablesToDelete = _projectRelatedTables.Reverse().ToList();
      tablesToDelete.Add("Projects");

      foreach (var tableName in tablesToDelete)
      {
        var deleteQuery = $"DELETE FROM {tableName} WHERE LinkIDProject = @ProjectLinkId";
        using var deleteCmd = new SqlCommand(deleteQuery, sourceConn);
        deleteCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

        var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();

        _loggingService?.LogDatabase("Delete from source", deleteQuery, $"@ProjectLinkId={projectLinkId}", null);
        _loggingService?.LogArchive($"Deleted from {tableName}", $"Rows affected: {rowsAffected}");
      }

      stopwatch.Stop();
      _loggingService?.LogArchive("Source cleanup completed", $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
    }

    private async Task<int> GetProjectCountAsync()
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Counting total projects", "Counting rows in Projects table");

      using var sourceConn = new SqlConnection(_sourceConnectionString);
      await sourceConn.OpenAsync();

      var countQuery = "SELECT COUNT(*) FROM Projects";
      using var countCmd = new SqlCommand(countQuery, sourceConn);
      var countResult = await countCmd.ExecuteScalarAsync();
      var count = countResult != null ? Convert.ToInt32(countResult) : 0;

      stopwatch.Stop();
      _loggingService?.LogDatabase("Count projects", countQuery, null, stopwatch.Elapsed);
      _loggingService?.LogArchive($"Project count retrieved", $"Total projects: {count}");

      return count;
    }

    private async Task<List<Guid>> GetProjectIdsAsync()
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Retrieving project IDs", "Fetching all project IDs from Projects table");

      var projectIds = new List<Guid>();

      using var sourceConn = new SqlConnection(_sourceConnectionString);
      await sourceConn.OpenAsync();

      var query = "SELECT LinkID FROM Projects";
      using var cmd = new SqlCommand(query, sourceConn);
      using var reader = await cmd.ExecuteReaderAsync();

      while (await reader.ReadAsync())
      {
        projectIds.Add(reader.GetGuid("LinkID"));
      }

      stopwatch.Stop();
      _loggingService?.LogDatabase("Get project IDs", query, null, stopwatch.Elapsed);
      _loggingService?.LogArchive($"Project IDs retrieved", $"Count: {projectIds.Count}");

      return projectIds;
    }

    public async Task<bool> TestConnectionsAsync()
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogSystem("Testing database connections");

      try
      {
        // Test source connection
        using var sourceConn = new SqlConnection(_sourceConnectionString);
        await sourceConn.OpenAsync();
        _loggingService?.LogInfo("Connection", "Source database connection successful",
          $"Host: {_config.SourceHost}:{_config.SourcePort}, Database: {_config.SourceDatabase}");

        // Test destination connection
        using var destConn = new SqlConnection(_destinationConnectionString);
        await destConn.OpenAsync();
        _loggingService?.LogInfo("Connection", "Destination database connection successful",
          $"Host: {_config.DestinationHost}:{_config.DestinationPort}, Database: {_config.DestinationDatabase}");

        stopwatch.Stop();
        _loggingService?.LogSystem("Connection test completed successfully", $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
        return true;
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        _loggingService?.LogError("Connection", "Connection test failed",
          $"Error: {ex.Message}\nDuration: {stopwatch.Elapsed.TotalSeconds:F2}s");
        return false;
      }
    }
  }
}
