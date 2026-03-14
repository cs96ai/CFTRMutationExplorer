namespace CftrMutationExplorer.Core.Models.Mrna;

/// <summary>
/// Library of known 5' and 3' UTR sequences used in mRNA therapeutics.
/// </summary>
public static class UtrLibrary
{
    public static readonly IReadOnlyDictionary<string, UtrEntry> FivePrimeUtrs =
        new Dictionary<string, UtrEntry>
        {
            ["hba1"] = new(
                "HBA1 (α-globin)",
                "ACUUCUUGGUGAACAAUUUGAACCUGAAACAGAGAGAAUAGCUAGUUAUUCAGAGGGAAAGCUGAGUUUUGAAUACUGGCUACAAUGUAGGC",
                "Human α-globin 5'UTR. Widely used in mRNA therapeutics for high translation efficiency."),

            ["hbb"] = new(
                "HBB (β-globin)",
                "ACAUUUGCUUCUCAGUCGUUUAGAGAACAGGCCACCUUUGAAAGAGAGAAUGGCCAUGCUCUUUGAAACCCAGGAAGCUGUAGAU",
                "Human β-globin 5'UTR. Well-characterized, moderate expression."),

            ["minimal"] = new(
                "Minimal Kozak",
                "AGAGCC",
                "Minimal 5'UTR with just a short leader before the Kozak sequence."),

            ["tev"] = new(
                "TEV Leader",
                "GAAUUUUACAACUUACUAAUAUACCAAGAAAGCUUAUAUCCAAACCAUUUCCUAUCCAUAUAUAUCCAAA",
                "Tobacco Etch Virus 5' leader. High translation initiation in cap-independent manner."),

            ["optimized1"] = new(
                "Optimized Synthetic v1",
                "GGAAAUAAGAGAGAAAAGAAGAGUAAGAAGAAAUAUAAGAGCCACC",
                "Synthetic optimized 5'UTR. Low structure, strong Kozak context."),
        };

    public static readonly IReadOnlyDictionary<string, UtrEntry> ThreePrimeUtrs =
        new Dictionary<string, UtrEntry>
        {
            ["hba1"] = new(
                "HBA1 (α-globin)",
                "GCUGCCUUCUGCGGGGCUUGCCUUCUGGCCAUGCCCUUCUUCUCUCCCUUGCACCUGUACCUCUUGGUCUUUGAAUAAAGCCUGAAUAGGCCGAACUAC",
                "Human α-globin 3'UTR. Standard for mRNA therapeutics. Contains stability elements."),

            ["hbb"] = new(
                "HBB (β-globin)",
                "GCUAAUAAAUGGGGAAAUUUAUUUUAUAGAAUGCAUAAAGUAUAAGCUUUGCAUACAAAGUAUUUGACUAAUUUUUUAUUUAUUUUAUUUUUAUU",
                "Human β-globin 3'UTR. Contains AU-rich stability elements."),

            ["aes_mtrna1"] = new(
                "AES-mtRNR1 Tandem",
                "CUAGCAAUAAACAAGUUAACAACAACAAUUGCAUUCAUUUUAUGUUUCAGGUU" +
                "CAGGGGGGAUGGUGGAAUUCCCUCUAGAUGCCAGCAUAGUCCAGGAUGAGCCC" +
                "UAGUAUCGCUAUGUUAUCCAGACCGCUGGAGCCCCGCGUAAAAUGAUCGUAGAUUUAUUUAGGG",
                "AES-mtRNR1 tandem 3'UTR. Used in BioNTech mRNA constructs. Enhanced stability and translation."),

            ["minimal"] = new(
                "Minimal poly(A) signal",
                "AAUAAA",
                "Minimal 3'UTR with just the polyadenylation signal."),
        };

    public static UtrEntry GetFivePrimeUtr(string key) =>
        FivePrimeUtrs.GetValueOrDefault(key, FivePrimeUtrs["hba1"]);

    public static UtrEntry GetThreePrimeUtr(string key) =>
        ThreePrimeUtrs.GetValueOrDefault(key, ThreePrimeUtrs["hba1"]);
}

public record UtrEntry(string Name, string Sequence, string Description)
{
    public int Length => Sequence.Length;
    public double GcContent
    {
        get
        {
            if (Sequence.Length == 0) return 0;
            int gc = Sequence.Count(c => c == 'G' || c == 'C');
            return (double)gc / Sequence.Length;
        }
    }
}
