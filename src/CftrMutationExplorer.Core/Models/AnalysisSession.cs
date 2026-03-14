namespace CftrMutationExplorer.Core.Models;

public class AnalysisSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? ReferenceFilePath { get; set; }
    public string? MutantFilePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }
    public string? ViewMode { get; set; }
    public int? SelectedResidueNumber { get; set; }
    public char? SelectedChainId { get; set; }
}
