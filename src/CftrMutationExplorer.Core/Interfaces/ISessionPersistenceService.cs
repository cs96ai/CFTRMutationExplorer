using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Core.Interfaces;

public interface ISessionPersistenceService
{
    Task<AnalysisSession> CreateSessionAsync(string name);
    Task<AnalysisSession?> GetSessionAsync(string id);
    Task<List<AnalysisSession>> GetRecentSessionsAsync(int count = 10);
    Task UpdateSessionAsync(AnalysisSession session);
    Task DeleteSessionAsync(string id);
    Task AddRecentFileAsync(string filePath);
    Task<List<string>> GetRecentFilesAsync(int count = 10);
}
