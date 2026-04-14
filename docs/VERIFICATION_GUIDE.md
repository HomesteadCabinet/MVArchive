# MVArchive Data Verification Guide

This README explains how to use the verification scripts to ensure that Microvellum job data is correctly archived.

## Overview

The verification process consists of two main scripts:

1. **[verify_archive_completeness.sql](file:///C:/Users/JuicyJerry/Dev.local/MVArchive/verify_archive_completeness.sql)** - Validates project-scoped data
2. **[verify_catalog_tables.sql](file:///C:/Users/JuicyJerry/Dev.local/MVArchive/verify_catalog_tables.sql)** - Validates catalog/reference tables

## Prerequisites

- SQL Server Management Studio (SSMS) or Azure Data Studio
- Access to both source and archive databases
- A completed archive job to verify

## Step-by-Step Verification Process

### Step 1: Identify Test Project

Choose a project with comprehensive data coverage:
- Multiple products from different libraries
- Nested subassemblies (3+ levels deep)
- Work orders with activities
- Purchase orders with received materials
- Binary data (drawings, specification files)
- Shipping data if applicable

### Step 2: Configure the Scripts

#### For verify_archive_completeness.sql

Open the script and update these variables (lines 12-14):

```sql
DECLARE @ProjectID NVARCHAR(255) = 'YOUR_PROJECT_LINKID_HERE'  -- Replace with actual LinkID
DECLARE @SourceDB NVARCHAR(128) = 'MicrovellumData'            -- Source database name
DECLARE @ArchiveDB NVARCHAR(128) = 'MicrovellumArchive'        -- Archive database name
```

#### For verify_catalog_tables.sql

Open the script and update these variables (lines 12-13):

```sql
DECLARE @SourceDB NVARCHAR(128) = 'MicrovellumData'      -- Source database name
DECLARE @ArchiveDB NVARCHAR(128) = 'MicrovellumArchive'  -- Archive database name
```

### Step 3: Run the Verification Scripts

#### Option A: Run in SSMS

1. Open SQL Server Management Studio
2. Connect to either the source or archive SQL Server (both databases must be accessible)
3. Open `verify_archive_completeness.sql`
4. Ensure configuration variables are correct
5. Execute the script (F5 or Execute button)
6. Review the results in the Messages pane

Repeat for `verify_catalog_tables.sql`.

#### Option B: Run via sqlcmd

From PowerShell or Command Prompt:

```powershell
# Project data verification
sqlcmd -S <ServerName> -d <DatabaseName> -i verify_archive_completeness.sql -o project_verification_results.txt

# Catalog data verification
sqlcmd -S <ServerName> -d <DatabaseName> -i verify_catalog_tables.sql -o catalog_verification_results.txt
```

### Step 4: Interpret Results

Each section displays side-by-side counts from source and archive databases.

#### Expected Results

**Project-Scoped Tables** (verify_archive_completeness.sql):
- Record counts should **MATCH EXACTLY** between source and archive for the specified project
- Example:
  ```
  Database              ProductCount   UniqueLibraries   TemplateProducts   InstanceProducts
  MicrovellumData       42             3                 0                  42
  MicrovellumArchive    42             3                 0                  42
  ```

**Catalog Tables** (verify_catalog_tables.sql):
- **changes_only mode**: Archive may have FEWER records than source (only changed records)
- **all_rows mode**: Archive should MATCH source exactly
- Example:
  ```
  Database              RecordCount    LastModified
  MicrovellumData       156            2026-01-15 10:23:45
  MicrovellumArchive    89             2026-01-10 14:12:30
  ```
  ☝️ This is NORMAL for changes_only mode

**Template Tables**:
- Archive should contain ALL templates from libraries referenced by archived products
- Template counts depend on which libraries are used by your projects

### Step 5: Identify Discrepancies

If counts DON'T match for project-scoped data:

1. **Missing Records**: Archive count < Source count
   - Indicates incomplete archiving
   - Check manifest.xml relationship definitions
   - Review ArchiveService logs for errors

2. **Extra Records**: Archive count > Source count
   - Usually indicates data from multiple archived projects
   - This is NORMAL if you've archived multiple projects to the same archive DB

3. **Zero Records in Both**:
   - Project may not use that entity type (e.g., no bundles, no pallets)
   - This is NORMAL

### Step 6: Deep Dive Investigation

If you find discrepancies, use these queries to identify specific missing records:

```sql
-- Example: Find products in source but not in archive
SELECT p.LinkID, p.Name, p.JobNumber
FROM MicrovellumData.dbo.Products p
WHERE p.LinkIDProject = 'YOUR_PROJECT_LINKID'
  AND p.LinkID NOT IN (
    SELECT LinkID
    FROM MicrovellumArchive.dbo.Products
  )
```

Repeat this pattern for any table with mismatched counts.

## Verification Checklist

Use this checklist when verifying an archived project:

- [ ] Identified test project with comprehensive data
- [ ] Updated `@ProjectID` in verify_archive_completeness.sql
- [ ] Updated database names in both scripts
- [ ] Executed verify_archive_completeness.sql
- [ ] Verified all project-scoped counts match
- [ ] Executed verify_catalog_tables.sql
- [ ] Verified catalog sync is appropriate for sync mode
- [ ] Verified library templates are present
- [ ] Documented any discrepancies
- [ ] Investigated root cause of discrepancies (if any)
- [ ] Updated manifest.xml if relationships are missing
- [ ] Re-ran archive if necessary
- [ ] Re-verified after fixes

## Common Issues and Solutions

### Issue: Subassemblies count mismatch

**Possible Causes:**
- Recursive CTE not properly handling nested relationships
- Missing `LinkIDParentSubassembly` relationships

**Solution:**
1. Review the subassembly query in verify_archive_completeness.sql
2. Check manifest.xml for self-referential Subassemblies relationship
3. Verify MAXRECURSION setting (currently 100)

### Issue: Workorders not archived

**Possible Causes:**
- Workorders have NO `LinkIDProject` field - they're linked via `Products.LinkIDWorkOrder`
- Products not properly linked to workorders

**Solution:**
1. Verify `Products.LinkIDWorkOrder` values are populated
2. Check manifest.xml relationship from Products to Workorders
3. Review ArchiveService.cs `BuildProjectRelatedQuery` for Workorders

### Issue: Binary data (WorkBook) is NULL

**Possible Causes:**
- Binary data not synced due to configuration
- SyncCatalogTablesAsync may skip binary fields

**Solution:**
1. Check `binary="true"` attribute in manifest.xml for file tables
2. Review ArchiveService.cs binary data handling logic
3. Verify file sizes aren't exceeding SQL Server limits

### Issue: Catalog tables have 0 records in archive

**Possible Causes:**
- SyncReferenceTablesAsync not called
- Catalog table names don't match manifest.xml

**Solution:**
1. Check ArchiveConfig to ensure catalogs are enabled
2. Verify catalog table names in manifest.xml match database schema exactly
3. Review logs from SyncReferenceTablesAsync execution

## Automation

For automated regression testing, you can create a PowerShell script:

```powershell
# Example: automated-verification.ps1
param(
    [string]$ProjectID,
    [string]$SourceDB = "MicrovellumData",
    [string]$ArchiveDB = "MicrovellumArchive",
    [string]$ServerName = "localhost"
)

# Update scripts with parameters
(Get-Content verify_archive_completeness.sql) `
    -replace "DECLARE @ProjectID NVARCHAR\(255\) = '.*'", "DECLARE @ProjectID NVARCHAR(255) = '$ProjectID'" `
    -replace "DECLARE @SourceDB NVARCHAR\(128\) = '.*'", "DECLARE @SourceDB NVARCHAR(128) = '$SourceDB'" `
    -replace "DECLARE @ArchiveDB NVARCHAR\(128\) = '.*'", "DECLARE @ArchiveDB NVARCHAR(128) = '$ArchiveDB'" `
    | Out-File -FilePath temp_verify_project.sql

# Execute
sqlcmd -S $ServerName -d $SourceDB -i temp_verify_project.sql -o "verification_results_$ProjectID.txt"

# Parse results and check for mismatches
# ... (add logic to parse output and fail if counts don't match)
```

## Additional Resources

- **[implementation_plan.md](file:///C:/Users/JuicyJerry/.gemini/antigravity/brain/b67cbfb5-7adc-4cfb-a37c-1bd908758313/implementation_plan.md)** - Full documentation of archiving architecture
- **[manifest.xml](file:///C:/Users/JuicyJerry/Dev.local/MVArchive/manifest.xml)** - Relationship definitions
- **[ArchiveService.cs](file:///C:/Users/JuicyJerry/Dev.local/MVArchive/Services/ArchiveService.cs)** - Core archiving logic

## Support

If you encounter issues not covered in this guide:

1. Review application logs in ArchiveService for error messages
2. Check manifest.xml for syntax errors or missing relationships
3. Verify database connectivity and permissions
4. Consult the implementation plan for detailed table relationship mappings
