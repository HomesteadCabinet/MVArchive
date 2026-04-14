/*
 * MVArchive Catalog/Reference Table Verification Script
 *
 * Purpose: Validate that catalog and reference tables are correctly synced
 *          from the source database to the archive database.
 *
 * Usage:
 *   1. Update the @SourceDB and @ArchiveDB variables with your database names
 *   2. Execute this script against either database server
 *   3. Review the output for any discrepancies
 *
 * Documentation: See docs/VERIFICATION_GUIDE.md for detailed instructions
 */

-- ============================================================================
-- CONFIGURATION
-- ============================================================================
DECLARE @SourceDB NVARCHAR(128) = 'MicrovellumData'  -- Source database name
DECLARE @ArchiveDB NVARCHAR(128) = 'MicrovellumArchive'  -- Archive database name
DECLARE @SQL NVARCHAR(MAX)

-- ============================================================================
-- CATALOG TABLES VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'CATALOG/REFERENCE TABLES VERIFICATION'
PRINT '============================================================================'
PRINT ''
PRINT 'These tables are synced independently of specific projects.'
PRINT 'Counts may differ based on sync mode (changes_only vs all_rows).'
PRINT ''

PRINT '1. ToolFiles (changes_only)'
PRINT '----------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ToolFiles

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ToolFiles'

EXEC sp_executesql @SQL
PRINT ''

PRINT '2. ProcessingStations (changes_only)'
PRINT '-------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ProcessingStations

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ProcessingStations'

EXEC sp_executesql @SQL
PRINT ''

PRINT '3. Sheets (changes_only)'
PRINT '------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Sheets

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Sheets'

EXEC sp_executesql @SQL
PRINT ''

PRINT '4. Materials (Catalog - changes_only)'
PRINT '--------------------------------------'
PRINT 'NOTE: Materials can be both catalog (no LinkIDProject) and project-scoped'
PRINT ''
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS TotalRecords,
    SUM(CASE WHEN LinkIDProject IS NULL THEN 1 ELSE 0 END) AS CatalogMaterials,
    SUM(CASE WHEN LinkIDProject IS NOT NULL THEN 1 ELSE 0 END) AS ProjectMaterials
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Materials

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS TotalRecords,
    SUM(CASE WHEN LinkIDProject IS NULL THEN 1 ELSE 0 END) AS CatalogMaterials,
    SUM(CASE WHEN LinkIDProject IS NOT NULL THEN 1 ELSE 0 END) AS ProjectMaterials
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Materials'

EXEC sp_executesql @SQL
PRINT ''

PRINT '5. Employees (changes_only)'
PRINT '---------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Employees

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Employees'

EXEC sp_executesql @SQL
PRINT ''

PRINT '6. EmployeeSecure (changes_only)'
PRINT '---------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.EmployeeSecure

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.EmployeeSecure'

EXEC sp_executesql @SQL
PRINT ''

PRINT '7. EmployeeLogin (changes_only)'
PRINT '--------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.EmployeeLogin

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.EmployeeLogin'

EXEC sp_executesql @SQL
PRINT ''

PRINT '8. Departments (changes_only)'
PRINT '------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Departments

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Departments'

EXEC sp_executesql @SQL
PRINT ''

PRINT '9. Vendors (changes_only)'
PRINT '-------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Vendors

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Vendors'

EXEC sp_executesql @SQL
PRINT ''

PRINT '10. ActivityStations (changes_only)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ActivityStations

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ActivityStations'

EXEC sp_executesql @SQL
PRINT ''

PRINT '11. Shifts (changes_only)'
PRINT '-------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Shifts

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount,
    MAX(DateModified) AS LastModified
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Shifts'

EXEC sp_executesql @SQL
PRINT ''

PRINT '12. ShiftsActivityStations (changes_only)'
PRINT '------------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ShiftsActivityStations

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ShiftsActivityStations'

EXEC sp_executesql @SQL
PRINT ''

PRINT '13. MicrovellumSystem (all_rows)'
PRINT '---------------------------------'
PRINT 'Singleton table - should always have same record count'
PRINT ''
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.MicrovellumSystem

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.MicrovellumSystem'

EXEC sp_executesql @SQL
PRINT ''

