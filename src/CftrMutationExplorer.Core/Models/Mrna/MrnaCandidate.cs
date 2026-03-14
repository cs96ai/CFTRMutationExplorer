namespace CftrMutationExplorer.Core.Models.Mrna;

/// <summary>
/// A candidate mRNA sequence in the optimization population.
/// The Codons array stores the codon index (into SynonymousCodons) for each amino acid position.
/// </summary>
public class MrnaCandidate
{
    /// <summary>
    /// Codon choices: for each amino acid position i, Codons[i] is the index into
    /// CodonTable.SynonymousCodons[aminoAcid] for the codon used at that position.
    /// </summary>
    public byte[] Codons { get; set; }

    public OptimizationScore Score { get; set; } = new();

    public int Rank { get; set; }
    public double CrowdingDistance { get; set; }

    public MrnaCandidate(int proteinLength)
    {
        Codons = new byte[proteinLength];
    }

    public MrnaCandidate(byte[] codons)
    {
        Codons = codons;
    }

    /// <summary>
    /// Build the RNA coding sequence from codon choices and a protein sequence.
    /// </summary>
    public string ToRnaSequence(string proteinSequence)
    {
        var rna = new char[proteinSequence.Length * 3];
        for (int i = 0; i < proteinSequence.Length; i++)
        {
            var aa = proteinSequence[i].ToString();
            var synonymous = CodonTable.GetSynonymousCodons(aa);
            var idx = Codons[i] % synonymous.Length;
            var codon = synonymous[idx];
            rna[i * 3] = codon[0];
            rna[i * 3 + 1] = codon[1];
            rna[i * 3 + 2] = codon[2];
        }
        return new string(rna);
    }

    public MrnaCandidate Clone()
    {
        return new MrnaCandidate((byte[])Codons.Clone())
        {
            Score = Score.Clone(),
            Rank = Rank,
            CrowdingDistance = CrowdingDistance,
        };
    }

    /// <summary>
    /// Check if this candidate dominates another (all objectives at least as good, one strictly better).
    /// </summary>
    public bool Dominates(MrnaCandidate other)
    {
        return Score.Dominates(other.Score);
    }
}

/// <summary>
/// Multi-objective fitness scores for an mRNA candidate.
/// All scores are normalized to [0, 1] where HIGHER = BETTER.
/// </summary>
public class OptimizationScore
{
    public double Cai { get; set; }
    public double GcContentScore { get; set; }
    public double CpgScore { get; set; }
    public double UridineScore { get; set; }
    public double RareCodonScore { get; set; }
    public double RepeatScore { get; set; }
    public double FoldingScore { get; set; }
    public double CodonPairScore { get; set; }

    /// <summary>Weighted composite fitness (higher = better).</summary>
    public double CompositeFitness { get; set; }

    public double[] ToObjectiveArray() => new[]
    {
        Cai, GcContentScore, CpgScore, UridineScore,
        RareCodonScore, RepeatScore, FoldingScore, CodonPairScore,
    };

    public bool Dominates(OptimizationScore other)
    {
        var a = ToObjectiveArray();
        var b = other.ToObjectiveArray();
        bool atLeastOneBetter = false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] < b[i]) return false;
            if (a[i] > b[i]) atLeastOneBetter = true;
        }
        return atLeastOneBetter;
    }

    public OptimizationScore Clone() => (OptimizationScore)MemberwiseClone();
}
