/*
 * MVArchive Data Completeness Verification Script
 *
 * Purpose: Validate that all required data for a Microvellum job has been
 *          correctly archived from the source database to the archive database.
 *
 * Usage:
 *   1. Update the @ProjectID variable with the LinkID of the project to verify
 *   2. Update the @SourceDB and @ArchiveDB variables with your database names
 *   3. Execute this script against either database server
 *   4. Review the output for any discrepancies
 *
 * Documentation: See docs/VERIFICATION_GUIDE.md for detailed instructions
 */

-- ============================================================================
-- CONFIGURATION
-- ============================================================================
DECLARE @ProjectID NVARCHAR(255) = 'YOUR_PROJECT_LINKID_HERE'
DECLARE @SourceDB NVARCHAR(128) = 'MicrovellumData'  -- Source database name
DECLARE @ArchiveDB NVARCHAR(128) = 'MicrovellumArchive'  -- Archive database name

-- ============================================================================
-- SECTION 1: PROJECT ROOT VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 1: PROJECT ROOT VALIDATION'
PRINT '============================================================================'
PRINT ''

-- Verify project exists in both databases
DECLARE @SQL NVARCHAR(MAX)

PRINT '1.1 - Project Record Count'
PRINT '----------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ProjectCount,
    MAX(Name) AS ProjectName,
    MAX(JobNumber) AS JobNumber
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Projects
WHERE LinkID = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ProjectCount,
    MAX(Name) AS ProjectName,
    MAX(JobNumber) AS JobNumber
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Projects
WHERE LinkID = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 2: CORE PROJECT DATA VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 2: CORE PROJECT DATA VALIDATION'
PRINT '============================================================================'
PRINT ''

PRINT '2.1 - Categories (Self-Referential)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS CategoryCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Categories
WHERE LinkIDParent = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS CategoryCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Categories
WHERE LinkIDParent = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '2.2 - Locations'
PRINT '----------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS LocationCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Locations
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS LocationCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Locations
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '2.3 - Products'
PRINT '--------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ProductCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN Type = 1 THEN 1 ELSE 0 END) AS TemplateProducts,
    SUM(CASE WHEN Type != 1 OR Type IS NULL THEN 1 ELSE 0 END) AS InstanceProducts
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ProductCount,
    COUNT(DISTINCT LinkIDLibrary) AS UniqueLibraries,
    SUM(CASE WHEN Type = 1 THEN 1 ELSE 0 END) AS TemplateProducts,
    SUM(CASE WHEN Type != 1 OR Type IS NULL THEN 1 ELSE 0 END) AS InstanceProducts
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '2.4 - Subassemblies (Recursive CTE)'
PRINT '------------------------------------'
SET @SQL = '
;WITH SubassemblySet AS (
    -- Direct project subassemblies
    SELECT s.ID, s.LinkID, 1 AS Level
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Subassemblies s
    WHERE s.LinkIDProject = @ProjectID

    UNION ALL

    -- Recursively nested subassemblies
    SELECT c.ID, c.LinkID, Level + 1
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Subassemblies c
    INNER JOIN SubassemblySet ps ON c.LinkIDParentSubassembly = ps.LinkID
)
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS SubassemblyCount,
    MAX(Level) AS MaxNestingLevel
FROM SubassemblySet
OPTION (MAXRECURSION 100)

UNION ALL

;WITH SubassemblySet AS (
    SELECT s.ID, s.LinkID, 1 AS Level
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Subassemblies s
    WHERE s.LinkIDProject = @ProjectID

    UNION ALL

    SELECT c.ID, c.LinkID, Level + 1
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Subassemblies c
    INNER JOIN SubassemblySet ps ON c.LinkIDParentSubassembly = ps.LinkID
)
SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS SubassemblyCount,
    MAX(Level) AS MaxNestingLevel
