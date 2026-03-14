using CftrMutationExplorer.Core.Models.Mrna;

namespace CftrMutationExplorer.Core.Interfaces;

/// <summary>
/// Orchestrates the full mRNA optimization pipeline:
/// sequence loading → optimization → construct assembly → export.
/// </summary>
public interface IMrnaOptimizationService
{
    /// <summary>
    /// Run the multi-objective optimization to find optimal codon choices for the given protein.
    /// </summary>
    Task<OptimizationResult> OptimizeAsync(
        string proteinSequence,
        OptimizationConfig config,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assemble a complete mRNA construct from an optimized candidate.
    /// </summary>
    ConstructDesign AssembleConstruct(MrnaCandidate candidate, string proteinSequence, OptimizationConfig config);

    /// <summary>
    /// Score the native (wildtype) CFTR CDS for comparison.
    /// </summary>
    OptimizationScore ScoreNativeSequence(string rnaCds, ObjectiveWeights weights);

    /// <summary>
    /// Get the CFTR protein sequence.
    /// </summary>
    string GetCftrProteinSequence();
}
