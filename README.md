# MV Archive - WPF Database Viewer & Archiver

A comprehensive WPF application that connects to a MSSQL database, displays all records from the "Projects" table, and provides powerful archiving capabilities for projects and all related data.

## Features

- **Database Viewer**: Connect to MSSQL database and display all Projects table records
- **Comprehensive Archiving**: Archive projects and ALL related data including:
  - Locations, Products, Subassemblies
  - Hardware, Edgebanding, Drawings
  - All file types (AutoCAD, TIFF, Cut Parts, etc.)
  - Activities, Bundles, Work Orders
  - Correspondence, Purchase Orders
- **Dry Run Mode**: Copy data to archive without deleting from source
- **Flexible Configuration**: Separate source and destination database settings
- **Real-time Progress**: Monitor archive progress with detailed logging
- **Connection Testing**: Test both source and destination connections
- **Error Handling**: Comprehensive error handling and user feedback

## Database Connection

The application uses the following connection parameters:

- **Source Database**: `MICROVELLUM_DB_HOST` environment variable or defaults to `192.168.1.35`
- **User**: `MICROVELLUM_DB_USER` environment variable or defaults to `sa`
- **Password**: `MICROVELLUM_DB_PASSWORD` environment variable or defaults to `H0m35te@d12!`
- **Port**: `MICROVELLUM_DB_PORT` environment variable or defaults to `1435`
- **Database**: `MICROVELLUM_DB_NAME` environment variable or defaults to `testdb`

## Archive Configuration

### Source and Destination Databases
- **Source Database**: The database containing projects to be archived
- **Destination Database**: The archive database (default: `TestArchive`)
- **Separate Connections**: Configure different hosts, ports, and credentials for each

### Archive Options
- **Dry Run Mode**: When enabled, data is copied to archive but source data remains intact
- **Full Archive Mode**: When disabled, source data is deleted after successful archiving

## Projects Table Structure

The application displays the following columns from the Projects table:
- ID (GUID)
- Name
- Job Number
- Job Description
- Project Manager
- Architect
- Contractor
- Project Budget
- Total Project Cost
- Scheduled Start Date
- Scheduled Completion Date
- Date Created
- Inactive Status

## Related Data Archiving

The archiving system automatically identifies and archives all related data:

### Core Project Data
- **Projects** - Main project records
- **Locations** - Project locations and addresses
- **Products** - Project products and specifications
- **Subassemblies** - Project subassembly components

### Technical Data
- **Hardware** - Project hardware specifications
- **Edgebanding** - Project edgebanding details
- **AutoCADDrawings** - Project AutoCAD drawings
- **TiffDrawings** - Project TIFF format drawings

### File Management
- **CutPartsFiles** - Project cut parts files
- **DoorWizardFiles** - Project door wizard files
- **EdgebandFiles** - Project edgeband files
- **GlobalFiles** - Project global files
- **HardwareFiles** - Project hardware files
- **ProjectWizardFiles** - Project wizard files

### Business Data
- **Activities** - Project activities and tasks
- **Bundles** - Project bundles and packages
- **WorkOrders** - Project work orders
- **Correspondence** - Project communications
- **PurchaseOrders** - Project purchase orders

## Environment Variables (Optional)

You can set these environment variables to override the default connection settings:

```bash
MICROVELLUM_DB_HOST=your_host
MICROVELLUM_DB_USER=your_username
MICROVELLUM_DB_PASSWORD=your_password
MICROVELLUM_DB_PORT=your_port
MICROVELLUM_DB_NAME=your_database
```

## Prerequisites

- .NET 9.0 or later
- SQL Server instance accessible from your machine
- Projects table in the specified database
- Archive database (e.g., `TestArchive`) with appropriate permissions

## Building and Running

1. **Restore NuGet packages:**
   ```bash
   dotnet restore
   ```

2. **Build the application:**
   ```bash
   dotnet build
   ```

3. **Run the application:**
   ```bash
   dotnet run
   ```

## Usage

### Basic Database Viewing
1. **Launch the application** - It will automatically test the database connection
2. **Test Connection** - Click the "Test Connection" button to verify database connectivity
3. **View Data** - If connected, the Projects table data will automatically load
4. **Refresh** - Click "Refresh Data" to reload the latest data from the database

### Archiving Projects
1. **Configure Archive Settings** - Click "Archive Configuration" to set source/destination databases
2. **Test Connections** - Verify both source and destination database connectivity
3. **Archive Single Project** - Select a project and click "Archive Selected Project"
4. **Archive All Projects** - Click "Archive All Projects" to archive everything
5. **Monitor Progress** - Watch real-time progress in the archive progress window

## Archive Process

### Step 1: Configuration
- Set source database (projects to archive)
- Set destination database (archive location)
- Choose dry run mode (recommended for testing)
- Test both connections

### Step 2: Data Archiving
- Copy main project records
- Identify all related tables with `LinkIDProject` references
- Create destination tables if they don't exist
- Copy all related data in batches
- Handle binary data (drawings, files) properly

### Step 3: Cleanup (Non-Dry Run)
- Delete archived data from source database
- Maintain referential integrity
- Log all operations

## Project Structure

- `Models/Project.cs` - Data model for Project records matching the database schema
- `Models/ArchiveConfig.cs` - Archive configuration settings
- `Models/ArchiveProgress.cs` - Archive progress tracking
- `Services/DatabaseService.cs` - Database connection and query logic using Microsoft.Data.SqlClient
- `Services/ArchiveService.cs` - Comprehensive archiving service
- `MainWindow.xaml` - Main UI layout with archive controls
- `MainWindow.xaml.cs` - Main window logic and archive operations
- `ArchiveConfigWindow.xaml` - Archive configuration interface
- `ArchiveProgressWindow.xaml` - Real-time archive progress monitoring
- `App.xaml` - Application entry point

## Troubleshooting

- **Connection Failed**: Verify your database server is running and accessible
- **No Data**: Ensure the Projects table exists and contains data
- **Permission Error**: Check that the database user has SELECT permissions on the Projects table
- **Build Errors**: Ensure you have .NET 9.0 SDK installed
- **Archive Failures**: Verify both source and destination database permissions
- **Missing Tables**: Ensure the archive database has sufficient space and permissions

## Safety Features

- **Dry Run Mode**: Always test with dry run enabled first
- **Connection Testing**: Verify both databases before archiving
- **Progress Monitoring**: Real-time progress tracking and logging
- **Error Handling**: Comprehensive error reporting and recovery
- **Confirmation Dialogs**: User confirmation for destructive operations

## Notes

- The application uses `TrustServerCertificate=true` for development purposes
- All database operations are asynchronous for better UI responsiveness
- Error messages are displayed in both the status bar and message boxes
- The Project model matches the exact schema from the database
- Archive operations can be cancelled at any time
- Binary data (drawings, files) is preserved during archiving
- Foreign key relationships are maintained during the archive process
