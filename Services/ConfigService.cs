using System;
using MVArchive.Models;

namespace MVArchive.Services
{
	public sealed class ConfigService
	{
		private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());
		public static ConfigService Instance => _instance.Value;

		private ArchiveConfig _current;
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
			_current = LoadDefaultsFromEnvironment();
		}

		public void Update(ArchiveConfig config)
		{
			Current = config;
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
				IsDryRun = true
			};
		}
	}
}
