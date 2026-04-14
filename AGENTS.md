# Repository Guidelines

## Project Structure & Module Organization
`MVArchive` is a .NET 9 WPF desktop app. UI entry points live at the repo root: `App.xaml`, `MainWindow.xaml`, `ArchiveConfigWindow.xaml`, and `ArchiveProgressWindow.xaml`, each paired with a `.xaml.cs` file. Domain models are in `Models/`, data and archive logic are in `Services/`, and reusable UI pieces are in `Controls/`. Reference material and verification notes live in `docs/` and `resources/`. Generated folders such as `bin/`, `bin_alt/`, and `obj/` should not be edited by hand.

## Build, Test, and Development Commands
Use the .NET CLI from the repository root:

- `dotnet restore` restores NuGet packages.
- `dotnet build` compiles the WPF app for `net9.0-windows`.
- `dotnet run` launches the application locally.
- `build_and_run.bat` performs a local build, then starts the app if the build succeeds.

For archive verification, run `verify_archive_completeness.sql` and `verify_catalog_tables.sql` in SSMS, Azure Data Studio, or with `sqlcmd` after setting the database names and project `LinkID`.

## Coding Style & Naming Conventions
Follow the existing C# and XAML conventions in the repo: braces on the next line in C#, nullable reference types enabled, and async methods suffixed with `Async`. Use `PascalCase` for types, methods, and public properties, `_camelCase` for private fields, and descriptive XAML control names such as `txtStatus`, `dgAvailable`, or `btnExportLog`. Match the surrounding file's indentation and spacing rather than reformatting unrelated code.

## Testing Guidelines
There is no separate automated test project checked in today. Treat `dotnet build` as the minimum validation step, then verify behavior manually in the UI against a non-production database. For archive changes, run both SQL verification scripts and confirm dry-run and destructive paths behave correctly. If you add automated tests later, place them in a sibling test project and name files after the class or service under test.

## Commit & Pull Request Guidelines
Current git history uses short summaries (`Initial commit`, `1-28-26`, `4-14-26`), but future commits should be more descriptive and imperative, for example: `Add destination filtering for archived projects`. Keep commits focused. PRs should include the problem, the approach, manual validation steps, and screenshots for WPF UI changes. Link related work, and call out database-impacting or destructive archive changes explicitly.

## Security & Configuration Tips
Database credentials are currently sourced from environment variables such as `MICROVELLUM_DB_HOST`, `MICROVELLUM_DB_USER`, and `MICROVELLUM_DB_PASSWORD`. Prefer environment overrides instead of hardcoding secrets, and test archive operations against disposable or staging databases first.
