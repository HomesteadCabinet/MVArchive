# ArchiveService Manifest-Driven Refactoring

## Overview

The `ArchiveService` has been refactored to use a manifest-driven approach instead of hardcoded table arrays. This makes the service more flexible, maintainable, and configurable without requiring code changes.

## Key Changes

### 1. New Models

- **`ArchiveManifest`**: Represents the parsed manifest.xml structure
- **`ManifestCatalog`**: Defines table configurations with filters and modes
- **`ManifestRelationship`**: Defines relationships between tables
- **`ManifestParameter`**: Defines runtime parameters

### 2. New Services

- **`ManifestService`**: Handles loading, parsing, and querying the manifest

### 3. Refactored ArchiveService

- Removed hardcoded table arrays
- Added manifest validation and loading
- Dynamic table discovery based on manifest relationships
- Configurable query building based on manifest filters

## Manifest Structure

The `manifest.xml` file defines:

### Catalogs
```xml
<Catalog name="Materials" table="Materials" pk="LinkID" mode="changes_only">
  <Filter where="Materials.LinkIDProject = @ProjectID" />
</Catalog>
```

- **name**: Logical name for the catalog
- **table**: Actual database table name
- **pk**: Primary key column
- **mode**:
  - `all_rows`: Reference tables (synced completely)
  - `changes_only`: Project-specific tables (filtered by project)

### Relationships
```xml
<Relationship from="Locations" to="Projects" type="many-to-1">
  <ViaLink lhs="Locations.LinkIDProject" rhs="Projects.LinkID" />
</Relationship>
```

Defines how tables are related to the root entity (Projects).

### Parameters
```xml
<Parameter name="ProjectID" value="@ProjectID" />
<Parameter name="LibraryID" value="@LibraryID" />
```

Runtime parameters that can be used in filters.

## Benefits

1. **Flexibility**: Add/remove tables without code changes
2. **Maintainability**: Table relationships defined in configuration
3. **Reusability**: Same service can handle different database schemas
4. **Validation**: Manifest validation ensures configuration correctness
5. **Debugging**: Better visibility into what tables are being processed

## Usage

### Basic Usage
```csharp
var archiveService = new ArchiveService(config, loggingService);
// Service automatically loads and validates manifest.xml

// Archive a project
var progress = await archiveService.ArchiveProjectAsync(projectId);
```

### Manifest Management
```csharp
// Reload manifest (useful for configuration changes)
archiveService.ReloadManifest();

// Get manifest information
var info = archiveService.GetManifestInfo();
var tables = archiveService.GetManifestTables();
```

### Custom Manifest Path
```csharp
var manifestService = new ManifestService("custom-manifest.xml");
var archiveService = new ArchiveService(config, loggingService);
```

## Migration from Hardcoded Approach

The refactored service maintains backward compatibility:

1. **Default Behavior**: If no manifest relationships are defined, falls back to `LinkIDProject` linking
2. **Gradual Migration**: Can start with basic manifest and add relationships over time
3. **Validation**: Service validates manifest and provides clear error messages

## Configuration Examples

### Simple Project Archive
```xml
<Catalog name="Products" table="Products" pk="LinkID" mode="changes_only">
  <Filter where="Products.LinkIDProject = @ProjectID" />
</Catalog>
```

### Library Template Archive
```xml
<Catalog name="Template_Products" table="Products" pk="LinkID" mode="changes_only">
  <Filter where="Products.LinkIDLibrary = @LibraryID" />
  <Filter where="Products.Type = 1" />
</Catalog>
```

### Reference Table
```xml
<Catalog name="Employees" table="Employees" pk="LinkID" mode="all_rows" />
```

## Error Handling

The service provides comprehensive error handling:

- **Manifest Loading**: Clear error messages for missing or invalid manifest files
- **Validation**: Checks for required elements and provides warnings for missing relationships
- **Fallback**: Graceful fallback to default behavior when manifest configuration is incomplete

## Performance Considerations

- Manifest is loaded once during service initialization
- Table collections are computed on-demand using properties
- No performance impact on archive operations
- Minimal memory overhead for manifest structures

## Future Enhancements

1. **Dynamic Manifest Reloading**: Hot-reload manifest changes
2. **Conditional Processing**: Complex filter logic based on manifest rules
3. **Audit Logging**: Track which manifest rules were applied
4. **Template Support**: Reusable manifest templates for common scenarios
5. **Validation Rules**: Custom validation rules for manifest content
