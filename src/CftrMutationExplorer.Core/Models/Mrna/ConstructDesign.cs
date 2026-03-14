namespace CftrMutationExplorer.Core.Models.Mrna;

/// <summary>
/// Complete mRNA construct design including all regions:
/// 5'Cap — 5'UTR — AUG — CDS — Stop — 3'UTR — Poly(A)
/// </summary>
public class ConstructDesign
{
    public string Name { get; set; } = "CFTR-mRNA-v1";
    public string Description { get; set; } = string.Empty;

    public string FivePrimeCapDescription { get; set; } = "CleanCap AG (Cap1, m7GpppAm2'OMe)";
    public string FivePrimeUtrName { get; set; } = string.Empty;
    public string FivePrimeUtrSequence { get; set; } = string.Empty;
    public string KozakSequence { get; set; } = "GCCACC"; // Kozak consensus before AUG
    public string CodingSequence { get; set; } = string.Empty;
    public string StopCodon { get; set; } = "UGA";
    public string ThreePrimeUtrName { get; set; } = string.Empty;
    public string ThreePrimeUtrSequence { get; set; } = string.Empty;
    public int PolyALength { get; set; } = 120;

    /// <summary>
    /// Nucleotide modification strategy applied to the construct.
    /// </summary>
    public ModificationStrategy Modifications { get; set; } = new();

    public string ProteinSequenceEncoded { get; set; } = string.Empty;
    public OptimizationScore? OptimizationScores { get; set; }

    /// <summary>
    /// Full construct sequence (5'UTR + Kozak + AUG + CDS + Stop + 3'UTR, excluding cap and poly(A)
    /// since those are added during synthesis, not encoded).
    /// </summary>
    public string FullSequence =>
        FivePrimeUtrSequence + KozakSequence + "AUG" + CodingSequence + StopCodon + ThreePrimeUtrSequence;

    public int TotalLength => FullSequence.Length + PolyALength;

    public double OverallGcContent
    {
        get
        {
            var seq = FullSequence;
            if (seq.Length == 0) return 0;
            int gc = seq.Count(c => c == 'G' || c == 'C');
            return (double)gc / seq.Length;
        }
    }

    /// <summary>
    /// Export the construct as FASTA format.
    /// </summary>
    public string ToFasta()
    {
        var polyA = new string('A', PolyALength);
        var fullSeq = FullSequence + polyA;
        var lines = new List<string>
        {
            $">{Name} | {ProteinSequenceEncoded.Length} aa | {TotalLength} nt | GC={OverallGcContent:P1}",
        };
        for (int i = 0; i < fullSeq.Length; i += 70)
            lines.Add(fullSeq.Substring(i, Math.Min(70, fullSeq.Length - i)));
        return string.Join("\n", lines);
    }
}

public class ModificationStrategy
{
    public bool N1MethylPseudouridine { get; set; } = true;
    public bool FiveMethylCytidine { get; set; } = false;
    public string CapType { get; set; } = "Cap1";
    public string Description => N1MethylPseudouridine
        ? "Complete U→m¹Ψ substitution (N1-methylpseudouridine)"
        : "Unmodified nucleotides";
}
