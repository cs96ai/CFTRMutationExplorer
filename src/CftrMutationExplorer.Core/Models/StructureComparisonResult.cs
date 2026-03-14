namespace CftrMutationExplorer.Core.Models;

public class StructureComparisonResult
{
    public string ReferenceFileName { get; set; } = string.Empty;
    public string MutantFileName { get; set; } = string.Empty;

    public int ReferenceChainCount { get; set; }
    public int MutantChainCount { get; set; }
    public int ReferenceResidueCount { get; set; }
    public int MutantResidueCount { get; set; }
    public int ReferenceAtomCount { get; set; }
    public int MutantAtomCount { get; set; }

    public int ResidueDifference => MutantResidueCount - ReferenceResidueCount;
    public int AtomDifference => MutantAtomCount - ReferenceAtomCount;

    /// <summary>
    /// Simplified RMSD in Angstroms, computed over paired alpha-carbon atoms.
    /// This is a demo approximation, not a publication-grade metric.
    /// </summary>
    public double? SimplifiedRmsd { get; set; }

    public List<ResidueComparisonEntry> ResidueComparisons { get; set; } = new();
    public List<string> MissingInMutant { get; set; } = new();
    public List<string> MissingInReference { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public string RmsdDisplayText =>
        SimplifiedRmsd.HasValue
            ? $"{SimplifiedRmsd.Value:F2} Å (demo approximation)"
            : "N/A";
}

public class ResidueComparisonEntry
{
    public int ResidueNumber { get; set; }
    public char ChainId { get; set; }
    public string ReferenceResidueName { get; set; } = string.Empty;
    public string MutantResidueName { get; set; } = string.Empty;
    public double? CentroidDistance { get; set; }
    public bool IsAltered => ReferenceResidueName != MutantResidueName;
    public bool IsMissing { get; set; }
}
