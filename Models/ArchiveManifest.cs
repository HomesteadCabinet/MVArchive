using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace MVArchive.Models
{
  public class ArchiveManifest
  {
    public string ManifestVersion { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public List<ManifestParameter> Parameters { get; set; } = new();
    public List<ManifestCatalog> Catalogs { get; set; } = new();
    public ManifestRoot Root { get; set; } = new();
    public ManifestIntegrity Integrity { get; set; } = new();

    public static ArchiveManifest Parse(string xmlContent)
    {
      var doc = XDocument.Parse(xmlContent);
      var root = doc.Root;
      if (root == null || root.Name.LocalName != "ProjectArchiveManifest")
        throw new ArgumentException("Invalid manifest XML: missing ProjectArchiveManifest root element");

      var manifest = new ArchiveManifest
      {
        ManifestVersion = root.Attribute("manifest_version")?.Value ?? "1.0.0",
        Domain = root.Attribute("domain")?.Value ?? "ProjectArchive"
      };

      // Parse Parameters
      var parametersElement = root.Element("Parameters");
      if (parametersElement != null)
      {
        foreach (var param in parametersElement.Elements("Parameter"))
        {
          manifest.Parameters.Add(new ManifestParameter
          {
            Name = param.Attribute("name")?.Value ?? string.Empty,
            Value = param.Attribute("value")?.Value ?? string.Empty
          });
        }
      }

      // Parse Catalogs
      var catalogsElement = root.Element("Catalogs");
      if (catalogsElement != null)
      {
        foreach (var catalog in catalogsElement.Elements("Catalog"))
        {
          var manifestCatalog = new ManifestCatalog
          {
            Name = catalog.Attribute("name")?.Value ?? string.Empty,
            Table = catalog.Attribute("table")?.Value ?? string.Empty,
            PrimaryKey = catalog.Attribute("pk")?.Value ?? string.Empty,
            Mode = catalog.Attribute("mode")?.Value ?? "changes_only",
            IsBinary = string.Equals(catalog.Attribute("binary")?.Value, "true", StringComparison.OrdinalIgnoreCase),
            IsSingleton = string.Equals(catalog.Attribute("singleton")?.Value, "true", StringComparison.OrdinalIgnoreCase)
          };

          // Parse filters
          foreach (var filter in catalog.Elements("Filter"))
          {
            manifestCatalog.Filters.Add(new ManifestFilter
            {
              Where = filter.Attribute("where")?.Value ?? string.Empty
            });
          }

          manifest.Catalogs.Add(manifestCatalog);
        }
      }

      // Parse Root
      var rootElement = root.Element("Root");
      if (rootElement != null)
      {
        manifest.Root = new ManifestRoot
        {
          Entity = rootElement.Attribute("entity")?.Value ?? string.Empty,
          Key = rootElement.Attribute("key")?.Value ?? string.Empty
        };

        // Parse Selector
        var selectorElement = rootElement.Element("Selector");
        if (selectorElement != null)
        {
          manifest.Root.Selector = new ManifestSelector
          {
            Column = selectorElement.Attribute("column")?.Value ?? string.Empty,
            Value = selectorElement.Attribute("value")?.Value ?? string.Empty
          };
        }

        // Parse Relationships
        var relationshipsElement = rootElement.Element("Relationships");
        if (relationshipsElement != null)
        {
          foreach (var rel in relationshipsElement.Elements("Relationship"))
          {
            manifest.Root.Relationships.Add(ParseRelationshipElement(rel));
          }
        }
      }

      // Parse Integrity
      var integrityElement = root.Element("Integrity");
      if (integrityElement != null)
      {
        manifest.Integrity = new ManifestIntegrity
        {
          EnforceFkCheck = integrityElement.Attribute("enforce_fk_check")?.Value == "true",
          UnexpectedRowPolicy = integrityElement.Attribute("unexpected_row_policy")?.Value ?? "warn",
          OrphanLinkPolicy = integrityElement.Attribute("orphan_link_policy")?.Value ?? "skip"
        };
      }

      return manifest;
    }

    private static ManifestRelationship ParseRelationshipElement(XElement rel)
    {
      var relationship = new ManifestRelationship
      {
        From = rel.Attribute("from")?.Value ?? string.Empty,
        To = rel.Attribute("to")?.Value ?? string.Empty,
        Type = rel.Attribute("type")?.Value ?? string.Empty,
        LinkLogic = rel.Attribute("linkLogic")?.Value ?? "AND" // Default to AND for safety
      };

      // Parse all ViaLink elements
      foreach (var viaLinkElement in rel.Elements("ViaLink"))
      {
        var viaLink = new ManifestViaLink
        {
          Lhs = viaLinkElement.Attribute("lhs")?.Value ?? string.Empty,
          Rhs = viaLinkElement.Attribute("rhs")?.Value ?? string.Empty
        };
        relationship.ViaLinks.Add(viaLink);
      }

      // Backward compatibility: set ViaLink to first ViaLink if only one exists
      if (relationship.ViaLinks.Count == 1)
      {
        relationship.ViaLink = relationship.ViaLinks[0];
      }
      else if (relationship.ViaLinks.Count > 1)
      {
        // For backward compatibility, set ViaLink to first one, but ViaLinks list will be used
        relationship.ViaLink = relationship.ViaLinks[0];
      }

      foreach (var nestedRel in rel.Elements("Relationship"))
      {
        relationship.NestedRelationships.Add(ParseRelationshipElement(nestedRel));
      }

      return relationship;
    }
  }

  public class ManifestParameter
  {
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
  }

  public class ManifestCatalog
  {
    public string Name { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string PrimaryKey { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public List<ManifestFilter> Filters { get; set; } = new();
    public bool IsBinary { get; set; }
    public bool IsSingleton { get; set; }
  }

  public class ManifestFilter
  {
    public string Where { get; set; } = string.Empty;
  }

  public class ManifestRoot
  {
    public string Entity { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public ManifestSelector? Selector { get; set; }
    public List<ManifestRelationship> Relationships { get; set; } = new();
  }

  public class ManifestSelector
  {
    public string Column { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
  }

  public class ManifestRelationship
  {
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public ManifestViaLink? ViaLink { get; set; } // Backward compatibility - use first ViaLink if only one exists
    public List<ManifestViaLink> ViaLinks { get; set; } = new(); // Multiple ViaLinks with OR/AND logic
    public string LinkLogic { get; set; } = "AND"; // "AND" or "OR" - default to AND for safety
    public List<ManifestRelationship> NestedRelationships { get; set; } = new();
  }

  public class ManifestViaLink
  {
    public string Lhs { get; set; } = string.Empty;
    public string Rhs { get; set; } = string.Empty;
  }

  public class ManifestIntegrity
  {
    public bool EnforceFkCheck { get; set; }
    public string UnexpectedRowPolicy { get; set; } = string.Empty;
    public string OrphanLinkPolicy { get; set; } = string.Empty;
  }
}
