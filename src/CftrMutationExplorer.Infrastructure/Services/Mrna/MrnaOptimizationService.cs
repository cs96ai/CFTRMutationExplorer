using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models.Mrna;

namespace CftrMutationExplorer.Infrastructure.Services.Mrna;

public class MrnaOptimizationService : IMrnaOptimizationService
{
    private readonly ICodonScoringService _scoring;
    private readonly IRnaFoldingService _folding;

    public MrnaOptimizationService(ICodonScoringService scoring, IRnaFoldingService folding)
    {
        _scoring = scoring;
        _folding = folding;
    }

    public async Task<OptimizationResult> OptimizeAsync(
        string proteinSequence,
        OptimizationConfig config,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var optimizer = new NsgaIIOptimizer(_scoring, _folding);
        return await Task.Run(
            () => optimizer.RunAsync(proteinSequence, config, progress, cancellationToken),
            cancellationToken);
    }

    public ConstructDesign AssembleConstruct(MrnaCandidate candidate, string proteinSequence, OptimizationConfig config)
    {
        var cds = candidate.ToRnaSequence(proteinSequence);
        var fiveUtr = UtrLibrary.GetFivePrimeUtr(config.FivePrimeUtrKey);
        var threeUtr = UtrLibrary.GetThreePrimeUtr(config.ThreePrimeUtrKey);

        return new ConstructDesign
        {
            Name = $"CFTR-mRNA-optimized",
            Description = $"Codon-optimized CFTR mRNA construct. " +
                          $"CAI={candidate.Score.Cai:F3}, GC={candidate.Score.GcContentScore:F3}, " +
                          $"CpG={candidate.Score.CpgScore:F3}",
            FivePrimeCapDescription = "CleanCap AG (Cap1, m7GpppAm2'OMe)",
            FivePrimeUtrName = fiveUtr.Name,
            FivePrimeUtrSequence = fiveUtr.Sequence,
            KozakSequence = "GCCACC",
            CodingSequence = cds,
            StopCodon = "UGA",
            ThreePrimeUtrName = threeUtr.Name,
            ThreePrimeUtrSequence = threeUtr.Sequence,
            PolyALength = config.PolyALength,
            Modifications = new ModificationStrategy
            {
                N1MethylPseudouridine = config.UseM1Pseudouridine,
                CapType = "Cap1",
            },
            ProteinSequenceEncoded = proteinSequence,
            OptimizationScores = candidate.Score,
        };
    }

    public OptimizationScore ScoreNativeSequence(string rnaCds, ObjectiveWeights weights)
    {
        var score = new OptimizationScore
        {
            Cai = _scoring.CalculateCai(rnaCds),
            GcContentScore = _scoring.CalculateGcContentScore(rnaCds),
            CpgScore = _scoring.CalculateCpgScore(rnaCds),
            UridineScore = _scoring.CalculateUridineScore(rnaCds),
            RareCodonScore = _scoring.CalculateRareCodonScore(rnaCds),
            RepeatScore = _scoring.CalculateRepeatScore(rnaCds),
            CodonPairScore = _scoring.CalculateCodonPairScore(rnaCds),
            FoldingScore = _folding.ScoreFivePrimeFolding(rnaCds),
        };

        var w = weights.ToArray();
        var obj = score.ToObjectiveArray();
        double weightedSum = 0, totalWeight = 0;
        for (int i = 0; i < obj.Length; i++)
        {
            weightedSum += obj[i] * w[i];
            totalWeight += w[i];
        }
        score.CompositeFitness = totalWeight > 0 ? weightedSum / totalWeight : 0;

        return score;
    }

    public string GetCftrProteinSequence() => CftrSequence.ProteinSequence;
}
