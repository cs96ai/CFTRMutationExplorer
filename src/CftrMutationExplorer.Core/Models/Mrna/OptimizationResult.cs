namespace CftrMutationExplorer.Core.Models.Mrna;

/// <summary>
/// Final result from the mRNA optimization pipeline.
/// </summary>
public class OptimizationResult
{
    public List<MrnaCandidate> ParetoFront { get; set; } = new();
    public MrnaCandidate? BestOverall { get; set; }
    public int GenerationsRun { get; set; }
    public int TotalSequencesEvaluated { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public bool ConvergedEarly { get; set; }
    public List<GenerationStats> History { get; set; } = new();
    public OptimizationConfig Config { get; set; } = new();
    public string ProteinSequence { get; set; } = string.Empty;
}

/// <summary>
/// Per-generation statistics for tracking optimization progress.
/// </summary>
public class GenerationStats
{
    public int Generation { get; set; }
    public double BestCompositeFitness { get; set; }
    public double AverageCompositeFitness { get; set; }
    public double WorstCompositeFitness { get; set; }
    public int ParetoFrontSize { get; set; }
    public double PopulationDiversity { get; set; }
    public double BestCai { get; set; }
    public double BestGcScore { get; set; }
    public double BestCpgScore { get; set; }
    public double BestUridineScore { get; set; }
    public TimeSpan Elapsed { get; set; }
}

/// <summary>
/// Progress data emitted during optimization for UI updates.
/// </summary>
public class OptimizationProgress
{
    public int CurrentGeneration { get; set; }
    public int MaxGenerations { get; set; }
    public double ProgressPercent => MaxGenerations > 0
        ? (double)CurrentGeneration / MaxGenerations * 100.0
        : 0;
    public GenerationStats CurrentStats { get; set; } = new();
    public int SequencesPerSecond { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}