FROM SubassemblySet
OPTION (MAXRECURSION 100)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '2.5 - Parts'
PRINT '------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS PartCount,
    COUNT(DISTINCT LinkIDMaterial) AS UniqueMaterials,
    SUM(CASE WHEN LinkIDWorkOrder IS NOT NULL THEN 1 ELSE 0 END) AS PartsWithWorkOrders
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Parts
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS PartCount,
    COUNT(DISTINCT LinkIDMaterial) AS UniqueMaterials,
    SUM(CASE WHEN LinkIDWorkOrder IS NOT NULL THEN 1 ELSE 0 END) AS PartsWithWorkOrders
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Parts
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 3: WORK ORDER & PRODUCTION DATA VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 3: WORK ORDER & PRODUCTION DATA VALIDATION'
PRINT '============================================================================'
PRINT ''

PRINT '3.1 - Workorders (via Products)'
PRINT '--------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(DISTINCT w.LinkID) AS WorkorderCount,
    COUNT(DISTINCT p.LinkID) AS ProductsWithWorkorders
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
WHERE p.LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(DISTINCT w.LinkID) AS WorkorderCount,
    COUNT(DISTINCT p.LinkID) AS ProductsWithWorkorders
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
WHERE p.LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '3.2 - WorkOrderBatches'
PRINT '----------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS BatchCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.WorkOrderBatches wb
WHERE wb.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS BatchCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.WorkOrderBatches wb
WHERE wb.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '3.3 - WorkOrderActivities'
PRINT '-------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ActivityCount,
    SUM(CASE WHEN LinkIDProject IS NOT NULL THEN 1 ELSE 0 END) AS ViaProject,
    SUM(CASE WHEN LinkIDPart IS NOT NULL THEN 1 ELSE 0 END) AS ViaPart,
    SUM(CASE WHEN LinkIDSubassembly IS NOT NULL THEN 1 ELSE 0 END) AS ViaSubassembly,
    SUM(CASE WHEN LinkIDWorkOrder IS NOT NULL THEN 1 ELSE 0 END) AS ViaWorkOrder
FROM ' + QUOTENAME(@SourceDB) + '.dbo.WorkOrderActivities
WHERE
    LinkIDProject = @ProjectID
    OR LinkIDPart IN (SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Parts WHERE LinkIDProject = @ProjectID)
    OR LinkIDSubassembly IN (SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Subassemblies WHERE LinkIDProject = @ProjectID)
    OR LinkIDWorkOrder IN (
        SELECT w.LinkID
        FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
        INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
        WHERE p.LinkIDProject = @ProjectID
    )

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ActivityCount,
    SUM(CASE WHEN LinkIDProject IS NOT NULL THEN 1 ELSE 0 END) AS ViaProject,
    SUM(CASE WHEN LinkIDPart IS NOT NULL THEN 1 ELSE 0 END) AS ViaPart,
    SUM(CASE WHEN LinkIDSubassembly IS NOT NULL THEN 1 ELSE 0 END) AS ViaSubassembly,
    SUM(CASE WHEN LinkIDWorkOrder IS NOT NULL THEN 1 ELSE 0 END) AS ViaWorkOrder
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.WorkOrderActivities
WHERE
    LinkIDProject = @ProjectID
    OR LinkIDPart IN (SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Parts WHERE LinkIDProject = @ProjectID)
    OR LinkIDSubassembly IN (SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Subassemblies WHERE LinkIDProject = @ProjectID)
    OR LinkIDWorkOrder IN (
        SELECT w.LinkID
        FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
        INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
        WHERE p.LinkIDProject = @ProjectID
    )'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '3.4 - ProjectWorkOrderActivities'
PRINT '---------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS PWOActivityCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ProjectWorkOrderActivities
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS PWOActivityCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ProjectWorkOrderActivities
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '3.5 - ScheduledWorkOrderActivities'
PRINT '-----------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ScheduledActivityCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ScheduledWorkOrderActivities swa
WHERE swa.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ScheduledActivityCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ScheduledWorkOrderActivities swa
WHERE swa.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 4: MATERIAL & INVENTORY VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 4: MATERIAL & INVENTORY VALIDATION'
PRINT '============================================================================'
PRINT ''

