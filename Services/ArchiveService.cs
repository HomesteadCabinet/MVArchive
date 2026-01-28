using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    private readonly Func<string, bool>? _overwriteConfirmationCallback;
    private readonly ManifestService _manifestService;
    private readonly DatabaseService _databaseService;

    // Manifest-driven table collections
    private string[] _projectRelatedTables => _manifestService.GetProjectRelatedTables();
    private string[] _referenceTables => _manifestService.GetReferenceTables();
    private string[] _libraryTemplateTables => _manifestService.GetLibraryTemplateTables();
    private string[] _binaryDataTables => _manifestService.GetBinaryDataTables();
    private string[] _catalogTablesToSync => _manifestService.GetCatalogTablesToSync();

    public ArchiveService(ArchiveConfig config, LoggingService? loggingService = null, Func<string, bool>? overwriteConfirmationCallback = null)
    {
      _config = config;
      _loggingService = loggingService;
      _overwriteConfirmationCallback = overwriteConfirmationCallback;
      _manifestService = new ManifestService();
      _databaseService = new DatabaseService();

      _sourceConnectionString = $"Server={_config.SourceHost},{_config.SourcePort};Database={_config.SourceDatabase};User Id={_config.SourceUser};Password={_config.SourcePassword};TrustServerCertificate=true;";
      _destinationConnectionString = $"Server={_config.DestinationHost},{_config.DestinationPort};Database={_config.DestinationDatabase};User Id={_config.DestinationUser};Password={_config.DestinationPassword};TrustServerCertificate=true;";

      // Validate manifest
      ValidateManifest();

      _loggingService?.LogSystem("ArchiveService initialized",
        $"Source: {_config.SourceHost}:{_config.SourcePort}/{_config.SourceDatabase}\n" +
        $"Destination: {_config.DestinationHost}:{_config.DestinationPort}/{_config.DestinationDatabase}\n" +
        $"Dry Run: {_config.IsDryRun}\n" +
        $"Manifest: {_manifestService.Manifest.ManifestVersion}");
    }

    private static string QuoteIdentifier(string identifier)
    {
      // Support dotted identifiers by quoting each segment separately
      if (identifier.Contains('.', StringComparison.Ordinal))
      {
        var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
          parts[i] = $"[{parts[i].Replace("]", "]]", StringComparison.Ordinal)}]";
        }
        return string.Join('.', parts);
      }

      // Simple identifier
      return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private async Task<string> GetProjectDisplayNameAsync(string linkId)
    {
      try
      {
        var project = await _databaseService.GetProjectByLinkIdAsync(linkId);
        if (project?.Name != null && !string.IsNullOrWhiteSpace(project.Name))
        {
          return project.Name;
        }
      }
      catch (Exception ex)
      {
        _loggingService?.LogWarning("Archive", "Failed to retrieve project name, using LinkID", $"LinkID: {linkId}, Error: {ex.Message}");
      }

      // Fallback to LinkID if name is not available or retrieval fails
      return linkId;
    }

        private int CalculateTotalTables(bool syncReferences)
    {
      var total = _projectRelatedTables.Length + 1; // +1 for Projects table

      if (syncReferences)
      {
        total += _referenceTables.Length;

        // Only include catalog and library template tables if not skipping them
        if (!_config.SkipCatalogTables)
        {
          total += _libraryTemplateTables.Length + 1; // +1 for Libraries (processed in template phase but not in _libraryTemplateTables)
          total += _catalogTablesToSync.Length; // Catalog tables (ToolFiles, ProcessingStations, Employees, etc.)
        }
      }

      // Include special per-project filtered tables (e.g., Categories by LinkIDParent)
      total += 1; // Categories (LinkIDParent)

      return total;
    }

    private string BuildProjectRelatedQuery(string tableName, string projectLinkId)
    {
      // Subassemblies are not directly linked to Projects; they hang off Products and can be nested under other Subassemblies.
      // Derive the full set for a project via Products, then recursively include child subassemblies.
      if (tableName.Equals("Subassemblies", StringComparison.OrdinalIgnoreCase))
      {
        return BuildProjectSubassembliesQuery();
      }

      // Check if this table has a manifest-defined relationship to Projects
      var rootRels = _manifestService.Manifest.Root.Relationships;
      var relationship = rootRels.FirstOrDefault(r => string.Equals(r.From, tableName, StringComparison.OrdinalIgnoreCase));

      if (relationship != null && (relationship.ViaLink != null || relationship.ViaLinks.Count > 0))
      {
        // Use manifest-defined relationship.
        // If the relationship is directly to the root entity (Projects), filter directly; otherwise, filter via the parent.
        var rootEntity = _manifestService.Manifest.Root.Entity; // expected "Projects"
        string Tail(string s)
        {
          return s.Contains('.', StringComparison.Ordinal)
            ? s.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last()
            : s;
        }

        // Get ViaLinks list (use ViaLinks if available, otherwise fall back to single ViaLink for backward compatibility)
        var viaLinks = relationship.ViaLinks.Count > 0
          ? relationship.ViaLinks
          : (relationship.ViaLink != null ? new List<Models.ManifestViaLink> { relationship.ViaLink } : new List<Models.ManifestViaLink>());

        var linkLogic = relationship.LinkLogic ?? "AND"; // Default to AND

        // Self-referential relationships (e.g., Categories -> Categories) are still project-scoped via their "parent" field.
        // In these cases, the manifest lhs already points at the project/root identifier (e.g., Categories.LinkIDParent).
        if (string.Equals(relationship.To, relationship.From, StringComparison.OrdinalIgnoreCase))
        {
          if (viaLinks.Count == 1)
          {
            var lhsColumnOnly = Tail(viaLinks[0].Lhs);
            return $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(lhsColumnOnly)} = @ProjectLinkId";
          }
          else if (viaLinks.Count > 1)
          {
            // Multiple conditions with OR/AND logic
            var conditions = viaLinks.Select(vl => $"{QuoteIdentifier(Tail(vl.Lhs))} = @ProjectLinkId");
            var separator = linkLogic.Equals("OR", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
            return $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {string.Join(separator, conditions)}";
          }
        }

        if (string.Equals(relationship.To, rootEntity, StringComparison.OrdinalIgnoreCase))
        {
          // Direct root relationship: <table> -> Projects
          // Map any explicit Projects.LinkID usage to LinkIDProject (legacy) and strip qualifiers from lhs.
          if (viaLinks.Count == 1)
          {
            var lhs = viaLinks[0].Lhs.Replace("Projects.LinkID", "LinkIDProject", StringComparison.Ordinal);
            var lhsColumnOnly = Tail(lhs);
            return $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(lhsColumnOnly)} = @ProjectLinkId";
          }
          else if (viaLinks.Count > 1)
          {
            // Multiple conditions - need to check which are direct vs indirect
            var conditions = new List<string>();

            foreach (var vl in viaLinks)
            {
              var lhs = vl.Lhs.Replace("Projects.LinkID", "LinkIDProject", StringComparison.Ordinal);
              var lhsColumnOnly = Tail(lhs);

              // Check if this is a direct project link (contains "LinkIDProject" or "Projects.LinkID")
              if (lhs.Contains("LinkIDProject", StringComparison.OrdinalIgnoreCase) ||
                  vl.Lhs.Contains("Projects.LinkID", StringComparison.OrdinalIgnoreCase))
              {
                // Direct link to Projects
                conditions.Add($"{QuoteIdentifier(lhsColumnOnly)} = @ProjectLinkId");
              }
              else
              {
                // Indirect link via another table - need to build EXISTS clause
                // Extract parent table from rhs (e.g., "Parts.LinkID" -> "Parts")
                var rhsTable = vl.Rhs.Contains('.')
                  ? vl.Rhs.Split('.')[0]
                  : null;

                if (!string.IsNullOrEmpty(rhsTable))
                {
                  // Find parent relationship to Projects
                  var parentRel = rootRels.FirstOrDefault(r =>
                    string.Equals(r.From, rhsTable, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.To, rootEntity, StringComparison.OrdinalIgnoreCase) &&
                    (r.ViaLink != null || r.ViaLinks.Count > 0));

                  if (parentRel != null)
                  {
                    var parentViaLinks = parentRel.ViaLinks.Count > 0
                      ? parentRel.ViaLinks
                      : (parentRel.ViaLink != null ? new List<Models.ManifestViaLink> { parentRel.ViaLink } : new List<Models.ManifestViaLink>());

                    if (parentViaLinks.Count > 0)
                    {
                      var parentProjectCol = Tail(parentViaLinks[0].Lhs.Replace("Projects.LinkID", "LinkIDProject", StringComparison.Ordinal));
                      var parentJoinCol = Tail(vl.Rhs);
                      var childJoinCol = lhsColumnOnly;

                      conditions.Add($"EXISTS (SELECT 1 FROM {QuoteIdentifier(rhsTable)} WHERE {QuoteIdentifier(rhsTable)}.{QuoteIdentifier(parentJoinCol)} = {QuoteIdentifier(tableName)}.{QuoteIdentifier(childJoinCol)} AND {QuoteIdentifier(rhsTable)}.{QuoteIdentifier(parentProjectCol)} = @ProjectLinkId)");
                    }
                  }
                }
              }
            }

            if (conditions.Count > 0)
            {
              var separator = linkLogic.Equals("OR", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
              return $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {string.Join(separator, conditions)}";
            }
          }
        }
        else
        {
          // Root relationship to a parent other than Projects (e.g., Subassemblies -> Products).
          // Filter rows where the parent belongs to the project.
          var parentTable = relationship.To;
          var parentRel = rootRels.FirstOrDefault(r =>
            string.Equals(r.From, parentTable, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.To, rootEntity, StringComparison.OrdinalIgnoreCase) &&
            (r.ViaLink != null || r.ViaLinks.Count > 0));

          if (parentRel != null)
          {
            var parentViaLinks = parentRel.ViaLinks.Count > 0
              ? parentRel.ViaLinks
              : (parentRel.ViaLink != null ? new List<Models.ManifestViaLink> { parentRel.ViaLink } : new List<Models.ManifestViaLink>());

            if (parentViaLinks.Count > 0)
            {
              // Get parent project column (use first ViaLink for parent's project filter)
              var parentProjectCol = Tail(parentViaLinks[0].Lhs.Replace("Projects.LinkID", "LinkIDProject", StringComparison.Ordinal));

              if (viaLinks.Count == 1)
              {
                // Single ViaLink - use EXISTS clause
                var childJoinCol = Tail(viaLinks[0].Lhs);
                var parentJoinCol = Tail(viaLinks[0].Rhs);

                var existsClause =
                  $"EXISTS (SELECT 1 FROM {QuoteIdentifier(parentTable)} " +
                  $"WHERE {QuoteIdentifier(parentTable)}.{QuoteIdentifier(parentJoinCol)} = {QuoteIdentifier(tableName)}.{QuoteIdentifier(childJoinCol)} " +
                  $"AND {QuoteIdentifier(parentTable)}.{QuoteIdentifier(parentProjectCol)} = @ProjectLinkId)";

                return $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {existsClause}";
              }
              else if (viaLinks.Count > 1)
              {
                // Multiple ViaLinks - build EXISTS clauses for each and combine with OR/AND
                var existsClauses = viaLinks.Select(vl =>
                {
                  var childJoinCol = Tail(vl.Lhs);
                  var parentJoinCol = Tail(vl.Rhs);
                  return $"EXISTS (SELECT 1 FROM {QuoteIdentifier(parentTable)} " +
                         $"WHERE {QuoteIdentifier(parentTable)}.{QuoteIdentifier(parentJoinCol)} = {QuoteIdentifier(tableName)}.{QuoteIdentifier(childJoinCol)} " +
                         $"AND {QuoteIdentifier(parentTable)}.{QuoteIdentifier(parentProjectCol)} = @ProjectLinkId)";
                });

                var separator = linkLogic.Equals("OR", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
                return $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {string.Join(separator, existsClauses)}";
              }
            }
          }
        }
      }

      // If not directly related to Projects, try one-level nested relationship via a parent that is related to Projects
      foreach (var parent in rootRels)
      {
        var parentViaLinks = parent.ViaLinks.Count > 0
          ? parent.ViaLinks
          : (parent.ViaLink != null ? new List<Models.ManifestViaLink> { parent.ViaLink } : new List<Models.ManifestViaLink>());

        if (parentViaLinks.Count == 0) continue;

        var nested = parent.NestedRelationships.FirstOrDefault(n =>
          string.Equals(n.From, tableName, StringComparison.OrdinalIgnoreCase) &&
          (n.ViaLink != null || n.ViaLinks.Count > 0));

        if (nested != null)
        {
          var nestedViaLinks = nested.ViaLinks.Count > 0
            ? nested.ViaLinks
            : (nested.ViaLink != null ? new List<Models.ManifestViaLink> { nested.ViaLink } : new List<Models.ManifestViaLink>());

          if (nestedViaLinks.Count > 0)
          {
            // Parent example: Products -> Projects via Products.LinkIDProject
            // Nested example: ProductMap -> Products via ProductMap.LinkIDProduct = Products.LinkID
            var parentTable = parent.From;           // e.g., Products
            var childTable = tableName;              // e.g., ProductMap

            string Tail(string s)
            {
              return s.Contains('.', StringComparison.Ordinal)
                ? s.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last()
                : s;
            }

            // Column on parent used to filter by project (use first parent ViaLink)
            var parentFilterLhs = parentViaLinks[0].Lhs.Replace("Projects.LinkID", "LinkIDProject", StringComparison.Ordinal);
            var parentProjectCol = Tail(parentFilterLhs); // e.g., LinkIDProject

            var nestedLinkLogic = nested.LinkLogic ?? "AND";

            if (nestedViaLinks.Count == 1)
            {
              // Single nested ViaLink
              var childJoinCol = Tail(nestedViaLinks[0].Lhs); // e.g., LinkIDProduct
              var parentJoinCol = Tail(nestedViaLinks[0].Rhs); // e.g., LinkID

              // Use EXISTS so we can keep SELECT * shape for downstream logic
              var existsClause = $"EXISTS (SELECT 1 FROM {QuoteIdentifier(parentTable)} WHERE {QuoteIdentifier(parentTable)}.{QuoteIdentifier(parentJoinCol)} = {QuoteIdentifier(childTable)}.{QuoteIdentifier(childJoinCol)} AND {QuoteIdentifier(parentTable)}.{QuoteIdentifier(parentProjectCol)} = @ProjectLinkId)";
              return $"SELECT * FROM {QuoteIdentifier(childTable)} WHERE {existsClause}";
            }
            else if (nestedViaLinks.Count > 1)
            {
              // Multiple nested ViaLinks - build EXISTS clauses for each and combine with OR/AND
              var existsClauses = nestedViaLinks.Select(vl =>
              {
                var childJoinCol = Tail(vl.Lhs);
                var parentJoinCol = Tail(vl.Rhs);
                return $"EXISTS (SELECT 1 FROM {QuoteIdentifier(parentTable)} WHERE {QuoteIdentifier(parentTable)}.{QuoteIdentifier(parentJoinCol)} = {QuoteIdentifier(childTable)}.{QuoteIdentifier(childJoinCol)} AND {QuoteIdentifier(parentTable)}.{QuoteIdentifier(parentProjectCol)} = @ProjectLinkId)";
              });

              var separator = nestedLinkLogic.Equals("OR", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
              return $"SELECT * FROM {QuoteIdentifier(childTable)} WHERE {string.Join(separator, existsClauses)}";
            }
          }
        }
      }

      // Fallback: if no relationship found, log warning and return empty result set
      // This prevents "Invalid column name" errors for tables without LinkIDProject
      _loggingService?.LogWarning("Archive", $"No manifest relationship found for table {tableName}",
        "Table will be skipped. Add a relationship in manifest.xml if this table should be archived.");
      return $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE 1=0"; // Return empty result set
    }

    private static bool IsCteQuery(string sql)
    {
      var trimmed = sql.TrimStart();
      return trimmed.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith(";WITH ", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildProjectRelatedCountQuery(string tableName, string projectLinkId)
    {
      var baseQuery = BuildProjectRelatedQuery(tableName, projectLinkId);

      // If the base query starts with a CTE, we cannot wrap it in a derived table.
      if (IsCteQuery(baseQuery))
      {
        return baseQuery.Replace("SELECT *", "SELECT COUNT(*)", StringComparison.Ordinal);
      }

      return $"SELECT COUNT(*) FROM ({baseQuery}) q";
    }

    private string BuildProjectRelatedBatchQuery(string tableName, string projectLinkId)
    {
      var baseQuery = BuildProjectRelatedQuery(tableName, projectLinkId);
      var batchTail = $@"
              ORDER BY {QuoteIdentifier("ID")}
              OFFSET @Offset ROWS
              FETCH NEXT @BatchSize ROWS ONLY";

      // CTE queries (e.g., Subassemblies) need MAXRECURSION after ORDER BY/OFFSET.
      return IsCteQuery(baseQuery)
        ? $"{baseQuery}{batchTail}\n              OPTION (MAXRECURSION 0)"
        : $"{baseQuery}{batchTail}";
    }

    private string BuildProjectRelatedDeleteQuery(string tableName, string projectLinkId)
    {
      // Mirror BuildProjectRelatedQuery shapes for overwrite deletions.
      if (tableName.Equals("Subassemblies", StringComparison.OrdinalIgnoreCase))
      {
        return BuildProjectSubassembliesDeleteQuery();
      }

      return BuildProjectRelatedQuery(tableName, projectLinkId)
        .Replace("SELECT * FROM", "DELETE FROM", StringComparison.Ordinal);
    }

    private string BuildProjectSubassembliesQuery()
    {
      // NOTE: This query is intentionally "SELECT * ..." so callers can reuse it for batching.
      // It finds subassemblies with direct LinkIDProject link and recursively includes child subassemblies.
      return $@"
;WITH SubassemblySet AS (
  -- Subassemblies with direct LinkIDProject link
  SELECT s.{QuoteIdentifier("ID")}, s.{QuoteIdentifier("LinkID")}
  FROM {QuoteIdentifier("Subassemblies")} s
  WHERE s.{QuoteIdentifier("LinkIDProject")} = @ProjectLinkId
  UNION ALL
  -- Recursively include child subassemblies
  SELECT c.{QuoteIdentifier("ID")}, c.{QuoteIdentifier("LinkID")}
  FROM {QuoteIdentifier("Subassemblies")} c
  INNER JOIN SubassemblySet ps ON c.{QuoteIdentifier("LinkIDParentSubassembly")} = ps.{QuoteIdentifier("LinkID")}
)
SELECT * FROM {QuoteIdentifier("Subassemblies")}
WHERE {QuoteIdentifier("ID")} IN (SELECT DISTINCT {QuoteIdentifier("ID")} FROM SubassemblySet)";
    }

    private string BuildProjectSubassembliesDeleteQuery()
    {
      // Delete shape for overwrite: same derived set as BuildProjectSubassembliesQuery.
      // MAXRECURSION is appended by callers after ORDER BY/OFFSET where applicable; here we only produce the base delete.
      return $@"
;WITH SubassemblySet AS (
  -- Subassemblies with direct LinkIDProject link
  SELECT s.{QuoteIdentifier("ID")}, s.{QuoteIdentifier("LinkID")}
  FROM {QuoteIdentifier("Subassemblies")} s
  WHERE s.{QuoteIdentifier("LinkIDProject")} = @ProjectLinkId
  UNION ALL
  -- Recursively include child subassemblies
  SELECT c.{QuoteIdentifier("ID")}, c.{QuoteIdentifier("LinkID")}
  FROM {QuoteIdentifier("Subassemblies")} c
  INNER JOIN SubassemblySet ps ON c.{QuoteIdentifier("LinkIDParentSubassembly")} = ps.{QuoteIdentifier("LinkID")}
)
DELETE FROM {QuoteIdentifier("Subassemblies")}
WHERE {QuoteIdentifier("ID")} IN (SELECT DISTINCT {QuoteIdentifier("ID")} FROM SubassemblySet)";
    }

    /// <summary>Build DELETE query for source cleanup: use manifest relationship so tables linked to Products (e.g. Subassemblies) or nested (e.g. ProductMap) use the correct filter.</summary>
    private string BuildDeleteFromSourceQuery(string tableName)
    {
      var rootRels = _manifestService.Manifest.Root.Relationships;
      string Tail(string s) => s.Contains('.', StringComparison.Ordinal)
        ? s.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last()
        : s;

      // Direct root relationship (table -> Projects or table -> Products)
      var relationship = rootRels.FirstOrDefault(r => string.Equals(r.From, tableName, StringComparison.OrdinalIgnoreCase));
      if (relationship?.ViaLink != null)
      {
        var childCol = Tail(relationship.ViaLink.Lhs);
        var rootEntity = _manifestService.Manifest.Root.Entity; // "Projects"
        if (string.Equals(relationship.To, rootEntity, StringComparison.OrdinalIgnoreCase))
          return $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(childCol)} = @ProjectLinkId";
        // Table links to parent other than Projects (e.g. Subassemblies -> Products): delete where parent is in project
        var parentRel = rootRels.FirstOrDefault(r => string.Equals(r.From, relationship.To, StringComparison.OrdinalIgnoreCase) && r.ViaLink != null);
        var parentProjectCol = parentRel?.ViaLink != null ? Tail(parentRel.ViaLink!.Lhs.Replace("Projects.LinkID", "LinkIDProject", StringComparison.Ordinal)) : "LinkIDProject";
        var parentKeyCol = Tail(relationship.ViaLink.Rhs);
        var parentTable = relationship.To;
        return $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(childCol)} IN (SELECT {QuoteIdentifier(parentKeyCol)} FROM {QuoteIdentifier(parentTable)} WHERE {QuoteIdentifier(parentProjectCol)} = @ProjectLinkId)";
      }

      // Nested under a root relationship (e.g. ProductMap under Products)
      foreach (var parent in rootRels)
      {
        if (parent.ViaLink == null) continue;
        var nested = parent.NestedRelationships.FirstOrDefault(n => string.Equals(n.From, tableName, StringComparison.OrdinalIgnoreCase) && n.ViaLink != null);
        if (nested != null && nested.ViaLink != null)
        {
          var childJoinCol = Tail(nested.ViaLink.Lhs);
          var parentJoinCol = Tail(nested.ViaLink.Rhs);
          var parentFilterLhs = parent.ViaLink.Lhs.Replace("Projects.LinkID", "LinkIDProject", StringComparison.Ordinal);
          var parentProjectCol = Tail(parentFilterLhs);
          var parentTable = parent.From;
          return $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(childJoinCol)} IN (SELECT {QuoteIdentifier(parentJoinCol)} FROM {QuoteIdentifier(parentTable)} WHERE {QuoteIdentifier(parentProjectCol)} = @ProjectLinkId)";
        }
      }

      return $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier("LinkIDProject")} = @ProjectLinkId";
    }

    private string GetProjectParameterValue()
    {
      return _manifestService.GetParameterValue("ProjectID");
    }

    private string GetLibraryParameterValue()
    {
      return _manifestService.GetParameterValue("LibraryID");
    }

    private void ValidateManifest()
    {
      var manifest = _manifestService.Manifest;

      if (manifest.Catalogs.Count == 0)
      {
        throw new InvalidOperationException("Manifest contains no catalogs. Please check your manifest.xml file.");
      }

      if (string.IsNullOrEmpty(manifest.Root.Entity))
      {
        throw new InvalidOperationException("Manifest root entity is not defined. Please check your manifest.xml file.");
      }

      if (manifest.Root.Relationships.Count == 0)
      {
        _loggingService?.LogWarning("Archive", "Manifest contains no relationships", "Archive will use default table linking behavior");
      }

      _loggingService?.LogInfo("Archive", "Manifest validation passed",
        $"Catalogs: {manifest.Catalogs.Count}, Root Entity: {manifest.Root.Entity}, Relationships: {manifest.Root.Relationships.Count}");
    }

    public void ReloadManifest()
    {
      _manifestService.ReloadManifest();
      ValidateManifest();
      _loggingService?.LogInfo("Archive", "Manifest reloaded", "Archive service configuration updated from manifest");
    }

    public ArchiveManifest GetManifest()
    {
      return _manifestService.Manifest;
    }

    public string[] GetManifestTables()
    {
      return _manifestService.GetAllTableNames();
    }

    public string GetManifestInfo()
    {
      var manifest = _manifestService.Manifest;
      return $"Manifest v{manifest.ManifestVersion} - Domain: {manifest.Domain}, " +
             $"Catalogs: {manifest.Catalogs.Count}, " +
             $"Project Tables: {_projectRelatedTables.Length}, " +
             $"Reference Tables: {_referenceTables.Length}, " +
             $"Template Tables: {_libraryTemplateTables.Length}";
    }

    /// <summary>Unified method to sync a single table from source to destination. Handles table creation, data loading, and sync logic.</summary>
    private async Task<bool> SyncTableAsync(
      SqlConnection sourceConn,
      SqlConnection destConn,
      string tableName,
      string sourceQuery,
      ArchiveProgress progress,
      IProgress<ArchiveProgress>? progressReporter,
      string logContext = "table")
    {
      using var sourceCmd = new SqlCommand(sourceQuery, sourceConn);
      return await SyncTableAsync(sourceConn, destConn, tableName, sourceCmd, progress, progressReporter, logContext);
    }

    /// <summary>Unified method to sync a single table using a parameterized command.</summary>
    private async Task<bool> SyncTableAsync(
      SqlConnection sourceConn,
      SqlConnection destConn,
      string tableName,
      SqlCommand sourceCmd,
      ArchiveProgress progress,
      IProgress<ArchiveProgress>? progressReporter,
      string logContext = "table")
    {
      try
      {
        progress.CurrentTable = tableName;
        progress.Status = $"Syncing {logContext}: {tableName}";
        progressReporter?.Report(progress);

        var tableStopwatch = Stopwatch.StartNew();
        _loggingService?.LogArchive($"Syncing {logContext}: {tableName}", "Starting table synchronization");

        // Ensure table exists in destination
        await EnsureTableExistsAsync(destConn, tableName, sourceConn);

        // Load data from source
        using var reader = await sourceCmd.ExecuteReaderAsync();
        var dataTable = new DataTable();
        dataTable.Load(reader);
        reader.Close();

        var sourceRecordCount = dataTable.Rows.Count;
        _loggingService?.LogArchive($"Retrieved {sourceRecordCount} records from source {tableName}", $"Source query: {sourceCmd.CommandText}");

        if (sourceRecordCount > 0)
        {
          // Update progress with record count
          progress.TotalRecords = sourceRecordCount;
          progress.CurrentRecord = 0;
          progressReporter?.Report(progress);

          // Get primary key column for the table
          var primaryKeyColumn = await GetPrimaryKeyColumnAsync(sourceConn, tableName);

          // Get destination columns/types to handle schema mismatches
          var destColumns = await GetDestinationColumnNamesAsync(destConn, tableName);
          var destTypes = await GetDestinationColumnTypesAsync(destConn, tableName);
          var destBinaryColumns = new HashSet<string>(
            destTypes.Where(kv => IsBinaryTypeName(kv.Value)).Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase
          );

          if (string.IsNullOrEmpty(primaryKeyColumn))
          {
            _loggingService?.LogWarning("Archive", $"No primary key found for {logContext} table {tableName}", "Will use INSERT OR REPLACE logic");
            await SyncTableWithoutPrimaryKeyAsync(destConn, tableName, dataTable);
          }
          else
          {
            await SyncTableWithPrimaryKeyAsync(destConn, tableName, dataTable, primaryKeyColumn, destColumns, destBinaryColumns, progress, progressReporter);
          }
        }

        tableStopwatch.Stop();
        _loggingService?.LogArchive($"{logContext} {tableName} sync completed", $"Records processed: {sourceRecordCount}, Duration: {tableStopwatch.Elapsed.TotalSeconds:F2}s");

        progress.TablesProcessed++;
        progressReporter?.Report(progress);
        return true;
      }
      catch (Exception ex)
      {
        _loggingService?.LogError("Archive", $"Failed to sync {logContext} table {tableName}", $"Error: {ex.Message}");
        return false;
      }
    }

    private async Task<bool> SyncReferenceTablesAsync(ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      progress.Status = "Syncing reference tables";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      var referenceTables = _referenceTables;
      _loggingService?.LogArchive("Starting reference table synchronization", $"Tables: {string.Join(", ", referenceTables)}");

      using var sourceConn = new SqlConnection(_sourceConnectionString);
      using var destConn = new SqlConnection(_destinationConnectionString);

      await sourceConn.OpenAsync();
      await destConn.OpenAsync();

      var hadErrors = false;
      foreach (var tableName in referenceTables)
      {
        // Build query: reference tables copy all rows (singletons are now included in reference tables)
        var catalog = _manifestService.GetCatalogByTable(tableName);
        string sourceQuery = catalog != null && catalog.Mode.Equals("all_rows", StringComparison.OrdinalIgnoreCase)
          ? $"SELECT * FROM {QuoteIdentifier(tableName)}"
          : tableName.Equals("Categories", StringComparison.OrdinalIgnoreCase)
            ? $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier("LinkIDParent")} IS NULL OR LTRIM(RTRIM({QuoteIdentifier("LinkIDParent")})) = ''"
            : $"SELECT * FROM {QuoteIdentifier(tableName)}";

        var success = await SyncTableAsync(sourceConn, destConn, tableName, sourceQuery, progress, progressReporter, "reference table");
        if (!success) hadErrors = true;
      }

      stopwatch.Stop();
      _loggingService?.LogArchive("Reference table synchronization completed", $"Total duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
      return !hadErrors;
    }

    private async Task<bool> SyncCatalogTablesAsync(ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      progress.Status = "Syncing catalog tables";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      var catalogTables = _catalogTablesToSync;
      _loggingService?.LogArchive("Starting catalog table synchronization", $"Tables: {string.Join(", ", catalogTables)}");

      if (catalogTables.Length == 0)
      {
        _loggingService?.LogInfo("Archive", "No catalog tables to sync", "All catalog tables are already handled by other sync methods");
        return true;
      }

      using var sourceConn = new SqlConnection(_sourceConnectionString);
      using var destConn = new SqlConnection(_destinationConnectionString);

      await sourceConn.OpenAsync();
      await destConn.OpenAsync();

      var hadErrors = false;
      foreach (var tableName in catalogTables)
      {
        // Build query: use manifest filters if present, otherwise copy all rows
        var catalog = _manifestService.GetCatalogByTable(tableName);
        string sourceQuery = catalog != null && catalog.Filters.Count > 0
          ? _manifestService.BuildCatalogQuery(catalog, Array.Empty<string>(), Array.Empty<string>())
          : $"SELECT * FROM {QuoteIdentifier(tableName)}";

        var success = await SyncTableAsync(sourceConn, destConn, tableName, sourceQuery, progress, progressReporter, "catalog table");
        if (!success) hadErrors = true;
      }

      stopwatch.Stop();
      _loggingService?.LogArchive("Catalog table synchronization completed", $"Total duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
      return !hadErrors;
    }

    private async Task<bool> PopulateLibraryTemplateDataAsync(ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      progress.Status = "Populating library template data filtered by libraries referenced in products";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Starting library template data population", "Including Libraries table and template tables");

      using var sourceConn = new SqlConnection(_sourceConnectionString);
      using var destConn = new SqlConnection(_destinationConnectionString);

      await sourceConn.OpenAsync();
      await destConn.OpenAsync();

      // Collect library IDs from multiple sources to ensure we capture all libraries that have template data:
      // 1. Libraries referenced by project products
      // 2. Libraries that have library template Subassemblies (Type=1)
      // 3. Libraries that have library template Products (Type=1)
      var libraryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      // Source 1: Libraries referenced by project products
      var getLibraryIdsFromProductsQuery = $@"
        SELECT DISTINCT LinkIDLibrary
        FROM {QuoteIdentifier("Products")}
        WHERE LinkIDLibrary IS NOT NULL
        AND LinkIDLibrary != ''
        AND LinkIDProject IS NOT NULL";

      using (var libraryCmd = new SqlCommand(getLibraryIdsFromProductsQuery, sourceConn))
      using (var libraryReader = await libraryCmd.ExecuteReaderAsync())
      {
        while (await libraryReader.ReadAsync())
        {
          var libraryId = libraryReader.GetString(0);
          if (!string.IsNullOrEmpty(libraryId))
          {
            libraryIds.Add(libraryId);
          }
        }
      }

      // Source 2: Libraries that have library template Subassemblies (Type=1)
      var getLibraryIdsFromSubassembliesQuery = $@"
        SELECT DISTINCT LinkIDLibrary
        FROM {QuoteIdentifier("Subassemblies")}
        WHERE LinkIDLibrary IS NOT NULL
        AND LinkIDLibrary != ''
        AND Type = 1";

      using (var subassemblyCmd = new SqlCommand(getLibraryIdsFromSubassembliesQuery, sourceConn))
      using (var subassemblyReader = await subassemblyCmd.ExecuteReaderAsync())
      {
        while (await subassemblyReader.ReadAsync())
        {
          var libraryId = subassemblyReader.GetString(0);
          if (!string.IsNullOrEmpty(libraryId))
          {
            libraryIds.Add(libraryId);
          }
        }
      }

      // Source 3: Libraries that have library template Products (Type=1)
      var getLibraryIdsFromTemplateProductsQuery = $@"
        SELECT DISTINCT LinkIDLibrary
        FROM {QuoteIdentifier("Products")}
        WHERE LinkIDLibrary IS NOT NULL
        AND LinkIDLibrary != ''
        AND Type = 1";

      using (var templateProductsCmd = new SqlCommand(getLibraryIdsFromTemplateProductsQuery, sourceConn))
      using (var templateProductsReader = await templateProductsCmd.ExecuteReaderAsync())
      {
        while (await templateProductsReader.ReadAsync())
        {
          var libraryId = templateProductsReader.GetString(0);
          if (!string.IsNullOrEmpty(libraryId))
          {
            libraryIds.Add(libraryId);
          }
        }
      }

      var libraryIdsList = libraryIds.ToList();

      if (libraryIdsList.Count == 0)
      {
        _loggingService?.LogInfo("Archive", "No library IDs found", "Skipping library template data population");
        return true;
      }

      _loggingService?.LogArchive("Found library IDs for template population", $"Count: {libraryIdsList.Count}, Libraries: {string.Join(", ", libraryIdsList)}");

      // Library-scoped catalogs are defined in the manifest by filters containing @LibraryID
      // (e.g., Libraries, Categories, Products(Type=1), Subassemblies(Type=1), SpecificationGroups(Type=6), etc.)
      // Ensure Libraries goes first so referenced rows exist before dependent tables.
      var tablesToProcess = new List<string> { "Libraries" };
      foreach (var catalog in _manifestService.Manifest.Catalogs)
      {
        if (catalog.Filters.Any(f => (f.Where ?? string.Empty).Contains("@LibraryID", StringComparison.OrdinalIgnoreCase)))
        {
          if (!catalog.Table.Equals("Libraries", StringComparison.OrdinalIgnoreCase))
          {
            tablesToProcess.Add(catalog.Table);
          }
        }
      }
      tablesToProcess = tablesToProcess.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
      _loggingService?.LogArchive("Library template tables to process", $"Tables: {string.Join(", ", tablesToProcess)}");

      var hadErrors = false;
      foreach (var tableName in tablesToProcess)
      {
        try
        {
          // Get catalog configuration for this table
          var catalog = _manifestService.GetCatalogByTable(tableName);
          string sourceQuery;

          if (catalog == null)
          {
            _loggingService?.LogWarning("Archive", "Library template table not found in manifest catalogs", $"Table: {tableName}");
            continue;
          }

          // Use manifest-defined catalog query (injects the library id list into the WHERE clauses)
          sourceQuery = _manifestService.BuildCatalogQuery(catalog, "", libraryIdsList);
          using var sourceCmd = new SqlCommand(sourceQuery, sourceConn);

          var success = await SyncTableAsync(sourceConn, destConn, tableName, sourceCmd, progress, progressReporter, "library template");
          if (!success) hadErrors = true;
        }
        catch (Exception ex)
        {
          _loggingService?.LogError("Archive", $"Failed to populate library template data for {tableName}", $"Error: {ex.Message}");
          hadErrors = true; // Continue with next table but mark that errors occurred
        }
      }

      stopwatch.Stop();
      _loggingService?.LogArchive("Library template data population completed", $"Total duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
      return !hadErrors;
    }

    private async Task<string?> GetPrimaryKeyColumnAsync(SqlConnection connection, string tableName)
    {
      var query = @"
        SELECT COLUMN_NAME
        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
        WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1
        AND TABLE_NAME = @TableName AND TABLE_SCHEMA = 'dbo'";

      using var cmd = new SqlCommand(query, connection);
      cmd.Parameters.AddWithValue("@TableName", tableName);

      var result = await cmd.ExecuteScalarAsync();
      return result?.ToString();
    }

    private async Task SyncTableWithoutPrimaryKeyAsync(SqlConnection destConn, string tableName, DataTable dataTable)
    {
      // For tables without primary keys, we'll truncate and reload
      _loggingService?.LogWarning("Archive", $"Syncing table without primary key: {tableName}", "Using truncate and reload method");

      // Truncate destination table
      var truncateQuery = $"TRUNCATE TABLE {QuoteIdentifier(tableName)}";
      using var truncateCmd = new SqlCommand(truncateQuery, destConn);
      await truncateCmd.ExecuteNonQueryAsync();

      _loggingService?.LogArchive($"Truncated destination table: {tableName}", $"Query: {truncateQuery}");

      // Bulk copy all records
      await BulkCopyToDestinationAsync(destConn, tableName, dataTable);

      _loggingService?.LogArchive($"Bulk copied {dataTable.Rows.Count} records to {tableName}", "Truncate and reload completed");
    }

    private async Task SyncTableWithPrimaryKeyAsync(SqlConnection destConn, string tableName, DataTable dataTable, string primaryKeyColumn, HashSet<string> destinationColumns, HashSet<string> destinationBinaryColumns, ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      var syncedCount = 0;
      var updatedCount = 0;
      var insertedCount = 0;

      _loggingService?.LogArchive($"Syncing table with primary key: {tableName}", $"Primary key column: {primaryKeyColumn}");

      foreach (DataRow row in dataTable.Rows)
      {
        var primaryKeyValue = row[primaryKeyColumn];

        // Check if record exists in destination
        var existsQuery = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(primaryKeyColumn)} = @PrimaryKeyValue";
        using var existsCmd = new SqlCommand(existsQuery, destConn);
        existsCmd.Parameters.AddWithValue("@PrimaryKeyValue", primaryKeyValue);

        var existsResult = await existsCmd.ExecuteScalarAsync();
        var exists = existsResult != null ? Convert.ToInt32(existsResult) : 0;

        if (exists > 0)
        {
          // Record exists, check if it needs updating
          var needsUpdate = await RecordNeedsUpdateAsync(destConn, tableName, row, primaryKeyColumn, primaryKeyValue, destinationColumns);

          if (needsUpdate)
          {
            // Update existing record
            await UpdateExistingRecordAsync(destConn, tableName, row, primaryKeyColumn, primaryKeyValue, destinationColumns, destinationBinaryColumns);
            updatedCount++;
          }
        }
        else
        {
          // Insert new record
          await InsertNewRecordAsync(destConn, tableName, row, destinationColumns, destinationBinaryColumns);
          insertedCount++;
        }

        syncedCount++;

        // Update progress and report every 100 records
        if (syncedCount % 100 == 0)
        {
          progress.CurrentRecord = syncedCount;
          progressReporter?.Report(progress);
        }
      }

      // Final progress update
      progress.CurrentRecord = syncedCount;
      progressReporter?.Report(progress);

      _loggingService?.LogArchive($"Table {tableName} sync summary",
        $"Total: {syncedCount}, Inserted: {insertedCount}, Updated: {updatedCount}");
    }

    private async Task<bool> RecordNeedsUpdateAsync(SqlConnection destConn, string tableName, DataRow sourceRow, string primaryKeyColumn, object primaryKeyValue, HashSet<string> destinationColumns)
    {
      // Get the existing record from destination
      var selectQuery = $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(primaryKeyColumn)} = @PrimaryKeyValue";
      using var selectCmd = new SqlCommand(selectQuery, destConn);
      selectCmd.Parameters.AddWithValue("@PrimaryKeyValue", primaryKeyValue);

      using var reader = await selectCmd.ExecuteReaderAsync();
      if (await reader.ReadAsync())
      {
        // Compare each column (excluding the primary key)
        foreach (DataColumn column in sourceRow.Table.Columns)
        {
          if (column.ColumnName != primaryKeyColumn && destinationColumns.Contains(column.ColumnName))
          {
            var sourceValue = sourceRow[column];
            var destValue = reader[column.ColumnName];

            // Handle null comparisons
            if (sourceValue == DBNull.Value && destValue == DBNull.Value)
            {
              continue; // Both null, no change
            }
            else if (sourceValue == DBNull.Value || destValue == DBNull.Value)
            {
              return true; // One is null, one isn't - needs update
            }
            else if (!sourceValue.Equals(destValue))
            {
              return true; // Values differ - needs update
            }
          }
        }
      }
      reader.Close();

      return false; // No differences found
    }

    private static byte[]? CoerceToBinaryBytes(object value)
    {
      if (value == DBNull.Value || value == null)
      {
        return null;
      }

      if (value is byte[] bytes)
      {
        return bytes;
      }

      if (value is string s)
      {
        // Try base64 first
        try
        {
          return Convert.FromBase64String(s);
        }
        catch
        {
          // Fallback to UTF8 bytes
          return System.Text.Encoding.UTF8.GetBytes(s);
        }
      }

      // Last resort: attempt ToString and UTF8 encode
      return System.Text.Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty);
    }

    private async Task UpdateExistingRecordAsync(SqlConnection destConn, string tableName, DataRow row, string primaryKeyColumn, object primaryKeyValue, HashSet<string> destinationColumns, HashSet<string> destinationBinaryColumns)
    {
      // Build UPDATE query
      var setClause = string.Join(", ", row.Table.Columns
        .Cast<DataColumn>()
        .Where(col => col.ColumnName != primaryKeyColumn && destinationColumns.Contains(col.ColumnName))
        .Select(col => $"{QuoteIdentifier(col.ColumnName)} = @{col.ColumnName}"));

      var updateQuery = $"UPDATE {QuoteIdentifier(tableName)} SET {setClause} WHERE {QuoteIdentifier(primaryKeyColumn)} = @PrimaryKeyValue";

      using var updateCmd = new SqlCommand(updateQuery, destConn);
      updateCmd.Parameters.AddWithValue("@PrimaryKeyValue", primaryKeyValue);

      // Add parameters for all columns except primary key and only those existing in destination
      foreach (DataColumn column in row.Table.Columns)
      {
        if (column.ColumnName != primaryKeyColumn && destinationColumns.Contains(column.ColumnName))
        {
          var raw = row[column] == DBNull.Value ? DBNull.Value : row[column];
          if (raw == DBNull.Value)
          {
            // Ensure correct type for binary columns even when null
            if (destinationBinaryColumns.Contains(column.ColumnName))
            {
              var p = updateCmd.Parameters.Add($"@{column.ColumnName}", System.Data.SqlDbType.VarBinary);
              p.Value = DBNull.Value;
            }
            else
            {
              updateCmd.Parameters.AddWithValue($"@{column.ColumnName}", DBNull.Value);
            }
          }
          else if (destinationBinaryColumns.Contains(column.ColumnName))
          {
            var bytes = CoerceToBinaryBytes(raw) ?? Array.Empty<byte>();
            var p = updateCmd.Parameters.Add($"@{column.ColumnName}", System.Data.SqlDbType.VarBinary);
            p.Value = bytes;
          }
          else
          {
            updateCmd.Parameters.AddWithValue($"@{column.ColumnName}", raw);
          }
        }
      }

      var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
      _loggingService?.LogDatabase("Update record", updateQuery, $"Primary key: {primaryKeyValue}, Rows affected: {rowsAffected}", null);
    }

    private async Task InsertNewRecordAsync(SqlConnection destConn, string tableName, DataRow row, HashSet<string> destinationColumns, HashSet<string> destinationBinaryColumns)
    {
      // Build INSERT query
      var eligibleColumns = row.Table.Columns.Cast<DataColumn>().Where(col => destinationColumns.Contains(col.ColumnName)).ToList();
      var columnNames = string.Join(", ", eligibleColumns.Select(col => QuoteIdentifier(col.ColumnName)));
      var parameterNames = string.Join(", ", eligibleColumns.Select(col => $"@{col.ColumnName}"));

      var insertQuery = $"INSERT INTO {QuoteIdentifier(tableName)} ({columnNames}) VALUES ({parameterNames})";

      using var insertCmd = new SqlCommand(insertQuery, destConn);

      // Add parameters for intersecting columns
      foreach (DataColumn column in eligibleColumns)
      {
        var raw = row[column] == DBNull.Value ? DBNull.Value : row[column];
        if (raw == DBNull.Value)
        {
          if (destinationBinaryColumns.Contains(column.ColumnName))
          {
            var p = insertCmd.Parameters.Add($"@{column.ColumnName}", System.Data.SqlDbType.VarBinary);
            p.Value = DBNull.Value;
          }
          else
          {
            insertCmd.Parameters.AddWithValue($"@{column.ColumnName}", DBNull.Value);
          }
        }
        else if (destinationBinaryColumns.Contains(column.ColumnName))
        {
          var bytes = CoerceToBinaryBytes(raw) ?? Array.Empty<byte>();
          var p = insertCmd.Parameters.Add($"@{column.ColumnName}", System.Data.SqlDbType.VarBinary);
          p.Value = bytes;
        }
        else
        {
          insertCmd.Parameters.AddWithValue($"@{column.ColumnName}", raw);
        }
      }

      var rowsAffected = await insertCmd.ExecuteNonQueryAsync();
      _loggingService?.LogDatabase("Insert record", insertQuery, $"Rows affected: {rowsAffected}", null);
    }

    public async Task<ArchiveProgress> ArchiveProjectAsync(string projectLinkId, IProgress<ArchiveProgress>? progress = null, bool syncReferences = true)
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Starting project archive", $"Project LinkID: {projectLinkId}");
      _loggingService?.LogArchive(_config.IsDryRun ? "Dry run: ON — source will NOT be modified or deleted" : "Dry run: OFF — source WILL be cleaned after copy", null!);
      _loggingService?.LogArchive(_config.SkipCatalogTables ? "Skip Catalog Tables: ON — catalog tables will be skipped" : "Skip Catalog Tables: OFF — catalog tables will be synced", null!);

      var archiveProgress = new ArchiveProgress
      {
        StartTime = DateTime.Now,
        TotalTables = CalculateTotalTables(syncReferences),
        TotalProjects = 1
      };

      try
      {
        // Sync reference tables first if requested
        if (syncReferences)
        {
          var refsOk = await SyncReferenceTablesAsync(archiveProgress, progress);
          if (!refsOk)
          {
            throw new InvalidOperationException("Reference table synchronization failed. See logs for details.");
          }

          // Skip catalog tables if configured to do so (for faster debugging)
          _loggingService?.LogInfo("Archive", $"SkipCatalogTables setting value", $"SkipCatalogTables = {_config.SkipCatalogTables}");
          if (!_config.SkipCatalogTables)
          {
            _loggingService?.LogInfo("Archive", "Syncing catalog tables", "SkipCatalogTables is false, proceeding with catalog sync");
            // Sync catalog tables (ToolFiles, ProcessingStations, Employees, etc.)
            var catalogOk = await SyncCatalogTablesAsync(archiveProgress, progress);
            if (!catalogOk)
            {
              throw new InvalidOperationException("Catalog table synchronization failed. See logs for details.");
            }

            // Populate library template data after reference tables are synced
            var libTemplatesOk = await PopulateLibraryTemplateDataAsync(archiveProgress, progress);
            if (!libTemplatesOk)
            {
              throw new InvalidOperationException("Library template data population failed. See logs for details.");
            }
          }
          else
          {
            _loggingService?.LogInfo("Archive", "Skipping catalog tables", $"SkipCatalogTables setting is enabled (value: {_config.SkipCatalogTables}) for faster debugging");
          }
        }

        // Check if project already exists in destination
        using var destConn = new SqlConnection(_destinationConnectionString);
        await destConn.OpenAsync();

        var existsQuery = "SELECT COUNT(*) FROM Projects WHERE LinkID = @ProjectLinkId";
        using var existsCmd = new SqlCommand(existsQuery, destConn);
        existsCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

        var existsResult = await existsCmd.ExecuteScalarAsync();
        var exists = existsResult != null ? Convert.ToInt32(existsResult) : 0;

        if (exists > 0)
        {
          if (_config.OverwriteExisting)
          {
            _loggingService?.LogWarning("Archive", "Overwrite enabled - deleting existing project from destination", $"Project LinkID: {projectLinkId}");
            await DeleteExistingProjectFromDestinationAsync(destConn, projectLinkId);
          }
          else
          {
            _loggingService?.LogInfo("Archive", "Project already exists in destination and overwrite disabled - skipping", $"Project LinkID: {projectLinkId}");
            return new ArchiveProgress { Status = "Project already archived", IsComplete = true };
          }
        }

        // Copy the project record from source to destination safely
        using var sourceConn = new SqlConnection(_sourceConnectionString);
        await sourceConn.OpenAsync();

        // Ensure Projects table exists in destination
        await EnsureTableExistsAsync(destConn, "Projects", sourceConn);

        var selectProjectQuery = $"SELECT * FROM {QuoteIdentifier("Projects")} WHERE {QuoteIdentifier("LinkID")} = @ProjectLinkId";
        using var selectProjectCmd = new SqlCommand(selectProjectQuery, sourceConn);
        selectProjectCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);
        using var projectReader = await selectProjectCmd.ExecuteReaderAsync();
        var projectTable = new DataTable();
        projectTable.Load(projectReader);
        projectReader.Close();

        if (projectTable.Rows.Count == 0)
        {
          _loggingService?.LogWarning("Archive", "No project row found in source to copy", $"Project LinkID: {projectLinkId}");
        }
        else
        {
          await BulkCopyToDestinationAsync(destConn, "Projects", projectTable);
          _loggingService?.LogInfo("Archive", "Project record copied", $"Rows affected: {projectTable.Rows.Count}");
        }

        // Count Projects table as processed (+1 in TotalTables)
        archiveProgress.TablesProcessed++;
        progress?.Report(archiveProgress);

        // Library template data is now populated in PopulateLibraryTemplateDataAsync before project archiving

        // Archive all related data using the manifest-driven approach (manifest-only)
        var projectRelatedTables = _projectRelatedTables;
        foreach (var tableName in projectRelatedTables)
        {
          await ArchiveRelatedTableAsync(tableName, projectLinkId, archiveProgress, progress);
        }

        // Zip project CAD drawings from FactoryDatabasePath and copy to ProjectFilesDestinationPath; delete source if not dry run
        await ZipAndCopyProjectCadDrawingsAsync(projectLinkId);

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

        // Finalize progress totals
        archiveProgress.TablesProcessed = archiveProgress.TotalTables;
        archiveProgress.CurrentTable = "Completed";
        progress?.Report(archiveProgress);

        stopwatch.Stop();
        archiveProgress.Status = "Archive completed successfully";
        archiveProgress.IsComplete = true;
        archiveProgress.EndTime = DateTime.Now;
        // Mark overall project progress complete for single-project run
        archiveProgress.ProjectsProcessed = Math.Max(archiveProgress.ProjectsProcessed, 1);
        progress?.Report(archiveProgress);

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

    /// <summary>Zip the project CAD drawings directory from FactoryDatabasePath and copy to ProjectFilesDestinationPath. Delete source directory if not dry run.</summary>
    private async Task ZipAndCopyProjectCadDrawingsAsync(string projectLinkId)
    {
      try
      {
        var sourceRoot = _config.FactoryDatabasePath?.Trim();
        var destRoot = _config.ProjectFilesDestinationPath?.Trim();

        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(destRoot))
        {
          _loggingService?.LogInfo("Files", "Skipping project CAD copy: FactoryDatabasePath or ProjectFilesDestinationPath not configured");
          return;
        }

        // Directories under FactoryDatabasePath are named like the project; try project name (raw then sanitized) then LinkID
        var projectName = await GetProjectDisplayNameAsync(projectLinkId);
        var candidateDirs = new[]
        {
          Path.Combine(sourceRoot, projectName),
          Path.Combine(sourceRoot, SanitizeDirectoryName(projectName)),
          Path.Combine(sourceRoot, projectLinkId)
        };

        string? sourceDir = null;
        foreach (var dir in candidateDirs)
        {
          if (Directory.Exists(dir)) { sourceDir = dir; break; }
        }

        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
        {
          _loggingService?.LogInfo("Files", "No project CAD directory found", $"Under: {sourceRoot} (tried project name and LinkID)");
          return;
        }

        Directory.CreateDirectory(destRoot);
        // Use LinkID for zip name so it's unique and filesystem-safe
        var safeZipName = SanitizeFileName(projectLinkId) + ".zip";
        var zipPath = Path.Combine(destRoot, safeZipName);

        if (_config.OverwriteExisting && File.Exists(zipPath))
        {
          try { File.Delete(zipPath); } catch (Exception ex) { _loggingService?.LogWarning("Files", "Failed to delete existing zip before overwrite", ex.Message); }
        }

        await Task.Run(() =>
        {
          if (File.Exists(zipPath)) File.Delete(zipPath);
          ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        });

        _loggingService?.LogArchive("Project CAD drawings zipped and copied", $"From: {sourceDir} To: {zipPath}");

        if (_config.IsDryRun)
        {
          _loggingService?.LogInfo("Files", "Dry run: source project CAD directory not deleted", sourceDir!);
        }
        else
        {
          try
          {
            Directory.Delete(sourceDir, recursive: true);
            _loggingService?.LogArchive("Source project CAD directory deleted", sourceDir);
          }
          catch (Exception ex)
          {
            _loggingService?.LogWarning("Files", "Failed to delete source project directory after zip", ex.Message);
          }
        }
      }
      catch (Exception ex)
      {
        _loggingService?.LogWarning("Files", "Error zipping/copying project CAD drawings", ex.Message);
      }
    }

    private static string SanitizeDirectoryName(string name)
    {
      if (string.IsNullOrWhiteSpace(name)) return name;
      var invalid = Path.GetInvalidFileNameChars();
      var sanitized = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
      return string.IsNullOrEmpty(sanitized) ? "_" : sanitized;
    }

    private static string SanitizeFileName(string name)
    {
      if (string.IsNullOrWhiteSpace(name)) return name;
      var invalid = Path.GetInvalidFileNameChars();
      var sanitized = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
      return string.IsNullOrEmpty(sanitized) ? "_" : sanitized;
    }

    public async Task<ArchiveProgress> ArchiveSelectedProjectsAsync(List<string> projectLinkIds, IProgress<ArchiveProgress>? progress = null)
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Starting selected projects archive", $"Count: {projectLinkIds.Count}");
      _loggingService?.LogArchive(_config.SkipCatalogTables ? "Skip Catalog Tables: ON — catalog tables will be skipped" : "Skip Catalog Tables: OFF — catalog tables will be synced", null!);

      var archiveProgress = new ArchiveProgress
      {
        StartTime = DateTime.Now,
        TotalTables = CalculateTotalTables(true), // Always sync references in batch mode
        TotalProjects = projectLinkIds.Count
      };

      try
      {
        // Sync reference tables once for the batch
        var refsOk = await SyncReferenceTablesAsync(archiveProgress, progress);
        if (!refsOk)
        {
          throw new InvalidOperationException("Reference table synchronization failed. See logs for details.");
        }

        // Skip catalog tables if configured to do so (for faster debugging)
        _loggingService?.LogInfo("Archive", $"SkipCatalogTables setting value", $"SkipCatalogTables = {_config.SkipCatalogTables}");
        if (!_config.SkipCatalogTables)
        {
          _loggingService?.LogInfo("Archive", "Syncing catalog tables", "SkipCatalogTables is false, proceeding with catalog sync");
          // Sync catalog tables (ToolFiles, ProcessingStations, Employees, etc.)
          var catalogOk = await SyncCatalogTablesAsync(archiveProgress, progress);
          if (!catalogOk)
          {
            throw new InvalidOperationException("Catalog table synchronization failed. See logs for details.");
          }

          // Populate library template data after reference tables are synced
          var libTemplatesOk = await PopulateLibraryTemplateDataAsync(archiveProgress, progress);
          if (!libTemplatesOk)
          {
            throw new InvalidOperationException("Library template data population failed. See logs for details.");
          }
        }
        else
        {
          _loggingService?.LogInfo("Archive", "Skipping catalog tables", $"SkipCatalogTables setting is enabled (value: {_config.SkipCatalogTables}) for faster debugging");
        }

        foreach (var linkId in projectLinkIds)
        {
          archiveProgress.CurrentProject = await GetProjectDisplayNameAsync(linkId);
          archiveProgress.ProjectsProcessed++;
          progress?.Report(archiveProgress);

          _loggingService?.LogArchive($"Processing selected project {archiveProgress.ProjectsProcessed} of {archiveProgress.TotalProjects}",
            $"Project LinkID: {linkId}");

          await ArchiveProjectAsync(linkId, null, false); // Skip ref sync per project in batch mode
        }

        stopwatch.Stop();
        archiveProgress.IsComplete = true;
        archiveProgress.EndTime = DateTime.Now;
        archiveProgress.Status = _config.IsDryRun ? "Selected projects archived (Dry Run)" : "Selected projects archived and source cleaned";

        _loggingService?.LogArchive("Selected projects archive completed",
          $"Total Projects: {archiveProgress.TotalProjects}\n" +
          $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s\n" +
          $"Status: {archiveProgress.Status}");
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        archiveProgress.Status = $"Archive failed: {ex.Message}";
        archiveProgress.EndTime = DateTime.Now;

        _loggingService?.LogError("Archive", $"Selected projects archive failed after {stopwatch.Elapsed.TotalSeconds:F2}s",
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
        var copyQuery = $"INSERT INTO {QuoteIdentifier("dbo")}.{QuoteIdentifier("Projects")} SELECT * FROM {QuoteIdentifier("Projects")} WHERE {QuoteIdentifier("ID")} = @ProjectId";
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
      progress.CurrentTable = tableName;
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

        // Get count of records to archive using manifest-driven query
        var countQuery = BuildProjectRelatedCountQuery(tableName, projectLinkId);
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
            var batchQuery = BuildProjectRelatedBatchQuery(tableName, projectLinkId);

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
              await BulkCopyToDestinationWithDuplicateCheckAsync(destConn, tableName, dataTable, projectLinkId);
              bulkCopyStopwatch.Stop();

              totalProcessed += dataTable.Rows.Count;
              _loggingService?.LogDatabase("Batch query", batchQuery,
                $"@ProjectLinkId={projectLinkId}, @Offset={offset}, @BatchSize={batchSize}", batchStopwatch.Elapsed);
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

        // Table considered processed regardless of record count
        progress.TablesProcessed++;
        progressReporter?.Report(progress);
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

    private async Task ArchiveTableByColumnAsync(string tableName, string filterColumn, string filterValue, ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      progress.Status = $"Archiving {tableName} where {filterColumn} = {filterValue}";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive($"Starting table archive: {tableName}", $"Filter: {filterColumn} = {filterValue}");

      try
      {
        using var sourceConn = new SqlConnection(_sourceConnectionString);
        using var destConn = new SqlConnection(_destinationConnectionString);

        await sourceConn.OpenAsync();
        await destConn.OpenAsync();

        // Get count of records to archive
        var countQuery = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(filterColumn)} = @FilterValue";
        using var countCmd = new SqlCommand(countQuery, sourceConn);
        countCmd.Parameters.AddWithValue("@FilterValue", filterValue);

        var countResult = await countCmd.ExecuteScalarAsync();
        var recordCount = countResult != null ? Convert.ToInt32(countResult) : 0;

        _loggingService?.LogDatabase("Count records", countQuery, $"@FilterValue={filterValue}", stopwatch.Elapsed);

        if (recordCount > 0)
        {
          progress.TotalRecords = recordCount;
          progress.CurrentRecord = 0;

          _loggingService?.LogArchive($"Found {recordCount} records in {tableName}", $"Filter: {filterColumn} = {filterValue}");

          // Check if table exists in destination, if not create it
          await EnsureTableExistsAsync(destConn, tableName, sourceConn);

          // If overwrite is enabled, delete existing matching records first
          var existingCountQuery = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(filterColumn)} = @FilterValue";
          using (var existingCountCmd = new SqlCommand(existingCountQuery, destConn))
          {
            existingCountCmd.Parameters.AddWithValue("@FilterValue", filterValue);
            var existingCountResult = await existingCountCmd.ExecuteScalarAsync();
            var existingCount = existingCountResult != null ? Convert.ToInt32(existingCountResult) : 0;

            if (_config.OverwriteExisting && existingCount > 0)
            {
              var deleteQuery = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(filterColumn)} = @FilterValue";
              using var deleteCmd = new SqlCommand(deleteQuery, destConn);
              deleteCmd.Parameters.AddWithValue("@FilterValue", filterValue);
              var deletedRows = await deleteCmd.ExecuteNonQueryAsync();
              _loggingService?.LogArchive($"Deleted existing records from {tableName}", $"Rows affected: {deletedRows}");
            }
          }

          // Archive records in batches
          var batchSize = 1000;
          var offset = 0;
          var totalProcessed = 0;

          while (offset < recordCount)
          {
            var batchQuery = $@"
              SELECT * FROM {QuoteIdentifier(tableName)}
              WHERE {QuoteIdentifier(filterColumn)} = @FilterValue
              ORDER BY {QuoteIdentifier("ID")}
              OFFSET @Offset ROWS
              FETCH NEXT @BatchSize ROWS ONLY";

            using var batchCmd = new SqlCommand(batchQuery, sourceConn);
            batchCmd.Parameters.AddWithValue("@FilterValue", filterValue);
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
                $"@FilterValue={filterValue}, @Offset={offset}, @BatchSize={batchSize}", batchStopwatch.Elapsed);
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
          _loggingService?.LogInfo("Archive", $"No records found in {tableName}", $"Filter: {filterColumn} = {filterValue}");
        }

        // Count this filtered table as processed as well
        progress.TablesProcessed++;
        progressReporter?.Report(progress);
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        progress.Status = $"Error archiving {tableName}: {ex.Message}";
        progressReporter?.Report(progress);

        _loggingService?.LogError("Archive", $"Failed to archive table {tableName}",
          $"Filter: {filterColumn} = {filterValue}\nError: {ex.Message}\nDuration: {stopwatch.Elapsed.TotalSeconds:F2}s");
        throw;
      }
    }


    private async Task EnsureTemplateRecordsExistAsync(SqlConnection destConn, ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      progress.Status = "Ensuring template records exist in destination";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogArchive("Ensuring template records exist", "Copying template records (Type=6) to destination");

      // Tables that contain template records
      var templateTables = new[] { "GlobalFiles", "DoorWizardFiles", "ProjectWizardFiles", "CutPartsFiles", "EdgebandFiles", "HardwareFiles" };

      try
      {
        using var sourceConn = new SqlConnection(_sourceConnectionString);
        await sourceConn.OpenAsync();

        foreach (var tableName in templateTables)
        {
          // Check if table exists in destination
          await EnsureTableExistsAsync(destConn, tableName, sourceConn);

          // Check if template records already exist in destination
          var existingCountQuery = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier("Type")} = 6";
          using var existingCountCmd = new SqlCommand(existingCountQuery, destConn);
          var existingCountResult = await existingCountCmd.ExecuteScalarAsync();
          var existingCount = existingCountResult != null ? Convert.ToInt32(existingCountResult) : 0;

          if (existingCount == 0)
          {
            // Copy template records from source to destination
            var copyQuery = $"INSERT INTO {QuoteIdentifier(tableName)} SELECT * FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier("Type")} = 6";
            using var copyCmd = new SqlCommand(copyQuery, destConn);
            var rowsAffected = await copyCmd.ExecuteNonQueryAsync();

            _loggingService?.LogArchive($"Template records copied to {tableName}", $"Rows affected: {rowsAffected}");
          }
          else
          {
            _loggingService?.LogInfo("Archive", $"Template records already exist in {tableName}", $"Count: {existingCount}");
          }
        }

        stopwatch.Stop();
        _loggingService?.LogArchive("Template records check completed", $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
      }
      catch (Exception ex)
      {
        stopwatch.Stop();
        _loggingService?.LogError("Archive", "Failed to ensure template records exist", $"Error: {ex.Message}");
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
        DestinationTableName = $"dbo.{QuoteIdentifier(tableName)}",
        BatchSize = 1000
      };

      // Map only columns that exist in destination to avoid schema mismatch errors
      var destColumns = await GetDestinationColumnNamesAsync(destConn, tableName);
      foreach (DataColumn column in dataTable.Columns)
      {
        if (column.ColumnName != null && destColumns.Contains(column.ColumnName))
        {
          bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }
      }

      await bulkCopy.WriteToServerAsync(dataTable);
    }

    private static async Task<HashSet<string>> GetDestinationColumnNamesAsync(SqlConnection destConn, string tableName)
    {
      var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var query = @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName";
      using var cmd = new SqlCommand(query, destConn);
      cmd.Parameters.AddWithValue("@TableName", tableName);
      using var reader = await cmd.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        columns.Add(reader.GetString(0));
      }
      reader.Close();
      return columns;
    }

    private static async Task<Dictionary<string, string>> GetDestinationColumnTypesAsync(SqlConnection destConn, string tableName)
    {
      var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var query = @"SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName";
      using var cmd = new SqlCommand(query, destConn);
      cmd.Parameters.AddWithValue("@TableName", tableName);
      using var reader = await cmd.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        columns[reader.GetString(0)] = reader.GetString(1);
      }
      reader.Close();
      return columns;
    }

    private static bool IsBinaryTypeName(string dataType)
    {
      switch (dataType.ToLowerInvariant())
      {
        case "varbinary":
        case "image":
        case "binary":
        case "timestamp":
          return true;
        default:
          return false;
      }
    }

    private async Task BulkCopyToDestinationWithDuplicateCheckAsync(SqlConnection destConn, string tableName, DataTable dataTable, string projectLinkId)
    {
      // Check if this is a file table that needs special handling
      var fileTables = new[] { "GlobalFiles", "DoorWizardFiles", "ProjectWizardFiles", "CutPartsFiles", "EdgebandFiles", "HardwareFiles" };
      var isFileTable = fileTables.Contains(tableName);

      if (isFileTable)
      {
        // For file tables, we need to ensure template records exist and copy project files
        await HandleFileTableArchiveAsync(destConn, tableName, dataTable, projectLinkId);
      }
      else
      {
        // For regular tables, check if records already exist for this project (manifest-driven; not all tables use LinkIDProject)
        var existingCountQuery = BuildProjectRelatedCountQuery(tableName, projectLinkId);
        using var existingCountCmd = new SqlCommand(existingCountQuery, destConn);
        existingCountCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

        var existingCountResult = await existingCountCmd.ExecuteScalarAsync();
        var existingCount = existingCountResult != null ? Convert.ToInt32(existingCountResult) : 0;

        if (_config.OverwriteExisting && existingCount > 0)
        {
          // In overwrite mode, delete existing records and proceed
          _loggingService?.LogWarning("Archive", $"Records already exist in {tableName} for this project",
            $"Project LinkID: {projectLinkId}, Existing records: {existingCount}. Deleting existing records for overwrite.");

          var deleteQuery = BuildProjectRelatedDeleteQuery(tableName, projectLinkId);
          // CTE deletes (e.g., Subassemblies) need MAXRECURSION at the end.
          if (IsCteQuery(deleteQuery))
          {
            deleteQuery += "\nOPTION (MAXRECURSION 0)";
          }
          using var deleteCmd = new SqlCommand(deleteQuery, destConn);
          deleteCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);
          var deletedRows = await deleteCmd.ExecuteNonQueryAsync();

          _loggingService?.LogArchive($"Deleted existing records from {tableName}", $"Rows affected: {deletedRows}");
        }

        // Proceed with bulk copy; if not overwriting and records exist, this will add any missing rows only
        await BulkCopyToDestinationAsync(destConn, tableName, dataTable);
      }
    }

    private async Task HandleFileTableArchiveAsync(SqlConnection destConn, string tableName, DataTable dataTable, string projectLinkId)
    {
      // First, ensure template records (Type=6) exist in destination
      var templateCountQuery = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier("Type")} = 6";
      using var templateCountCmd = new SqlCommand(templateCountQuery, destConn);
      var templateCountResult = await templateCountCmd.ExecuteScalarAsync();
      var templateCount = templateCountResult != null ? Convert.ToInt32(templateCountResult) : 0;

      if (templateCount == 0)
      {
        // Copy template records (Type=6) from source to destination
        using var sourceConn = new SqlConnection(_sourceConnectionString);
        await sourceConn.OpenAsync();

        var selectTemplateQuery = $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier("Type")} = 6";
        using var selectCmd = new SqlCommand(selectTemplateQuery, sourceConn);
        using var reader = await selectCmd.ExecuteReaderAsync();
        var templateTable = new DataTable();
        templateTable.Load(reader);
        reader.Close();

        if (templateTable.Rows.Count > 0)
        {
          await BulkCopyToDestinationAsync(destConn, tableName, templateTable);
          _loggingService?.LogInfo("Archive", $"Template records copied to {tableName}", $"Rows: {templateTable.Rows.Count}");
        }
      }

      // Now check if project-specific files already exist (manifest-driven; not all tables use LinkIDProject)
      var existingCountQuery = BuildProjectRelatedCountQuery(tableName, projectLinkId);
      using var existingCountCmd = new SqlCommand(existingCountQuery, destConn);
      existingCountCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

      var existingCountResult = await existingCountCmd.ExecuteScalarAsync();
      var existingCount = existingCountResult != null ? Convert.ToInt32(existingCountResult) : 0;

      if (existingCount > 0)
      {
        // In overwrite mode, delete existing project files and proceed
        _loggingService?.LogWarning("Archive", $"Project files already exist in {tableName} for this project",
          $"Project LinkID: {projectLinkId}, Existing records: {existingCount}. Deleting existing files for overwrite.");

        var deleteQuery = BuildProjectRelatedDeleteQuery(tableName, projectLinkId);
        if (IsCteQuery(deleteQuery))
        {
          deleteQuery += "\nOPTION (MAXRECURSION 0)";
        }
        using var deleteCmd = new SqlCommand(deleteQuery, destConn);
        deleteCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);
        var deletedRows = await deleteCmd.ExecuteNonQueryAsync();

        _loggingService?.LogArchive($"Deleted existing project files from {tableName}", $"Rows affected: {deletedRows}");
      }

      // Copy project-specific files
      await BulkCopyToDestinationAsync(destConn, tableName, dataTable);
    }

    private async Task DeleteExistingProjectFromDestinationAsync(SqlConnection destConn, string projectLinkId)
    {
      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogWarning("Archive", "Deleting existing project from destination for overwrite", $"Project LinkID: {projectLinkId}");

      // Delete in reverse order to handle foreign key constraints
      var projectRelatedTables = _projectRelatedTables;
      var tablesToDelete = projectRelatedTables.Reverse().ToArray();
      var allTablesToDelete = tablesToDelete.Concat(new[] { "Projects" }).ToArray();

      foreach (var tableName in allTablesToDelete)
      {
        if (tableName.Equals("Projects", StringComparison.OrdinalIgnoreCase))
          continue; // Handled below by LinkID
        try
        {
          var deleteQuery = BuildDeleteFromSourceQuery(tableName);
          using var deleteCmd = new SqlCommand(deleteQuery, destConn);
          deleteCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

          var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
          _loggingService?.LogArchive($"Deleted existing records from {tableName}", $"Rows affected: {rowsAffected}");
        }
        catch (Exception ex)
        {
          // Some tables might not exist or have different schema - log but continue
          _loggingService?.LogWarning("Archive", $"Could not delete from {tableName} during overwrite", ex.Message);
        }
      }

      // Delete the main project record (Projects key is LinkID, not LinkIDProject)
      try
      {
        var deleteProjectQuery = $"DELETE FROM {QuoteIdentifier("Projects")} WHERE {QuoteIdentifier("LinkID")} = @ProjectLinkId";
        using var deleteProjectCmd = new SqlCommand(deleteProjectQuery, destConn);
        deleteProjectCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);
        var projectRowsAffected = await deleteProjectCmd.ExecuteNonQueryAsync();
        _loggingService?.LogArchive("Deleted existing project record", $"Rows affected: {projectRowsAffected}");
      }
      catch (Exception ex)
      {
        _loggingService?.LogError("Archive", "Failed to delete existing project record", ex.Message);
        throw;
      }

      stopwatch.Stop();
      _loggingService?.LogArchive("Existing project deletion completed", $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
    }

    private async Task DeleteFromSourceAsync(string projectLinkId, ArchiveProgress progress, IProgress<ArchiveProgress>? progressReporter)
    {
      // Defense-in-depth: never delete from source when dry run is set (even if caller missed the check)
      if (_config.IsDryRun)
      {
        _loggingService?.LogInfo("Archive", "Dry run — skipping source cleanup (no rows deleted)", $"Project LinkID: {projectLinkId}");
        return;
      }

      progress.Status = "Cleaning source database (not dry run)";
      progressReporter?.Report(progress);

      var stopwatch = Stopwatch.StartNew();
      _loggingService?.LogWarning("Archive", "Starting source cleanup", $"Project LinkID: {projectLinkId}");

      using var sourceConn = new SqlConnection(_sourceConnectionString);
      await sourceConn.OpenAsync();

      // Delete in reverse order to handle foreign key constraints
      var projectRelatedTables = _projectRelatedTables;
      var tablesToDelete = projectRelatedTables.Reverse().ToList();
      tablesToDelete.Add("Projects");

      foreach (var tableName in tablesToDelete)
      {
        string deleteQuery = tableName.Equals("Projects", StringComparison.OrdinalIgnoreCase)
          ? $"DELETE FROM {QuoteIdentifier("Projects")} WHERE {QuoteIdentifier("LinkID")} = @ProjectLinkId"
          : BuildDeleteFromSourceQuery(tableName);
        using var deleteCmd = new SqlCommand(deleteQuery, sourceConn);
        deleteCmd.Parameters.AddWithValue("@ProjectLinkId", projectLinkId);

        var rowsAffected = await deleteCmd.ExecuteNonQueryAsync();

        _loggingService?.LogDatabase("Delete from source", deleteQuery, $"@ProjectLinkId={projectLinkId}", null);
        _loggingService?.LogArchive($"Deleted from {tableName}", $"Rows affected: {rowsAffected}");
      }

      stopwatch.Stop();
      _loggingService?.LogArchive("Source cleanup completed", $"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
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
