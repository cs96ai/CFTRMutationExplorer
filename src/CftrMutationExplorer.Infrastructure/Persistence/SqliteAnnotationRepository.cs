using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;
using Microsoft.Data.Sqlite;

namespace CftrMutationExplorer.Infrastructure.Persistence;

public class SqliteAnnotationRepository : IAnnotationRepository
{
    private readonly string _connectionString;

    public SqliteAnnotationRepository(string? dbPath = null)
    {
        _connectionString = DatabaseInitializer.GetConnectionString(dbPath);
    }

    public async Task<List<Annotation>> GetAllAsync(string? sessionId = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        if (sessionId != null)
        {
            cmd.CommandText = "SELECT * FROM Annotations WHERE SessionId = @sessionId ORDER BY CreatedAt DESC";
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
        }
        else
        {
            cmd.CommandText = "SELECT * FROM Annotations ORDER BY CreatedAt DESC";
        }

        var annotations = new List<Annotation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            annotations.Add(MapAnnotation(reader));
        }
        return annotations;
    }

    public async Task<Annotation?> GetByIdAsync(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Annotations WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapAnnotation(reader) : null;
    }

    public async Task<int> AddAsync(Annotation annotation)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Annotations (Title, Note, Category, TargetChainId, TargetResidueNumber, TargetRegionDescription, CreatedAt, SessionId)
            VALUES (@title, @note, @category, @chainId, @resNum, @region, @created, @session);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@title", annotation.Title);
        cmd.Parameters.AddWithValue("@note", (object?)annotation.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@category", (int)annotation.Category);
        cmd.Parameters.AddWithValue("@chainId", (object?)annotation.TargetChainId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@resNum", (object?)annotation.TargetResidueNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@region", (object?)annotation.TargetRegionDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", annotation.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@session", (object?)annotation.SessionId ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Annotation annotation)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Annotations SET Title = @title, Note = @note, Category = @category,
            TargetChainId = @chainId, TargetResidueNumber = @resNum,
            TargetRegionDescription = @region, ModifiedAt = @modified
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", annotation.Id);
        cmd.Parameters.AddWithValue("@title", annotation.Title);
        cmd.Parameters.AddWithValue("@note", (object?)annotation.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@category", (int)annotation.Category);
        cmd.Parameters.AddWithValue("@chainId", (object?)annotation.TargetChainId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@resNum", (object?)annotation.TargetResidueNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@region", (object?)annotation.TargetRegionDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@modified", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Annotations WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Annotation>> GetByResidueAsync(char chainId, int residueNumber)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Annotations WHERE TargetChainId = @chain AND TargetResidueNumber = @resNum ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@chain", chainId.ToString());
        cmd.Parameters.AddWithValue("@resNum", residueNumber);

        var annotations = new List<Annotation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            annotations.Add(MapAnnotation(reader));
        return annotations;
    }

    private static Annotation MapAnnotation(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("Id")),
        Title = reader.GetString(reader.GetOrdinal("Title")),
        Note = reader.IsDBNull(reader.GetOrdinal("Note")) ? string.Empty : reader.GetString(reader.GetOrdinal("Note")),
        Category = (AnnotationCategory)reader.GetInt32(reader.GetOrdinal("Category")),
        TargetChainId = reader.IsDBNull(reader.GetOrdinal("TargetChainId")) ? null : reader.GetString(reader.GetOrdinal("TargetChainId"))[0],
        TargetResidueNumber = reader.IsDBNull(reader.GetOrdinal("TargetResidueNumber")) ? null : reader.GetInt32(reader.GetOrdinal("TargetResidueNumber")),
        TargetRegionDescription = reader.IsDBNull(reader.GetOrdinal("TargetRegionDescription")) ? null : reader.GetString(reader.GetOrdinal("TargetRegionDescription")),
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        ModifiedAt = reader.IsDBNull(reader.GetOrdinal("ModifiedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("ModifiedAt"))),
        SessionId = reader.IsDBNull(reader.GetOrdinal("SessionId")) ? null : reader.GetString(reader.GetOrdinal("SessionId"))
    };
}
