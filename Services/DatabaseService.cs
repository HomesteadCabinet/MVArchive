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
            var projects = new List<Project>();

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    using (var command = new SqlCommand("SELECT * FROM Projects", connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var project = new Project
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

                            projects.Add(project);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // In a production app, you'd want proper logging here
                System.Diagnostics.Debug.WriteLine($"Database error: {ex.Message}");
                throw;
            }

            return projects;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
