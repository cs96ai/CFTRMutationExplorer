using Microsoft.Data.Sqlite;

namespace CftrMutationExplorer.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static string DefaultDatabasePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CftrMutationExplorer",
            "cftr_explorer.db");

    public static void EnsureCreated(string? dbPath = null)
    {
        var path = dbPath ?? DefaultDatabasePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Annotations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Note TEXT,
                Category INTEGER NOT NULL DEFAULT 0,
                TargetChainId TEXT,
                TargetResidueNumber INTEGER,
                TargetRegionDescription TEXT,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT,
                SessionId TEXT
            );

            CREATE TABLE IF NOT EXISTS AnalysisSessions (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ReferenceFilePath TEXT,
                MutantFilePath TEXT,
                CreatedAt TEXT NOT NULL,
                LastAccessedAt TEXT,
                ViewMode TEXT,
                SelectedResidueNumber INTEGER,
                SelectedChainId TEXT
            );

            CREATE TABLE IF NOT EXISTS RecentFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath TEXT NOT NULL UNIQUE,
                AccessedAt TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public static string GetConnectionString(string? dbPath = null) =>
        $"Data Source={dbPath ?? DefaultDatabasePath}";
}
