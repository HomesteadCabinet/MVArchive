using System;
using System.IO;
using System.Xml.Serialization;
using MVArchive.Models;

namespace MVArchive.Services
{
  public sealed class ConfigService
  {
    private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());
    public static ConfigService Instance => _instance.Value;

    private ArchiveConfig _current;
    private readonly string _settingsFilePath;
    public ArchiveConfig Current
    {
      get => _current;
      private set
      {
        _current = value;
        ConfigurationChanged?.Invoke(this, _current);
      }
    }

    public event EventHandler<ArchiveConfig>? ConfigurationChanged;

    private ConfigService()
    {
      _settingsFilePath = GetSettingsPath();
      if (!TryLoadFromDisk(out var loaded))
      {
        _current = LoadDefaultsFromEnvironment();
        // Best-effort save of defaults so a file exists
        TrySaveToDisk(_current);
      }
      else
      {
        _current = loaded!;
      }
    }

    public void Update(ArchiveConfig config)
    {
      Current = config;
      TrySaveToDisk(config);
    }

    private static ArchiveConfig LoadDefaultsFromEnvironment()
    {
      var host = Environment.GetEnvironmentVariable("MICROVELLUM_DB_HOST") ?? "192.168.1.35";
      var port = Environment.GetEnvironmentVariable("MICROVELLUM_DB_PORT") ?? "1435";
      var db = Environment.GetEnvironmentVariable("MICROVELLUM_DB_NAME") ?? "testdb";
      var user = Environment.GetEnvironmentVariable("MICROVELLUM_DB_USER") ?? "sa";
      var pwd = Environment.GetEnvironmentVariable("MICROVELLUM_DB_PASSWORD") ?? "H0m35te@d12!";

      return new ArchiveConfig
      {
        SourceHost = host,
        SourcePort = port,
        SourceDatabase = db,
        SourceUser = user,
        SourcePassword = pwd,
        DestinationHost = host,
        DestinationPort = port,
        DestinationDatabase = "TestArchive",
        DestinationUser = user,
        DestinationPassword = pwd,
        IsDryRun = true,
        OverwriteExisting = false,
        FactoryDatabasePath = @"M:\Homestead_Library\Factory Database",
        ProjectFilesDestinationPath = @"C:\ArchiveProjects",
        MaxEntries = 500,
        SkipCatalogTables = false
      };
    }

    private static string GetSettingsPath()
    {
      var baseDir = AppDomain.CurrentDomain.BaseDirectory;
      return Path.Combine(baseDir, "settings.xml");
    }

    private bool TryLoadFromDisk(out ArchiveConfig? config)
    {
      config = null;
      try
      {
        if (!File.Exists(_settingsFilePath))
        {
          return false;
        }

        using var stream = File.OpenRead(_settingsFilePath);
        var serializer = new XmlSerializer(typeof(ArchiveConfig));
        if (serializer.Deserialize(stream) is ArchiveConfig loaded)
        {
          config = loaded;
          return true;
        }
      }
      catch
      {
        // Ignore and fall back to defaults
      }
      return false;
    }

    private void TrySaveToDisk(ArchiveConfig config)
    {
      try
      {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
          Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(_settingsFilePath);
        var serializer = new XmlSerializer(typeof(ArchiveConfig));
        serializer.Serialize(stream, config);
      }
      catch
      {
        // Non-fatal: persistence failures should not crash the app
      }
    }
  }
}
