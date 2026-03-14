using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;
using Microsoft.Data.Sqlite;

namespace CftrMutationExplorer.Infrastructure.Persistence;

public class SqliteSessionPersistenceService : ISessionPersistenceService
{
    private readonly string _connectionString;

    public SqliteSessionPersistenceService(string? dbPath = null)
    {
        _connectionString = DatabaseInitializer.GetConnectionString(dbPath);
    }

    public async Task<AnalysisSession> CreateSessionAsync(string name)
    {
        var session = new AnalysisSession { Name = name };

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AnalysisSessions (Id, Name, CreatedAt, LastAccessedAt)
            VALUES (@id, @name, @created, @accessed)
            """;
        cmd.Parameters.AddWithValue("@id", session.Id);
        cmd.Parameters.AddWithValue("@name", session.Name);
        cmd.Parameters.AddWithValue("@created", session.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@accessed", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
        return session;
    }

    public async Task<AnalysisSession?> GetSessionAsync(string id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AnalysisSessions WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSession(reader) : null;
    }

    public async Task<List<AnalysisSession>> GetRecentSessionsAsync(int count = 10)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM AnalysisSessions ORDER BY LastAccessedAt DESC LIMIT @count";
        cmd.Parameters.AddWithValue("@count", count);

        var sessions = new List<AnalysisSession>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            sessions.Add(MapSession(reader));
        return sessions;
    }

    public async Task UpdateSessionAsync(AnalysisSession session)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE AnalysisSessions SET
                Name = @name, ReferenceFilePath = @refPath, MutantFilePath = @mutPath,
                LastAccessedAt = @accessed, ViewMode = @viewMode,
                SelectedResidueNumber = @resNum, SelectedChainId = @chainId
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", session.Id);
        cmd.Parameters.AddWithValue("@name", session.Name);
        cmd.Parameters.AddWithValue("@refPath", (object?)session.ReferenceFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mutPath", (object?)session.MutantFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@accessed", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@viewMode", (object?)session.ViewMode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@resNum", (object?)session.SelectedResidueNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@chainId", (object?)session.SelectedChainId?.ToString() ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteSessionAsync(string id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AnalysisSessions WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddRecentFileAsync(string filePath)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RecentFiles (FilePath, AccessedAt) VALUES (@path, @accessed)
            ON CONFLICT(FilePath) DO UPDATE SET AccessedAt = @accessed
            """;
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.Parameters.AddWithValue("@accessed", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetRecentFilesAsync(int count = 10)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FilePath FROM RecentFiles ORDER BY AccessedAt DESC LIMIT @count";
        cmd.Parameters.AddWithValue("@count", count);

        var files = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            files.Add(reader.GetString(0));
        return files;
    }

    private static AnalysisSession MapSession(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        ReferenceFilePath = reader.IsDBNull(reader.GetOrdinal("ReferenceFilePath")) ? null : reader.GetString(reader.GetOrdinal("ReferenceFilePath")),
        MutantFilePath = reader.IsDBNull(reader.GetOrdinal("MutantFilePath")) ? null : reader.GetString(reader.GetOrdinal("MutantFilePath")),
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        LastAccessedAt = reader.IsDBNull(reader.GetOrdinal("LastAccessedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastAccessedAt"))),
        ViewMode = reader.IsDBNull(reader.GetOrdinal("ViewMode")) ? null : reader.GetString(reader.GetOrdinal("ViewMode")),
        SelectedResidueNumber = reader.IsDBNull(reader.GetOrdinal("SelectedResidueNumber")) ? null : reader.GetInt32(reader.GetOrdinal("SelectedResidueNumber")),
        SelectedChainId = reader.IsDBNull(reader.GetOrdinal("SelectedChainId")) ? null : reader.GetString(reader.GetOrdinal("SelectedChainId"))[0]
    };
}
