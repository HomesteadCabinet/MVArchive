# Manifest.xml Management Guide

## Overview

The `manifest.xml` file is the configuration file that drives the ArchiveService behavior. It defines which tables to archive, how they relate to each other, and what filtering rules to apply. This file replaces the hardcoded table arrays in the original ArchiveService.

## File Location

The manifest file should be placed in the root directory of your project as `manifest.xml`. The ArchiveService will automatically load and validate this file on startup.

## Basic Structure

```xml
<ProjectArchiveManifest manifest_version="1.0.0" domain="ProjectArchive">
  <!-- Parameters section -->
  <Parameters>
    <!-- Define runtime parameters here -->
  </Parameters>

  <!-- Catalogs section -->
  <Catalogs>
    <!-- Define table configurations here -->
  </Catalogs>

  <!-- Root entity and relationships -->
  <Root entity="Projects" key="LinkID">
    <!-- Define table relationships here -->
  </Root>

  <!-- Data integrity rules -->
  <Integrity enforce_fk_check="false" unexpected_row_policy="warn" orphan_link_policy="skip" />
</ProjectArchiveManifest>
```

## Parameters Section

The `<Parameters>` section defines runtime parameters that can be used in table filters.

### Available Parameters

```xml
<Parameters>
  <Parameter name="ProjectID" value="@ProjectID" />
  <Parameter name="LibraryID" value="@LibraryID" />
</Parameters>
```

- **ProjectID**: The unique identifier of the project being archived
- **LibraryID**: The unique identifier of the library being processed

### Using Parameters in Filters

Parameters are referenced in table filters using the `@` prefix:

```xml
<Filter where="Materials.LinkIDProject = @ProjectID" />
<Filter where="Categories.LinkIDLibrary = @LibraryID" />
```

## Catalogs Section

The `<Catalogs>` section defines how each table should be processed during archiving.

### Catalog Attributes

Each `<Catalog>` element has the following attributes:

- **name**: Logical name for the catalog (used for identification)
- **table**: Actual database table name
- **pk**: Primary key column name
- **mode**: Processing mode (see modes below)
- **binary** (optional): When `true`, marks the table as containing binary/blob data. Used by ArchiveService to ensure proper parameter typing and any file-table handling. Defaults to `false`.
- **singleton** (optional): When `true`, marks the table as a global/singleton (copied once, not linked to a project). ArchiveService ensures these are present in the destination prior to project archiving. Defaults to `false`.

### Processing Modes

#### `all_rows` - Reference Tables
Tables with this mode are synced completely (all rows copied).

```xml
<Catalog name="Employees" table="Employees" pk="LinkID" mode="all_rows" />
<Catalog name="EmployeeSecure" table="EmployeeSecure" pk="LinkID" mode="all_rows" />
```

**Use for**: System tables, lookup tables, configuration tables that don't change between projects.

#### `changes_only` - Project-Specific Tables
Tables with this mode are filtered by project or library.

```xml
<Catalog name="Materials" table="Materials" pk="LinkID" mode="changes_only">
  <Filter where="Materials.LinkIDProject = @ProjectID" />
</Catalog>
```

**Use for**: Tables that contain project-specific data.

### Filter Rules

Filters define which rows to include from each table.

#### Basic Filter
```xml
<Filter where="Materials.LinkIDProject = @ProjectID" />
```

#### Multiple Filters (AND logic)
```xml
<Catalog name="Template_Products" table="Products" pk="LinkID" mode="changes_only">
  <Filter where="Products.LinkIDLibrary IN (@LibraryID)" />
  <Filter where="Products.Type = 1" />
</Catalog>
```

Notes on list parameters:
- The service supports expanding `IN (@LibraryID)` into an `IN ('id1','id2',...)` list when multiple library IDs are detected.
- If you provide a single value, it behaves as `IN ('value')`.

#### Complex Filter Examples
```xml
<!-- Date-based filtering -->
<Filter where="Activities.DateCreated >= '2023-01-01'" />

<!-- Status-based filtering -->
<Filter where="Projects.Status IN ('Active', 'Pending')" />

<!-- Null checks -->
<Filter where="Categories.LinkIDParent IS NULL" />

<!-- Pattern matching -->
<Filter where="Products.Name LIKE '%Template%'" />
```

## Root Section

The `<Root>` section defines the main entity and how other tables relate to it.

### Root Configuration
```xml
<Root entity="Projects" key="LinkID">
  <Selector column="LinkID" value="@ProjectID" />
  <!-- Relationships defined here -->
</Root>
```

