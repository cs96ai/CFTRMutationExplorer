using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Core.Interfaces;

public interface IAnnotationRepository
{
    Task<List<Annotation>> GetAllAsync(string? sessionId = null);
    Task<Annotation?> GetByIdAsync(int id);
    Task<int> AddAsync(Annotation annotation);
    Task UpdateAsync(Annotation annotation);
    Task DeleteAsync(int id);
    Task<List<Annotation>> GetByResidueAsync(char chainId, int residueNumber);
}
