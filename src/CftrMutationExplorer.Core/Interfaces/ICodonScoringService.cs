using CftrMutationExplorer.Core.Models.Mrna;

namespace CftrMutationExplorer.Core.Interfaces;

/// <summary>
/// Scores mRNA candidate sequences on multiple objectives.
/// All scoring functions are stateless and thread-safe for parallel evaluation.
/// </summary>
public interface ICodonScoringService
{
    /// <summary>
    /// Compute all objectives and the composite fitness for a candidate.
    /// </summary>
    OptimizationScore ScoreCandidate(MrnaCandidate candidate, string proteinSequence, ObjectiveWeights weights);

    /// <summary>
    /// Batch-score multiple candidates in parallel.
    /// </summary>
    void ScoreBatch(IList<MrnaCandidate> candidates, string proteinSequence, ObjectiveWeights weights);

    double CalculateCai(string rnaSequence);
    double CalculateGcContentScore(string rnaSequence, double targetMin = 0.45, double targetMax = 0.55);
    double CalculateCpgScore(string rnaSequence);
    double CalculateUridineScore(string rnaSequence);
    double CalculateRareCodonScore(string rnaSequence);
    double CalculateRepeatScore(string rnaSequence);
    double CalculateCodonPairScore(string rnaSequence);

    /// <summary>
    /// Get a detailed per-position analysis of codon quality.
    /// </summary>
    List<CodonPositionAnalysis> AnalyzeByPosition(string rnaSequence);
}

public class CodonPositionAnalysis
{
    public int Position { get; set; }
    public string Codon { get; set; } = string.Empty;
    public string AminoAcid { get; set; } = string.Empty;
    public double RelativeAdaptiveness { get; set; }
    public double LocalGcContent { get; set; }
    public bool IsRareCodon { get; set; }
    public bool IsInCpgContext { get; set; }
}