- **entity**: The main table being archived (usually "Projects")
- **key**: The primary key column of the root entity

### Selector
```xml
<Selector column="LinkID" value="@ProjectID" />
```
Defines how to select the specific project being archived.

## Relationships Section

The `<Relationships>` section defines how tables are related to the root entity.

### Basic Relationship
```xml
<Relationship from="Locations" to="Projects" type="many-to-1">
  <ViaLink lhs="Locations.LinkIDProject" rhs="Projects.LinkID" />
</Relationship>
```

- **from**: The table that references the root entity
- **to**: The root entity table
- **type**: Relationship type (many-to-1, one-to-many, etc.)
- **ViaLink**: Defines the linking columns

### Nested Relationships
```xml
<Relationship from="Products" to="Projects" type="many-to-1">
  <ViaLink lhs="Products.LinkIDProject" rhs="Projects.LinkID" />

  <!-- Nested relationship -->
  <Relationship from="Subassemblies" to="Products" type="many-to-1">
    <ViaLink lhs="Subassemblies.LinkIDParentProduct" rhs="Products.LinkID" />
  </Relationship>
</Relationship>
```

The parser now supports recursively nested relationships at any depth. This means you can describe full hierarchies (Products → Subassemblies → Subassemblies ...) and ArchiveService will discover all project-related tables by walking the relationship tree.

## Integrity Section

The `<Integrity>` section defines data integrity rules.

```xml
<Integrity enforce_fk_check="false" unexpected_row_policy="warn" orphan_link_policy="skip" />
```

- **enforce_fk_check**: Whether to enforce foreign key constraints
- **unexpected_row_policy**: How to handle unexpected rows (warn, error, skip)
- **orphan_link_policy**: How to handle orphaned links (warn, error, skip)

## Complete Example

Here's a complete manifest example:

```xml
<ProjectArchiveManifest manifest_version="1.0.0" domain="ProjectArchive">
  <Parameters>
    <Parameter name="ProjectID" value="@ProjectID" />
    <Parameter name="LibraryID" value="@LibraryID" />
  </Parameters>

  <Catalogs>
    <!-- Reference tables (synced completely) -->
    <Catalog name="Employees" table="Employees" pk="LinkID" mode="all_rows" />
    <Catalog name="Libraries" table="Libraries" pk="LinkID" mode="all_rows" />

    <!-- Project-specific tables -->
    <Catalog name="Materials" table="Materials" pk="LinkID" mode="changes_only">
      <Filter where="Materials.LinkIDProject = @ProjectID" />
    </Catalog>

    <!-- Template tables (uses IN(@LibraryID) expansion) -->
    <Catalog name="Template_Products" table="Products" pk="LinkID" mode="changes_only">
      <Filter where="Products.LinkIDLibrary IN (@LibraryID)" />
      <Filter where="Products.Type = 1" />
    </Catalog>

    <!-- Binary and Singleton examples -->
    <Catalog name="CutPartsFiles" table="CutPartsFiles" pk="LinkID" mode="changes_only" binary="true">
      <Filter where="CutPartsFiles.LinkIDProject = @ProjectID" />
    </Catalog>

    <Catalog name="Factory" table="Factory" pk="LinkID" mode="all_rows" singleton="true" />
  </Catalogs>

  <Root entity="Projects" key="LinkID">
    <Selector column="LinkID" value="@ProjectID" />

    <Relationships>
      <Relationship from="Locations" to="Projects" type="many-to-1">
        <ViaLink lhs="Locations.LinkIDProject" rhs="Projects.LinkID" />
      </Relationship>

      <Relationship from="Products" to="Projects" type="many-to-1">
        <ViaLink lhs="Products.LinkIDProject" rhs="Projects.LinkID" />

        <Relationship from="Subassemblies" to="Products" type="many-to-1">
          <ViaLink lhs="Subassemblies.LinkIDParentProduct" rhs="Products.LinkID" />
        </Relationship>
      </Relationship>
    </Relationships>
  </Root>

  <Integrity enforce_fk_check="false" unexpected_row_policy="warn" orphan_link_policy="skip" />
</ProjectArchiveManifest>
```

## Managing the Manifest

### Adding New Tables

1. **Identify the table type**:
   - Reference table (mode="all_rows")
   - Project-specific table (mode="changes_only")

