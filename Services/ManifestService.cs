using System;
using System.IO;
using System.Linq;
using MVArchive.Models;
using System.Collections.Generic;

namespace MVArchive.Services
{
  public class ManifestService
  {
    private ArchiveManifest? _manifest;
    private readonly string _manifestPath;

    public ManifestService(string manifestPath = "manifest.xml")
    {
      _manifestPath = manifestPath;
    }

    public ArchiveManifest Manifest
    {
      get
      {
        if (_manifest == null)
        {
          LoadManifest();
        }
        return _manifest!;
      }
    }

    public void LoadManifest()
    {
      try
      {
        if (!File.Exists(_manifestPath))
        {
          throw new FileNotFoundException($"Manifest file not found: {_manifestPath}");
        }

        var xmlContent = File.ReadAllText(_manifestPath);
        _manifest = ArchiveManifest.Parse(xmlContent);
      }
      catch (Exception ex)
      {
        throw new InvalidOperationException($"Failed to load manifest from {_manifestPath}: {ex.Message}", ex);
      }
    }

    public void ReloadManifest()
    {
      _manifest = null;
      LoadManifest();
    }

    public string GetParameterValue(string parameterName)
    {
      var parameter = Manifest.Parameters.FirstOrDefault(p =>
        string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
      return parameter?.Value ?? string.Empty;
    }

    public ManifestCatalog? GetCatalog(string catalogName)
    {
      return Manifest.Catalogs.FirstOrDefault(c =>
        string.Equals(c.Name, catalogName, StringComparison.OrdinalIgnoreCase));
    }

    public ManifestCatalog? GetCatalogByTable(string tableName)
    {
      return Manifest.Catalogs.FirstOrDefault(c =>
        string.Equals(c.Table, tableName, StringComparison.OrdinalIgnoreCase));
    }

    public string[] GetAllTableNames()
    {
      return Manifest.Catalogs.Select(c => c.Table).ToArray();
    }

    public string[] GetTablesByMode(string mode)
    {
      return Manifest.Catalogs
        .Where(c => string.Equals(c.Mode, mode, StringComparison.OrdinalIgnoreCase))
        .Select(c => c.Table)
        .ToArray();
    }

    public string[] GetProjectRelatedTables()
    {
      var projectTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      void Traverse(MVArchive.Models.ManifestRelationship rel)
      {
        if (!string.IsNullOrWhiteSpace(rel.From))
        {
          projectTables.Add(rel.From);
        }
        foreach (var nested in rel.NestedRelationships)
        {
          Traverse(nested);
        }
      }

      foreach (var relationship in Manifest.Root.Relationships)
      {
        Traverse(relationship);
      }

      return projectTables.ToArray();
    }

    public string[] GetReferenceTables()
    {
      // Tables with mode="all_rows" OR singleton="true" are reference tables (singletons are identical to all_rows)
      var allRowsTables = GetTablesByMode("all_rows").ToList();
      var singletonTables = Manifest.Catalogs
        .Where(c => c.IsSingleton)
        .Select(c => c.Table)
        .Distinct(StringComparer.OrdinalIgnoreCase);
      return allRowsTables.Union(singletonTables, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string[] GetLibraryTemplateTables()
    {
      // Tables that contain library template data
      var templateTables = new HashSet<string>();

      foreach (var catalog in Manifest.Catalogs)
      {
        if (catalog.Name.StartsWith("Template_", StringComparison.OrdinalIgnoreCase))
        {
          templateTables.Add(catalog.Table);
        }
      }

      return templateTables.ToArray();
    }

    /// <summary>
    /// Catalog tables that are scoped by LibraryID (i.e. any catalog filter references @LibraryID).
    /// These should be synced using the manifest filters with a populated library id list.
    /// </summary>
    public string[] GetLibraryScopedCatalogTables()
    {
      return Manifest.Catalogs
        .Where(c => c.Filters.Any(f =>
          !string.IsNullOrWhiteSpace(f.Where) &&
          f.Where.Contains("@LibraryID", StringComparison.OrdinalIgnoreCase)))
        .Select(c => c.Table)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    public string[] GetBinaryDataTables()
    {
      // Prefer manifest-defined binary tables; fall back to known defaults if none are flagged
      var flagged = Manifest.Catalogs.Where(c => c.IsBinary).Select(c => c.Table).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
      if (flagged.Length > 0)
      {
        return flagged;
      }

      return
      [
        "AutoCADDrawings", "TiffDrawings", "CutPartsFiles", "ProjectWizardFiles",
        "SpecificationGroups", "Attachment", "FaceFrameImages", "GlobalImages"
      ];
    }

    /// <summary>Get catalog tables that need full sync: all catalog tables excluding project-related, templates, reference tables (includes singletons), and already-handled (Libraries, Categories).</summary>
    public string[] GetCatalogTablesToSync()
    {
      var projectTables = new HashSet<string>(GetProjectRelatedTables(), StringComparer.OrdinalIgnoreCase);
      var templateTables = new HashSet<string>(GetLibraryTemplateTables(), StringComparer.OrdinalIgnoreCase);
      var referenceTables = new HashSet<string>(GetReferenceTables(), StringComparer.OrdinalIgnoreCase);
      var libraryScopedTables = new HashSet<string>(GetLibraryScopedCatalogTables(), StringComparer.OrdinalIgnoreCase);

      // Exclude: project-related, templates, reference tables (includes singletons), Libraries (handled in template phase), Categories (handled separately)
      var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
      {
        "Libraries", "Categories"
      };
      excluded.UnionWith(projectTables);
      excluded.UnionWith(templateTables);
      excluded.UnionWith(referenceTables);
      excluded.UnionWith(libraryScopedTables);

      return Manifest.Catalogs
        .Where(c => !excluded.Contains(c.Table))
        .Select(c => c.Table)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    public string BuildCatalogQuery(ManifestCatalog catalog, string projectId, string libraryId)
    {
      // Back-compat: wrap single values and delegate to list-based overload
      var projectIds = string.IsNullOrEmpty(projectId) ? Array.Empty<string>() : [projectId];
      var libraryIds = string.IsNullOrEmpty(libraryId) ? Array.Empty<string>() : [libraryId];
      return BuildCatalogQuery(catalog, projectIds, libraryIds);
    }

    public string BuildCatalogQuery(ManifestCatalog catalog, string projectId, IReadOnlyList<string> libraryIds)
    {
      // Delegate to unified list-based overload
      var projectIds = string.IsNullOrEmpty(projectId) ? Array.Empty<string>() : [projectId];
      return BuildCatalogQuery(catalog, projectIds, libraryIds);
    }

    public string BuildCatalogQuery(ManifestCatalog catalog, IReadOnlyList<string> projectIds, IReadOnlyList<string> libraryIds)
    {
      var tableName = catalog.Table;
      var baseQuery = $"SELECT * FROM {QuoteIdentifier(tableName)}";

      if (catalog.Filters.Count == 0)
      {
        return baseQuery;
      }

      var whereConditions = new List<string>();

      string ToInList(IReadOnlyList<string> values)
      {
        if (values == null || values.Count == 0)
        {
          return "('')"; // empty list guard
        }
        return "(" + string.Join(",", values.Select(v => $"'{v.Replace("'", "''", StringComparison.Ordinal)}'")) + ")";
      }

      var libInList = ToInList(libraryIds);
      var projInList = ToInList(projectIds);
      var firstLib = (libraryIds != null && libraryIds.Count > 0) ? libraryIds[0] : string.Empty;
      var firstProj = (projectIds != null && projectIds.Count > 0) ? projectIds[0] : string.Empty;

      foreach (var filter in catalog.Filters)
      {
        var condition = (filter.Where ?? string.Empty).Trim();

        // Ignore empty/whitespace filters to avoid generating invalid SQL like "WHERE  AND ..."
        if (string.IsNullOrWhiteSpace(condition))
        {
          continue;
        }

        // Handle IN(@LibraryID)
        if (condition.Contains("IN (@LibraryID)", StringComparison.OrdinalIgnoreCase))
        {
          condition = condition.Replace("IN (@LibraryID)", $"IN {libInList}");
        }
        else
        {
          condition = condition.Replace("@LibraryID", $"'{firstLib.Replace("'", "''", StringComparison.Ordinal)}'");
        }

        // Handle IN(@ProjectID)
        if (condition.Contains("IN (@ProjectID)", StringComparison.OrdinalIgnoreCase))
        {
          condition = condition.Replace("IN (@ProjectID)", $"IN {projInList}");
        }
        else
        {
          condition = condition.Replace("@ProjectID", $"'{firstProj.Replace("'", "''", StringComparison.Ordinal)}'");
        }

        // Wrap each filter to avoid precedence surprises if a filter contains OR, etc.
        whereConditions.Add($"({condition})");
      }

      if (whereConditions.Count > 0)
      {
        baseQuery += " WHERE " + string.Join(" AND ", whereConditions);
      }

      return baseQuery;
    }

    private static string QuoteIdentifier(string identifier)
    {
      return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
  }
}