PRINT '4.1 - Materials (Project-Scoped)'
PRINT '---------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS MaterialCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Materials
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS MaterialCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Materials
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '4.2 - PlacedSheets'
PRINT '------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS PlacedSheetCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.PlacedSheets ps
WHERE ps.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS PlacedSheetCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.PlacedSheets ps
WHERE ps.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '4.3 - PlacedSheetsVendors'
PRINT '-------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS PlacedSheetVendorCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.PlacedSheetsVendors psv
WHERE psv.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS PlacedSheetVendorCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.PlacedSheetsVendors psv
WHERE psv.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '4.4 - MaterialCostBreaks'
PRINT '------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS CostBreakCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.MaterialCostBreaks mcb
WHERE mcb.LinkIDMaterial IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Materials WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS CostBreakCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.MaterialCostBreaks mcb
WHERE mcb.LinkIDMaterial IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Materials WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '4.5 - MaterialStorageLocations'
PRINT '-------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS StorageLocationCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.MaterialStorageLocations msl
WHERE msl.LinkIDCategory IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Categories WHERE LinkIDParent = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS StorageLocationCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.MaterialStorageLocations msl
WHERE msl.LinkIDCategory IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Categories WHERE LinkIDParent = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '4.6 - MaterialsVendors'
PRINT '----------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS MaterialVendorCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.MaterialsVendors mv
WHERE mv.LinkIDMaterial IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Materials WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS MaterialVendorCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.MaterialsVendors mv
WHERE mv.LinkIDMaterial IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Materials WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 5: PURCHASE ORDER VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 5: PURCHASE ORDER VALIDATION'
PRINT '============================================================================'
PRINT ''

PRINT '5.1 - PurchaseOrders'
PRINT '--------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS POCount,
    COUNT(DISTINCT LinkIDVendor) AS UniqueVendors
