namespace CftrMutationExplorer.Core.Models.Mrna;

/// <summary>
/// Configuration for the mRNA optimization genetic algorithm.
/// </summary>
public class OptimizationConfig
{
    public int PopulationSize { get; set; } = 500;
    public int MaxGenerations { get; set; } = 1000;
    public double CrossoverRate { get; set; } = 0.85;
    public double MutationRate { get; set; } = 0.15;
    public int TournamentSize { get; set; } = 3;
    public int EliteCount { get; set; } = 10;

    /// <summary>
    /// Convergence: stop if Pareto front hasn't improved for this many generations.
    /// </summary>
    public int StagnationLimit { get; set; } = 100;

    /// <summary>
    /// Objective weights for composite fitness calculation.
    /// </summary>
    public ObjectiveWeights Weights { get; set; } = new();

    /// <summary>
    /// 5' UTR selection key (from UtrLibrary).
    /// </summary>
    public string FivePrimeUtrKey { get; set; } = "hba1";

    /// <summary>
    /// 3' UTR selection key (from UtrLibrary).
    /// </summary>
    public string ThreePrimeUtrKey { get; set; } = "hba1";

    /// <summary>
    /// Poly(A) tail length in nucleotides.
    /// </summary>
    public int PolyALength { get; set; } = 120;

    /// <summary>
    /// Whether to apply N1-methylpseudouridine substitution.
    /// </summary>
    public bool UseM1Pseudouridine { get; set; } = true;

    /// <summary>
    /// Target GC content range.
    /// </summary>
    public double GcTargetMin { get; set; } = 0.45;
    public double GcTargetMax { get; set; } = 0.55;

    /// <summary>
    /// Whether to use GPU acceleration (auto-detected if available).
    /// </summary>
    public bool UseGpuAcceleration { get; set; } = false;
}

public class ObjectiveWeights
{
    public double Cai { get; set; } = 1.0;
    public double GcContent { get; set; } = 0.8;
    public double CpgDepletion { get; set; } = 0.9;
    public double UridineReduction { get; set; } = 0.7;
    public double RareCodonAvoidance { get; set; } = 0.6;
    public double RepeatAvoidance { get; set; } = 0.5;
    public double FoldingEnergy { get; set; } = 0.4;
    public double CodonPairBias { get; set; } = 0.3;

    public double[] ToArray() => new[]
    {
        Cai, GcContent, CpgDepletion, UridineReduction,
        RareCodonAvoidance, RepeatAvoidance, FoldingEnergy, CodonPairBias,
    };
}
