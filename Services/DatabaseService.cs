using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using MVArchive.Models;

namespace MVArchive.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService()
        {
            var host = Environment.GetEnvironmentVariable("MICROVELLUM_DB_HOST") ?? "192.168.1.35";
            var user = Environment.GetEnvironmentVariable("MICROVELLUM_DB_USER") ?? "sa";
            var password = Environment.GetEnvironmentVariable("MICROVELLUM_DB_PASSWORD") ?? "H0m35te@d12!";
            var port = Environment.GetEnvironmentVariable("MICROVELLUM_DB_PORT") ?? "1435";
            var dbName = Environment.GetEnvironmentVariable("MICROVELLUM_DB_NAME") ?? "testdb";

            _connectionString = $"Server={host},{port};Database={dbName};User Id={user};Password={password};TrustServerCertificate=true;";
        }

        public async Task<List<Project>> GetAllProjectsAsync()
        {
            return await GetAllProjectsFromConnectionAsync(_connectionString);
        }

        /// <summary>Load projects from a database using the given connection string (e.g. destination archive).</summary>
        public async Task<List<Project>> GetAllProjectsFromConnectionAsync(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return new List<Project>();

            var projects = new List<Project>();
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                using var command = new SqlCommand("SELECT * FROM Projects WHERE ISNULL(Name,'') <> 'Template'", connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    projects.Add(ReadProject(reader));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database error: {ex.Message}");
                throw;
            }
            return projects;
        }

        private static Project ReadProject(SqlDataReader reader)
        {
            return new Project
            {
                Id = reader.GetGuid("ID"),
                Name = reader.IsDBNull("Name") ? null : reader.GetString("Name"),
                JobNumber = reader.IsDBNull("JobNumber") ? null : reader.GetString("JobNumber"),
                JobDescription = reader.IsDBNull("JobDescription") ? null : reader.GetString("JobDescription"),
                JobAddress = reader.IsDBNull("JobAddress") ? null : reader.GetString("JobAddress"),
                JobPhone = reader.IsDBNull("JobPhone") ? null : reader.GetString("JobPhone"),
                JobEMail = reader.IsDBNull("JobEMail") ? null : reader.GetString("JobEMail"),
                JobFax = reader.IsDBNull("JobFax") ? null : reader.GetString("JobFax"),
                ProjectManager = reader.IsDBNull("ProjectManager") ? null : reader.GetString("ProjectManager"),
                Architect = reader.IsDBNull("Architect") ? null : reader.GetString("Architect"),
                Contractor = reader.IsDBNull("Contractor") ? null : reader.GetString("Contractor"),
                Draftsman = reader.IsDBNull("Draftsman") ? null : reader.GetString("Draftsman"),
                Estimator = reader.IsDBNull("Estimator") ? null : reader.GetString("Estimator"),
                GeneralContact = reader.IsDBNull("GeneralContact") ? null : reader.GetString("GeneralContact"),
                ProjectNumber = reader.IsDBNull("ProjectNumber") ? null : reader.GetInt32("ProjectNumber"),
                ProjectBudget = reader.IsDBNull("ProjectBudget") ? null : (float?)reader.GetDouble("ProjectBudget"),
                TotalProjectCost = reader.IsDBNull("TotalProjectCost") ? null : (float?)reader.GetDouble("TotalProjectCost"),
                ScheduledStartDate = reader.IsDBNull("ScheduledStartDate") ? null : reader.GetDateTime("ScheduledStartDate"),
                ScheduledCompletionDate = reader.IsDBNull("ScheduledCompletionDate") ? null : reader.GetDateTime("ScheduledCompletionDate"),
                DateCreated = reader.IsDBNull("DateCreated") ? null : reader.GetDateTime("DateCreated"),
                DateLastOpened = reader.IsDBNull("DateLastOpened") ? null : reader.GetDateTime("DateLastOpened"),
                IsInactive = reader.IsDBNull("IsInactive") ? null : reader.GetBoolean("IsInactive"),
                PrintFlag = reader.IsDBNull("PrintFlag") ? null : reader.GetBoolean("PrintFlag"),
                Modified = reader.IsDBNull("Modified") ? null : reader.GetBoolean("Modified"),
                Type = reader.IsDBNull("Type") ? null : reader.GetInt32("Type"),
                LinkID = reader.IsDBNull("LinkID") ? null : reader.GetString("LinkID"),
                LinkIDCategory = reader.IsDBNull("LinkIDCategory") ? null : reader.GetString("LinkIDCategory"),
                LinkIDCustomerCompany = reader.IsDBNull("LinkIDCustomerCompany") ? null : reader.GetString("LinkIDCustomerCompany"),
                LocationCoordinates = reader.IsDBNull("LocationCoordinates") ? null : reader.GetString("LocationCoordinates")
            };
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<Project?> GetProjectByLinkIdAsync(string linkId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand("SELECT * FROM Projects WHERE LinkID = @LinkId", connection);
                command.Parameters.AddWithValue("@LinkId", linkId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                    return ReadProject(reader);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database error: {ex.Message}");
                throw;
            }

            return null;
        }

        /// <summary>Clear all data from all tables in the specified database.</summary>
        public async Task ClearAllDataAsync(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Get all table names from the database
            var tables = new List<string>();
            using (var command = new SqlCommand(@"
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                AND TABLE_CATALOG = DB_NAME()
                ORDER BY TABLE_NAME", connection))
            {
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            // Disable all foreign key constraints
            using (var command = new SqlCommand("EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'", connection))
            {
                await command.ExecuteNonQueryAsync();
            }

            // Delete all data from each table
            foreach (var table in tables)
            {
                using var command = new SqlCommand($"DELETE FROM [{table}]", connection);
                await command.ExecuteNonQueryAsync();
            }

            // Re-enable all foreign key constraints
            using (var command = new SqlCommand("EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'", connection))
            {
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
