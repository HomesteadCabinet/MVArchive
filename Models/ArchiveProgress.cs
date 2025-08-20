using System;

namespace MVArchive.Models
{
    public class ArchiveProgress
    {
        public string CurrentTable { get; set; } = string.Empty;
        public int CurrentRecord { get; set; }
        public int TotalRecords { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsComplete { get; set; }
        public int TablesProcessed { get; set; }
        public int TotalTables { get; set; }
        public string CurrentProject { get; set; } = string.Empty;
        public int ProjectsProcessed { get; set; }
        public int TotalProjects { get; set; }
    }
}
