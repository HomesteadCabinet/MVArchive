@echo off
echo Building MV Archive WPF Application...
@REM dotnet clean
@REM dotnet restore
dotnet build

if %ERRORLEVEL% EQU 0 (
    echo Build successful! Starting application...
    dotnet run
) else (
    echo Build failed! Please check the error messages above.
    pause
)
