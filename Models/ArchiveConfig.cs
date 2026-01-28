namespace MVArchive.Models
{
    public class ArchiveConfig
    {
        public string SourceDatabase { get; set; } = string.Empty;
        public string DestinationDatabase { get; set; } = string.Empty;
        public bool IsDryRun { get; set; } = true;
        public string SourceHost { get; set; } = string.Empty;
        public string SourcePort { get; set; } = string.Empty;
        public string SourceUser { get; set; } = string.Empty;
        public string SourcePassword { get; set; } = string.Empty;
        public string DestinationHost { get; set; } = string.Empty;
        public string DestinationPort { get; set; } = string.Empty;
        public string DestinationUser { get; set; } = string.Empty;
        public string DestinationPassword { get; set; } = string.Empty;
        public bool OverwriteExisting { get; set; } = false;
        public string FactoryDatabasePath { get; set; } = @"M:\Homestead_Library\Factory Database";
        public string ProjectFilesDestinationPath { get; set; } = string.Empty;
        public int MaxEntries { get; set; } = 500;
        public bool SkipCatalogTables { get; set; } = false;
    }
}
