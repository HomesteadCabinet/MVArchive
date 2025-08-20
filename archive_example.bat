@echo off
echo MV Archive - Archive Example Setup
echo ===================================
echo.
echo This batch file shows how to set up environment variables for archiving.
echo You can modify these values to match your database configuration.
echo.
echo Setting environment variables for archiving...
echo.

REM Set source database (projects to archive)
set MICROVELLUM_DB_HOST=192.168.1.35
set MICROVELLUM_DB_PORT=1435
set MICROVELLUM_DB_NAME=testdb
set MICROVELLUM_DB_USER=sa
set MICROVELLUM_DB_PASSWORD=H0m35te@d12!

echo Source Database Configuration:
echo   Host: %MICROVELLUM_DB_HOST%
echo   Port: %MICROVELLUM_DB_PORT%
echo   Database: %MICROVELLUM_DB_NAME%
echo   User: %MICROVELLUM_DB_USER%
echo.

echo Archive Database Configuration (TestArchive):
echo   Host: 192.168.1.35
echo   Port: 1435
echo   Database: TestArchive
echo   User: sa
echo   Password: H0m35te@d12!
echo.

echo To run the archive application:
echo   1. Build: dotnet build
echo   2. Run: dotnet run
echo   3. Click "Archive Configuration" to set up archive settings
echo   4. Test connections to both databases
echo   5. Choose dry run mode for testing
echo   6. Archive individual projects or all projects
echo.

echo Environment variables are now set for this session.
echo Close this window to clear the variables.
echo.
pause
