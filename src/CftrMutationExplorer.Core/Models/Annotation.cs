namespace CftrMutationExplorer.Core.Models;

public enum AnnotationCategory
{
    GeneralNote,
    MutationImpact,
    PossibleBindingSite,
    UnstableRegion,
    InterestingRegion
}

public class Annotation
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public AnnotationCategory Category { get; set; } = AnnotationCategory.GeneralNote;
    public char? TargetChainId { get; set; }
    public int? TargetResidueNumber { get; set; }
    public string? TargetRegionDescription { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
    public string? SessionId { get; set; }
}
