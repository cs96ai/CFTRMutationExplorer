namespace CftrMutationExplorer.Core.Models.Mrna;

/// <summary>
/// Standard genetic code mapping RNA codons to amino acids,
/// with human codon usage frequencies from the Kazusa database (Homo sapiens, taxid 9606).
/// </summary>
public static class CodonTable
{
    public static readonly IReadOnlyDictionary<string, string> CodonToAminoAcid = new Dictionary<string, string>
    {
        ["UUU"] = "F", ["UUC"] = "F",
        ["UUA"] = "L", ["UUG"] = "L", ["CUU"] = "L", ["CUC"] = "L", ["CUA"] = "L", ["CUG"] = "L",
        ["AUU"] = "I", ["AUC"] = "I", ["AUA"] = "I",
        ["AUG"] = "M",
        ["GUU"] = "V", ["GUC"] = "V", ["GUA"] = "V", ["GUG"] = "V",
        ["UCU"] = "S", ["UCC"] = "S", ["UCA"] = "S", ["UCG"] = "S", ["AGU"] = "S", ["AGC"] = "S",
        ["CCU"] = "P", ["CCC"] = "P", ["CCA"] = "P", ["CCG"] = "P",
        ["ACU"] = "T", ["ACC"] = "T", ["ACA"] = "T", ["ACG"] = "T",
        ["GCU"] = "A", ["GCC"] = "A", ["GCA"] = "A", ["GCG"] = "A",
        ["UAU"] = "Y", ["UAC"] = "Y",
        ["CAU"] = "H", ["CAC"] = "H",
        ["CAA"] = "Q", ["CAG"] = "Q",
        ["AAU"] = "N", ["AAC"] = "N",
        ["AAA"] = "K", ["AAG"] = "K",
        ["GAU"] = "D", ["GAC"] = "D",
        ["GAA"] = "E", ["GAG"] = "E",
        ["UGU"] = "C", ["UGC"] = "C",
        ["UGG"] = "W",
        ["CGU"] = "R", ["CGC"] = "R", ["CGA"] = "R", ["CGG"] = "R", ["AGA"] = "R", ["AGG"] = "R",
        ["GGU"] = "G", ["GGC"] = "G", ["GGA"] = "G", ["GGG"] = "G",
        ["UAA"] = "*", ["UAG"] = "*", ["UGA"] = "*",
    };

    /// <summary>
    /// Human codon usage frequencies per 1000 codons (Kazusa DB, Homo sapiens).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> HumanFrequencyPerThousand = new Dictionary<string, double>
    {
        ["UUU"] = 17.6, ["UUC"] = 20.3,
        ["UUA"] = 7.7,  ["UUG"] = 12.9, ["CUU"] = 13.2, ["CUC"] = 19.6, ["CUA"] = 7.2, ["CUG"] = 39.6,
        ["AUU"] = 16.0, ["AUC"] = 20.8, ["AUA"] = 7.5,
        ["AUG"] = 22.0,
        ["GUU"] = 11.0, ["GUC"] = 14.5, ["GUA"] = 7.1,  ["GUG"] = 28.1,
        ["UCU"] = 15.2, ["UCC"] = 17.7, ["UCA"] = 12.2, ["UCG"] = 4.4, ["AGU"] = 12.1, ["AGC"] = 19.5,
        ["CCU"] = 17.5, ["CCC"] = 19.8, ["CCA"] = 16.9, ["CCG"] = 6.9,
        ["ACU"] = 13.1, ["ACC"] = 18.9, ["ACA"] = 15.1, ["ACG"] = 6.1,
        ["GCU"] = 18.4, ["GCC"] = 27.7, ["GCA"] = 15.8, ["GCG"] = 7.4,
        ["UAU"] = 12.2, ["UAC"] = 15.3,
        ["CAU"] = 10.9, ["CAC"] = 15.1,
        ["CAA"] = 12.3, ["CAG"] = 34.2,
        ["AAU"] = 17.0, ["AAC"] = 19.1,
        ["AAA"] = 24.4, ["AAG"] = 31.9,
        ["GAU"] = 21.8, ["GAC"] = 25.1,
        ["GAA"] = 29.0, ["GAG"] = 39.6,
        ["UGU"] = 10.6, ["UGC"] = 12.6,
        ["UGG"] = 13.2,
        ["CGU"] = 4.5,  ["CGC"] = 10.4, ["CGA"] = 6.2, ["CGG"] = 11.4, ["AGA"] = 12.2, ["AGG"] = 12.0,
        ["GGU"] = 10.8, ["GGC"] = 22.2, ["GGA"] = 16.5, ["GGG"] = 16.5,
        ["UAA"] = 1.0,  ["UAG"] = 0.8,  ["UGA"] = 1.6,
    };

