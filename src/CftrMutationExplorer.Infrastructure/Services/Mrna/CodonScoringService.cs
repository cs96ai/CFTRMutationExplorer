using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models.Mrna;

namespace CftrMutationExplorer.Infrastructure.Services.Mrna;

public class CodonScoringService : ICodonScoringService
{
    public OptimizationScore ScoreCandidate(MrnaCandidate candidate, string proteinSequence, ObjectiveWeights weights)
    {
        var rna = candidate.ToRnaSequence(proteinSequence);
        var score = new OptimizationScore
        {
            Cai = CalculateCai(rna),
            GcContentScore = CalculateGcContentScore(rna),
            CpgScore = CalculateCpgScore(rna),
            UridineScore = CalculateUridineScore(rna),
            RareCodonScore = CalculateRareCodonScore(rna),
            RepeatScore = CalculateRepeatScore(rna),
            CodonPairScore = CalculateCodonPairScore(rna),
            FoldingScore = 0.5, // placeholder until RNA folding is scored
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

        candidate.Score = score;
        return score;
    }

    public void ScoreBatch(IList<MrnaCandidate> candidates, string proteinSequence, ObjectiveWeights weights)
    {
        Parallel.ForEach(candidates, candidate =>
        {
            ScoreCandidate(candidate, proteinSequence, weights);
        });
    }

    /// <summary>
    /// Codon Adaptation Index: geometric mean of relative adaptiveness across all codons.
    /// CAI = exp((1/L) * Σ ln(w_i)) where w_i = freq(codon_i) / max_freq(synonymous).
    /// </summary>
    public double CalculateCai(string rnaSequence)
    {
        int codonCount = rnaSequence.Length / 3;
        if (codonCount == 0) return 0;

        double sumLogW = 0;
        int counted = 0;

        for (int i = 0; i < codonCount; i++)
        {
            var codon = rnaSequence.Substring(i * 3, 3);
            var aa = CodonTable.GetAminoAcid(codon);
            if (aa == "*" || aa == "M" || aa == "W" || aa == "?") continue; // skip: no synonymous choices

            var w = CodonTable.GetRelativeAdaptiveness(codon);
            if (w > 0)
            {
                sumLogW += Math.Log(w);
                counted++;
            }
        }

        return counted > 0 ? Math.Exp(sumLogW / counted) : 0;
    }

    /// <summary>
    /// Score GC content. Optimal range is typically 45-55% for mRNA therapeutics.
    /// Returns [0, 1] where 1 = perfectly in range.
    /// Also penalizes local windows outside 40-65%.
    /// </summary>
    public double CalculateGcContentScore(string rnaSequence, double targetMin = 0.45, double targetMax = 0.55)
    {
        if (rnaSequence.Length == 0) return 0;

        int gc = 0;
        for (int i = 0; i < rnaSequence.Length; i++)
            if (rnaSequence[i] == 'G' || rnaSequence[i] == 'C') gc++;

        double overallGc = (double)gc / rnaSequence.Length;

        double overallScore;
        if (overallGc >= targetMin && overallGc <= targetMax)
            overallScore = 1.0;
        else if (overallGc < targetMin)
            overallScore = Math.Max(0, 1.0 - (targetMin - overallGc) * 5.0);
        else
            overallScore = Math.Max(0, 1.0 - (overallGc - targetMax) * 5.0);

        // Sliding window penalty for extreme local regions
        const int windowSize = 50;
        double windowPenalty = 0;
        int windowCount = 0;
        for (int start = 0; start + windowSize <= rnaSequence.Length; start += 25)
        {
            int localGc = 0;
            for (int j = start; j < start + windowSize; j++)
                if (rnaSequence[j] == 'G' || rnaSequence[j] == 'C') localGc++;

            double localGcFrac = (double)localGc / windowSize;
            if (localGcFrac < 0.30 || localGcFrac > 0.70)
                windowPenalty += 1.0;
            windowCount++;
        }

        double windowScore = windowCount > 0 ? Math.Max(0, 1.0 - windowPenalty / windowCount * 2.0) : 1.0;

        return overallScore * 0.6 + windowScore * 0.4;
    }

    /// <summary>
    /// CpG depletion score. Human genome is CpG-depleted; high CpG triggers TLR9.
    /// Returns [0, 1] where 1 = well-depleted (low CpG).
    /// </summary>
    public double CalculateCpgScore(string rnaSequence)
    {
        if (rnaSequence.Length < 2) return 1.0;

        int cpgCount = 0;
        int cCount = 0, gCount = 0;

        for (int i = 0; i < rnaSequence.Length; i++)
        {
            if (rnaSequence[i] == 'C')
            {
                cCount++;
                if (i + 1 < rnaSequence.Length && rnaSequence[i + 1] == 'G')
                    cpgCount++;
            }
            else if (rnaSequence[i] == 'G')
            {
                gCount++;
            }
        }

        // Expected CpG = (C_count * G_count) / length
        double expectedCpg = rnaSequence.Length > 0 ? (double)(cCount * gCount) / rnaSequence.Length : 1;
        double observedExpectedRatio = expectedCpg > 0 ? cpgCount / expectedCpg : 0;

        // Human genome CpG O/E ratio is ~0.2-0.4. We want low ratios.
        // Score: 1.0 if ratio < 0.4, linearly decreasing to 0 at ratio 1.0
        if (observedExpectedRatio <= 0.4)
            return 1.0;
        if (observedExpectedRatio >= 1.0)
            return 0.0;

        return 1.0 - (observedExpectedRatio - 0.4) / 0.6;
    }

    /// <summary>
    /// Uridine content score. Fewer uridines = less innate immune activation.
    /// Even with m1Ψ modification, lower uridine content is preferred.
    /// Returns [0, 1] where 1 = low uridine.
    /// </summary>
    public double CalculateUridineScore(string rnaSequence)
    {
        if (rnaSequence.Length == 0) return 1.0;

        int uCount = 0;
        for (int i = 0; i < rnaSequence.Length; i++)
            if (rnaSequence[i] == 'U') uCount++;

        double uFraction = (double)uCount / rnaSequence.Length;

        // Typical range: 0.15-0.30. We want lower.
        // Score: 1.0 at 0.15 or below, 0.0 at 0.35 or above
        if (uFraction <= 0.15) return 1.0;
        if (uFraction >= 0.35) return 0.0;

        return 1.0 - (uFraction - 0.15) / 0.20;
    }

    /// <summary>
    /// Rare codon cluster avoidance. Clusters of rare codons cause ribosome stalling.
    /// Returns [0, 1] where 1 = no rare clusters.
    /// </summary>
    public double CalculateRareCodonScore(string rnaSequence)
    {
        int codonCount = rnaSequence.Length / 3;
        if (codonCount == 0) return 1.0;

        const double rareThreshold = 0.3;
        int clusterCount = 0;
        int currentRun = 0;

        for (int i = 0; i < codonCount; i++)
        {
            var codon = rnaSequence.Substring(i * 3, 3);
            var w = CodonTable.GetRelativeAdaptiveness(codon);

            if (w < rareThreshold)
            {
                currentRun++;
                if (currentRun >= 3)
                    clusterCount++;
            }
            else
            {
                currentRun = 0;
            }
        }

        // Penalty: each cluster reduces score
        double penalty = clusterCount * 0.1;
        return Math.Max(0, 1.0 - penalty);
    }

    /// <summary>
    /// Repeat sequence avoidance. Homopolymers and dinucleotide repeats cause synthesis issues.
    /// Returns [0, 1] where 1 = no problematic repeats.
    /// </summary>
    public double CalculateRepeatScore(string rnaSequence)
    {
        if (rnaSequence.Length < 6) return 1.0;

        int violations = 0;

        // Check homopolymer runs (≥6 identical nucleotides)
        int run = 1;
        for (int i = 1; i < rnaSequence.Length; i++)
        {
            if (rnaSequence[i] == rnaSequence[i - 1])
            {
                run++;
                if (run >= 6) violations++;
            }
            else
            {
                run = 1;
            }
        }

        // Check dinucleotide repeats (≥8 nt of XY pattern)
        for (int i = 0; i + 7 < rnaSequence.Length; i++)
        {
            bool isDiRepeat = true;
            char a = rnaSequence[i], b = rnaSequence[i + 1];
            if (a == b) continue;
            for (int j = 2; j < 8; j++)
            {
                if (rnaSequence[i + j] != (j % 2 == 0 ? a : b))
                {
                    isDiRepeat = false;
                    break;
                }
            }
            if (isDiRepeat) violations++;
        }

        double penalty = violations * 0.05;
        return Math.Max(0, 1.0 - penalty);
    }

    /// <summary>
    /// Codon pair bias score. Some adjacent codon pairs are under-represented in human genes
    /// and correlate with reduced expression.
    /// Simplified version: penalize codon pairs that create CpG or UpA dinucleotides at the junction.
    /// </summary>
    public double CalculateCodonPairScore(string rnaSequence)
    {
        int codonCount = rnaSequence.Length / 3;
        if (codonCount < 2) return 1.0;

        int badJunctions = 0;

        for (int i = 0; i < codonCount - 1; i++)
        {
            int pos = i * 3;
            // Junction between codon i and codon i+1 is at positions pos+2 and pos+3
            char lastOfCurrent = rnaSequence[pos + 2];
            char firstOfNext = rnaSequence[pos + 3];

            // CpG at junction is immunogenic
            if (lastOfCurrent == 'C' && firstOfNext == 'G')
                badJunctions++;

            // UpA at junction promotes mRNA degradation
            if (lastOfCurrent == 'U' && firstOfNext == 'A')
                badJunctions++;
        }

        double badFraction = (double)badJunctions / (codonCount - 1);
        return Math.Max(0, 1.0 - badFraction * 3.0);
    }

    public List<CodonPositionAnalysis> AnalyzeByPosition(string rnaSequence)
    {
        var result = new List<CodonPositionAnalysis>();
        int codonCount = rnaSequence.Length / 3;

        for (int i = 0; i < codonCount; i++)
        {
            var codon = rnaSequence.Substring(i * 3, 3);
            var aa = CodonTable.GetAminoAcid(codon);
            var w = CodonTable.GetRelativeAdaptiveness(codon);

            int windowStart = Math.Max(0, i * 3 - 25);
            int windowEnd = Math.Min(rnaSequence.Length, i * 3 + 28);
            int windowLen = windowEnd - windowStart;
            int gc = 0;
            for (int j = windowStart; j < windowEnd; j++)
                if (rnaSequence[j] == 'G' || rnaSequence[j] == 'C') gc++;

            bool cpgContext = false;
            int pos = i * 3;
            if (pos + 3 < rnaSequence.Length && rnaSequence[pos + 2] == 'C' && rnaSequence[pos + 3] == 'G')
                cpgContext = true;
            if (pos > 0 && rnaSequence[pos - 1] == 'C' && rnaSequence[pos] == 'G')
                cpgContext = true;

            result.Add(new CodonPositionAnalysis
            {
                Position = i + 1,
                Codon = codon,
                AminoAcid = aa,
                RelativeAdaptiveness = w,
                LocalGcContent = windowLen > 0 ? (double)gc / windowLen : 0,
                IsRareCodon = w < 0.3,
                IsInCpgContext = cpgContext,
            });
        }

        return result;
    }
}