2. **Add catalog entry**:
   ```xml
   <Catalog name="NewTable" table="NewTable" pk="LinkID" mode="changes_only">
     <Filter where="NewTable.LinkIDProject = @ProjectID" />
   </Catalog>
   ```

   Optional flags:
   - Mark as binary if it stores blobs/files: `binary="true"`
   - Mark as singleton if it is global (copied once): `singleton="true"`

3. **Define relationships** (if applicable):
   ```xml
   <Relationship from="NewTable" to="Projects" type="many-to-1">
     <ViaLink lhs="NewTable.LinkIDProject" rhs="Projects.LinkID" />
   </Relationship>
   ```

### Modifying Existing Tables

1. **Change processing mode**:
   ```xml
   <!-- Change from reference to project-specific -->
   <Catalog name="Materials" table="Materials" pk="LinkID" mode="changes_only">
     <Filter where="Materials.LinkIDProject = @ProjectID" />
   </Catalog>
   ```

2. **Update filters**:
   ```xml
   <!-- Add additional filtering -->
   <Catalog name="Products" table="Products" pk="LinkID" mode="changes_only">
     <Filter where="Products.LinkIDProject = @ProjectID" />
     <Filter where="Products.Status = 'Active'" />
   </Catalog>
   ```

   To support multiple library IDs, prefer `IN (@LibraryID)` over equality:
   ```xml
   <Filter where="Products.LinkIDLibrary IN (@LibraryID)" />
   ```

### Removing Tables

Simply delete the corresponding `<Catalog>` and `<Relationship>` entries.

## Best Practices

### 1. Naming Conventions
- Use descriptive catalog names
- Prefix template catalogs with "Template_"
- Use consistent naming patterns

### 2. Filter Optimization
- Keep filters simple and efficient
- Use indexed columns in filters
- Avoid complex OR conditions
- Prefer `IN (@LibraryID)` when you may pass multiple library IDs; the service will expand it.

### 3. Relationship Design
- Define clear, logical relationships
- Use appropriate relationship types
- Document complex nested relationships
- Take advantage of recursive relationships to fully model hierarchies; ArchiveService will traverse them.

### 4. Testing
- Test manifest changes in development first
- Validate XML syntax before deployment
- Monitor archive performance after changes

## Troubleshooting

### Common Issues

1. **Table not found**: Check table name spelling and case
2. **Filter syntax error**: Validate SQL syntax in filters
3. **Missing relationships**: Ensure all referenced tables have relationships defined
4. **Performance issues**: Review filter complexity and add indexes if needed

### Validation

The ArchiveService automatically validates the manifest on startup:

- Checks for required elements
- Validates XML syntax
- Warns about missing relationships
- Provides clear error messages
- If no `singleton="true"` catalogs are defined, the service preserves backward compatibility by ensuring the `Factory` table exists.

### Debugging

Use the ArchiveService methods to inspect manifest state:

```csharp
var info = archiveService.GetManifestInfo();
var tables = archiveService.GetManifestTables();
var manifest = archiveService.GetManifest();
```

## Migration from Hardcoded Tables

If you're migrating from the old hardcoded approach:

1. **Start with basic manifest**: Define catalogs for existing tables
2. **Add relationships gradually**: Define table relationships one by one
3. **Test incrementally**: Verify each change works before proceeding
4. **Remove hardcoded arrays**: Once manifest is complete, remove old code

## Advanced Features

### Dynamic Filtering
Filters can use complex SQL expressions:

```xml
<Filter where="Products.DateModified > DATEADD(day, -30, GETDATE())" />
<Filter where="Materials.Quantity > 0 AND Materials.Status = 'Active'" />
```

### Conditional Processing
Use multiple filters for complex logic:

```xml
<Catalog name="SmartFilter" table="Data" pk="ID" mode="changes_only">
  <Filter where="Data.ProjectID = @ProjectID" />
  <Filter where="Data.Type IN ('A', 'B', 'C')" />
  <Filter where="Data.Status != 'Deleted'" />
</Catalog>
```

### Template Patterns
Create reusable catalog patterns:

```xml
<!-- Standard project table pattern -->
<Catalog name="Project_Table" table="TableName" pk="LinkID" mode="changes_only">
  <Filter where="TableName.LinkIDProject = @ProjectID" />
</Catalog>
```

## Support and Maintenance

- **Regular review**: Periodically review and update the manifest
- **Version control**: Keep manifest changes in version control
- **Documentation**: Document complex configurations and business rules
- **Backup**: Keep backup copies of working manifest configurations