PRINT '14. Factory (all_rows)'
PRINT '----------------------'
PRINT 'Global singleton table - should always have same record count'
PRINT ''
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Factory

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS RecordCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Factory'

EXEC sp_executesql @SQL
PRINT ''

-- ============================================================================
-- LIBRARY TEMPLATE VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'LIBRARY TEMPLATE TABLES VERIFICATION'
PRINT '============================================================================'
PRINT ''
PRINT 'These tables contain both templates (Type=1 or Type=6) and project instances.'
PRINT 'Template data is synced separately by library reference.'
PRINT ''

-- Get distinct library IDs from archived projects to verify template sync
PRINT '15. Libraries Referenced in Archive'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products
WHERE LinkIDLibrary IS NOT NULL

UNION ALL

SELECT
    ''Libraries Table'',
    COUNT(*) AS LibraryRecords
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Libraries'

EXEC sp_executesql @SQL
PRINT ''

PRINT '16. Categories (Template Type=1)'
PRINT '---------------------------------'
SET @SQL = '
-- Library-scoped template categories in source
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateCategoryCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Categories
WHERE LinkIDLibrary IS NOT NULL

UNION ALL

-- Library-scoped template categories in archive
SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateCategoryCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Categories
WHERE LinkIDLibrary IS NOT NULL'

EXEC sp_executesql @SQL
PRINT ''

PRINT '17. Products (Template Type=1)'
PRINT '-------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateProductCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products
WHERE Type = 1

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateProductCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products
WHERE Type = 1'

EXEC sp_executesql @SQL
PRINT ''

PRINT '18. Subassemblies (Template Type=1)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateSubassemblyCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Subassemblies
WHERE Type = 1

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateSubassemblyCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Subassemblies
WHERE Type = 1'

EXEC sp_executesql @SQL
PRINT ''

PRINT '19. SpecificationGroups (Template Type=6)'
PRINT '------------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateSpecGroupCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@SourceDB) + '.dbo.SpecificationGroups
WHERE Type = 6

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateSpecGroupCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.SpecificationGroups
WHERE Type = 6'

EXEC sp_executesql @SQL
PRINT ''

PRINT '20. CutPartsFiles (Template Type=6)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.CutPartsFiles
WHERE Type = 6

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.CutPartsFiles
WHERE Type = 6'

EXEC sp_executesql @SQL
PRINT ''

PRINT '21. EdgebandFiles (Template Type=6)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.EdgebandFiles
WHERE Type = 6

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.EdgebandFiles
WHERE Type = 6'

EXEC sp_executesql @SQL
PRINT ''

PRINT '22. HardwareFiles (Template Type=6)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.HardwareFiles
WHERE Type = 6

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.HardwareFiles
WHERE Type = 6'

EXEC sp_executesql @SQL
PRINT ''

PRINT '23. DoorWizardFiles (Template Type=6)'
PRINT '--------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.DoorWizardFiles
WHERE Type = 6

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.DoorWizardFiles
WHERE Type = 6'

EXEC sp_executesql @SQL
PRINT ''

PRINT '24. GlobalFiles (Template Type=6)'
PRINT '----------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.GlobalFiles
WHERE Type = 6

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.GlobalFiles
WHERE Type = 6'

EXEC sp_executesql @SQL
PRINT ''

PRINT '25. ProjectWizardFiles (Template Type=6)'
PRINT '-----------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ProjectWizardFiles
WHERE Type = 6

UNION ALL

SELECT
    ''' + @ArchiveDB + ' Templates'' AS [Source],
    COUNT(*) AS TemplateFileCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ProjectWizardFiles
WHERE Type = 6'

EXEC sp_executesql @SQL
PRINT ''

-- ============================================================================
-- SUMMARY
-- ============================================================================
PRINT '============================================================================'
PRINT 'CATALOG VERIFICATION SUMMARY'
PRINT '============================================================================'
PRINT ''
PRINT 'Verification complete!'
PRINT ''
PRINT 'NOTES:'
PRINT '  - Changes_only mode: Archive should contain subset of source records'
PRINT '  - All_rows mode: Archive should match source record count exactly'
PRINT '  - Template data: Archive should contain templates for all referenced libraries'
PRINT ''