FROM ' + QUOTENAME(@SourceDB) + '.dbo.PurchaseOrders
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS POCount,
    COUNT(DISTINCT LinkIDVendor) AS UniqueVendors
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.PurchaseOrders
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '5.2 - PurchasedMaterial'
PRINT '-----------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS PurchasedMaterialCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.PurchasedMaterial pm
WHERE pm.LinkIDPurchaseOrder IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.PurchaseOrders WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS PurchasedMaterialCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.PurchasedMaterial pm
WHERE pm.LinkIDPurchaseOrder IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.PurchaseOrders WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '5.3 - ReceivedMaterials'
PRINT '-----------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ReceivedMaterialCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ReceivedMaterials rm
WHERE rm.LinkIDPurchaseOrder IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.PurchaseOrders WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ReceivedMaterialCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ReceivedMaterials rm
WHERE rm.LinkIDPurchaseOrder IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.PurchaseOrders WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '5.4 - ReceivedMaterialsPurchasedMaterial'
PRINT '----------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS JunctionCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ReceivedMaterialsPurchasedMaterial rmpm
WHERE rmpm.LinkIDReceivedMaterial IN (
    SELECT rm.LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.ReceivedMaterials rm
    WHERE rm.LinkIDPurchaseOrder IN (
        SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.PurchaseOrders WHERE LinkIDProject = @ProjectID
    )
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS JunctionCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ReceivedMaterialsPurchasedMaterial rmpm
WHERE rmpm.LinkIDReceivedMaterial IN (
    SELECT rm.LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ReceivedMaterials rm
    WHERE rm.LinkIDPurchaseOrder IN (
        SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.PurchaseOrders WHERE LinkIDProject = @ProjectID
    )
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '5.5 - MaterialCheckout'
PRINT '----------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS CheckoutCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.MaterialCheckout mc
WHERE mc.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS CheckoutCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.MaterialCheckout mc
WHERE mc.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 6: SPECIFICATION FILES & BINARY DATA VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 6: SPECIFICATION FILES & BINARY DATA VALIDATION'
PRINT '============================================================================'
PRINT ''

PRINT '6.1 - SpecificationGroups'
PRINT '-------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS SpecGroupCount,
    SUM(CASE WHEN Type = 6 THEN 1 ELSE 0 END) AS TemplateSpecGroups,
    SUM(CASE WHEN Type != 6 OR Type IS NULL THEN 1 ELSE 0 END) AS ProjectSpecGroups
FROM ' + QUOTENAME(@SourceDB) + '.dbo.SpecificationGroups
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS SpecGroupCount,
    SUM(CASE WHEN Type = 6 THEN 1 ELSE 0 END) AS TemplateSpecGroups,
    SUM(CASE WHEN Type != 6 OR Type IS NULL THEN 1 ELSE 0 END) AS ProjectSpecGroups
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.SpecificationGroups
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '6.2 - CutPartsFiles (Project-Scoped)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.CutPartsFiles
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.CutPartsFiles
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '6.3 - EdgebandFiles (Project-Scoped)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.EdgebandFiles
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.EdgebandFiles
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '6.4 - HardwareFiles (Project-Scoped)'
PRINT '------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.HardwareFiles
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.HardwareFiles
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '6.5 - DoorWizardFiles (Project-Scoped)'
PRINT '--------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.DoorWizardFiles
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.DoorWizardFiles
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '6.6 - GlobalFiles (Project-Scoped)'
PRINT '----------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.GlobalFiles
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.GlobalFiles
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '6.7 - ProjectWizardFiles (Project-Scoped)'
PRINT '-----------------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ProjectWizardFiles
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS FileCount,
    SUM(CASE WHEN WorkBook IS NOT NULL THEN 1 ELSE 0 END) AS FilesWithData
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ProjectWizardFiles
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 7: SHIPPING & BUNDLING VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 7: SHIPPING & BUNDLING VALIDATION'
PRINT '============================================================================'
PRINT ''

PRINT '7.1 - Bundles'
PRINT '-------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS BundleCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Bundles
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS BundleCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Bundles
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '7.2 - BundleItems'
PRINT '-----------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS BundleItemCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.BundleItems
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS BundleItemCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.BundleItems
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '7.3 - Pallets'
PRINT '-------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS PalletCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Pallets p
WHERE p.LinkIDLocation IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Locations WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS PalletCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Pallets p
WHERE p.LinkIDLocation IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Locations WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '7.4 - PalletsProducts'
PRINT '---------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS PalletProductCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.PalletsProducts pp
WHERE pp.LinkIDProduct IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS PalletProductCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.PalletsProducts pp
WHERE pp.LinkIDProduct IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 8: IMAGE & DRAWING DATA VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 8: IMAGE & DRAWING DATA VALIDATION'
PRINT '============================================================================'
PRINT ''

PRINT '8.1 - FaceFrameImages'
PRINT '---------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ImageCount,
    SUM(CASE WHEN JPegStream IS NOT NULL THEN 1 ELSE 0 END) AS WithJPeg,
    SUM(CASE WHEN TiffStream IS NOT NULL THEN 1 ELSE 0 END) AS WithTiff,
    SUM(CASE WHEN WMFStream IS NOT NULL THEN 1 ELSE 0 END) AS WithWMF
FROM ' + QUOTENAME(@SourceDB) + '.dbo.FaceFrameImages
WHERE LinkIDProduct IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ImageCount,
    SUM(CASE WHEN JPegStream IS NOT NULL THEN 1 ELSE 0 END) AS WithJPeg,
    SUM(CASE WHEN TiffStream IS NOT NULL THEN 1 ELSE 0 END) AS WithTiff,
    SUM(CASE WHEN WMFStream IS NOT NULL THEN 1 ELSE 0 END) AS WithWMF
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.FaceFrameImages
WHERE LinkIDProduct IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '8.2 - FaceFrameImagesParts'
PRINT '--------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ImagePartCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.FaceFrameImagesParts ffip
WHERE ffip.LinkIDPart IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Parts WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ImagePartCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.FaceFrameImagesParts ffip
WHERE ffip.LinkIDPart IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Parts WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '8.3 - FaceFrameImagesSubassemblies'
PRINT '-----------------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ImageSubassemblyCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.FaceFrameImagesSubassemblies ffis
WHERE ffis.LinkIDSubassembly IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Subassemblies WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ImageSubassemblyCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.FaceFrameImagesSubassemblies ffis
WHERE ffis.LinkIDSubassembly IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Subassemblies WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 9: ADDITIONAL SUPPORTING DATA VALIDATION
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 9: ADDITIONAL SUPPORTING DATA VALIDATION'
PRINT '============================================================================'
PRINT ''

PRINT '9.1 - ProductGroups'
PRINT '-------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(DISTINCT pg.LinkID) AS ProductGroupCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.ProductGroups pg ON p.LinkIDProductGroup = pg.LinkID
WHERE p.LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(DISTINCT pg.LinkID) AS ProductGroupCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.ProductGroups pg ON p.LinkIDProductGroup = pg.LinkID
WHERE p.LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.2 - ProductMap'
PRINT '----------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ProductMapCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ProductMap pm
WHERE pm.LinkIDProduct IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ProductMapCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ProductMap pm
WHERE pm.LinkIDProduct IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.3 - Activities'
PRINT '----------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ActivityCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.Activities
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ActivityCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Activities
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.4 - ProjectProperties'
PRINT '-----------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS PropertyCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ProjectProperties
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS PropertyCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ProjectProperties
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.5 - OrderLineItems'
PRINT '--------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS OrderLineItemCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.OrderLineItems oli
WHERE oli.LinkIDProduct IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products WHERE LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS OrderLineItemCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.OrderLineItems oli
WHERE oli.LinkIDProduct IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products WHERE LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.6 - ErrorMessages'
PRINT '-------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS ErrorMessageCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.ErrorMessages em
WHERE em.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS ErrorMessageCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.ErrorMessages em
WHERE em.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.7 - OptimizationResults'
PRINT '-------------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS OptResultCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.OptimizationResults orr
WHERE orr.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS OptResultCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.OptimizationResults orr
WHERE orr.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.8 - NestScrap'
PRINT '---------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS NestScrapCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.NestScrap ns
WHERE ns.LinkIDWorkOrderBatch IN (
    SELECT wb.LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.WorkOrderBatches wb
    WHERE wb.LinkIDWorkOrder IN (
        SELECT w.LinkID
        FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
        INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
        WHERE p.LinkIDProject = @ProjectID
    )
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS NestScrapCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.NestScrap ns
WHERE ns.LinkIDWorkOrderBatch IN (
    SELECT wb.LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.WorkOrderBatches wb
    WHERE wb.LinkIDWorkOrder IN (
        SELECT w.LinkID
        FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
        INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
        WHERE p.LinkIDProject = @ProjectID
    )
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.9 - NestScrapBin'
PRINT '------------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS NestScrapBinCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.NestScrapBin nsb
WHERE nsb.LinkIDCategory IN (
    SELECT LinkID FROM ' + QUOTENAME(@SourceDB) + '.dbo.Categories WHERE LinkIDParent = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS NestScrapBinCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.NestScrapBin nsb
WHERE nsb.LinkIDCategory IN (
    SELECT LinkID FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Categories WHERE LinkIDParent = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.10 - SawStacks'
PRINT '----------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS SawStackCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.SawStacks ss
WHERE ss.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@SourceDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@SourceDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS SawStackCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.SawStacks ss
WHERE ss.LinkIDWorkOrder IN (
    SELECT w.LinkID
    FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.Products p
    INNER JOIN ' + QUOTENAME(@ArchiveDB) + '.dbo.Workorders w ON p.LinkIDWorkOrder = w.LinkID
    WHERE p.LinkIDProject = @ProjectID
)'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

PRINT '9.11 - SheetSets'
PRINT '----------------'
SET @SQL = '
SELECT
    ''' + @SourceDB + ''' AS [Database],
    COUNT(*) AS SheetSetCount
FROM ' + QUOTENAME(@SourceDB) + '.dbo.SheetSets
WHERE LinkIDProject = @ProjectID

UNION ALL

SELECT
    ''' + @ArchiveDB + ''' AS [Database],
    COUNT(*) AS SheetSetCount
FROM ' + QUOTENAME(@ArchiveDB) + '.dbo.SheetSets
WHERE LinkIDProject = @ProjectID'

EXEC sp_executesql @SQL, N'@ProjectID NVARCHAR(255)', @ProjectID
PRINT ''

-- ============================================================================
-- SECTION 10: SUMMARY
-- ============================================================================
PRINT '============================================================================'
PRINT 'SECTION 10: VERIFICATION SUMMARY'
PRINT '============================================================================'
PRINT ''
PRINT 'Verification complete!'
PRINT ''
PRINT 'Review the output above for any discrepancies between source and archive.'
PRINT 'All record counts should match between the two databases.'
PRINT ''
PRINT 'NOTE: This script verifies project-scoped data only.'
PRINT 'Catalog tables (Employees, Vendors, Materials, etc.) must be verified separately.'
PRINT ''