    private static Dictionary<string, string[]>? _synonymousCodons;
    private static Dictionary<string, double>? _relativeAdaptiveness;
    private static Dictionary<string, string>? _bestCodonPerAa;

    /// <summary>
    /// Maps each amino acid (single letter) to its synonymous codons, sorted by human frequency (descending).
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> SynonymousCodons
    {
        get
        {
            if (_synonymousCodons != null) return _synonymousCodons;
            _synonymousCodons = CodonToAminoAcid
                .GroupBy(kv => kv.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(kv => kv.Key)
                          .OrderByDescending(c => HumanFrequencyPerThousand.GetValueOrDefault(c, 0))
                          .ToArray());
            return _synonymousCodons;
        }
    }

    /// <summary>
    /// Relative adaptiveness w(c) = freq(c) / max_freq_for_same_amino_acid.
    /// Used in CAI calculation. Range: (0, 1].
    /// </summary>
    public static IReadOnlyDictionary<string, double> RelativeAdaptiveness
    {
        get
        {
            if (_relativeAdaptiveness != null) return _relativeAdaptiveness;
            _relativeAdaptiveness = new Dictionary<string, double>();
            foreach (var group in CodonToAminoAcid.GroupBy(kv => kv.Value))
            {
                if (group.Key == "*") continue; // skip stop codons
                var maxFreq = group.Max(kv => HumanFrequencyPerThousand.GetValueOrDefault(kv.Key, 0.01));
                foreach (var kv in group)
                {
                    var freq = HumanFrequencyPerThousand.GetValueOrDefault(kv.Key, 0.01);
                    _relativeAdaptiveness[kv.Key] = freq / maxFreq;
                }
            }
            return _relativeAdaptiveness;
        }
    }

    /// <summary>
    /// The single most-preferred codon for each amino acid in human cells.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BestCodonPerAminoAcid
    {
        get
        {
            if (_bestCodonPerAa != null) return _bestCodonPerAa;
            _bestCodonPerAa = SynonymousCodons
                .Where(kv => kv.Key != "*")
                .ToDictionary(kv => kv.Key, kv => kv.Value[0]);
            return _bestCodonPerAa;
        }
    }

    public static string GetAminoAcid(string codon) =>
        CodonToAminoAcid.GetValueOrDefault(codon.ToUpperInvariant(), "?");

    public static string[] GetSynonymousCodons(string aminoAcid) =>
        SynonymousCodons.GetValueOrDefault(aminoAcid.ToUpperInvariant(), Array.Empty<string>());

    public static double GetRelativeAdaptiveness(string codon) =>
        RelativeAdaptiveness.GetValueOrDefault(codon.ToUpperInvariant(), 0.01);

    /// <summary>
    /// Convert a DNA sequence to RNA (T → U).
    /// </summary>
    public static string DnaToRna(string dna) =>
        dna.ToUpperInvariant().Replace('T', 'U');

    /// <summary>
    /// Convert an RNA sequence to DNA (U → T).
    /// </summary>
    public static string RnaToDna(string rna) =>
        rna.ToUpperInvariant().Replace('U', 'T');

    /// <summary>
    /// Translate an RNA coding sequence to a protein sequence.
    /// </summary>
    public static string Translate(string rnaCds)
    {
        var protein = new char[rnaCds.Length / 3];
        for (int i = 0; i < protein.Length; i++)
        {
            var codon = rnaCds.Substring(i * 3, 3).ToUpperInvariant();
            var aa = GetAminoAcid(codon);
            if (aa == "*") break;
            protein[i] = aa[0];
        }
        return new string(protein).TrimEnd('\0');
    }

    /// <summary>
    /// Back-translate a protein sequence using the most common human codon for each amino acid.
    /// </summary>
    public static string BackTranslateOptimal(string proteinSequence)
    {
        var rna = new char[proteinSequence.Length * 3];
        for (int i = 0; i < proteinSequence.Length; i++)
        {
            var aa = proteinSequence[i].ToString();
            var codon = BestCodonPerAminoAcid.GetValueOrDefault(aa, "NNN");
            rna[i * 3] = codon[0];
            rna[i * 3 + 1] = codon[1];
            rna[i * 3 + 2] = codon[2];
        }
        return new string(rna);
    }
}
