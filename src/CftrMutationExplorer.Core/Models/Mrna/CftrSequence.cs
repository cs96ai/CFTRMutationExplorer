namespace CftrMutationExplorer.Core.Models.Mrna;

/// <summary>
/// CFTR protein and mRNA sequence data.
/// Protein: UniProt P13569 (CFTR_HUMAN), 1480 amino acids.
/// Gene: CFTR (ABCC7), Chromosome 7q31.2.
/// mRNA: NCBI NM_000492.4, CDS = 4443 nucleotides.
/// </summary>
public static class CftrSequence
{
    /// <summary>
    /// CFTR wildtype protein sequence (UniProt P13569, 1480 amino acids).
    /// Position 508 is Phenylalanine (F) — deleted in the ΔF508 mutation.
    /// </summary>
    public const string ProteinSequence =
        "MQRSPLEKASVVSKLFFSWTRPILRKGYRQRLELSDIYQIPSVDSADNLS" + // 1-50
        "EKLEREWDRELASKKNPKLINALRRCFFWRFMFYGIFLYLGEVTKAVQPL" + // 51-100
        "LLGRIIASYDPDNKEERSIAIYLGIGLCLLFIVRTLLLHPAIFGLHHIGM" + // 101-150
        "QMRIAMFSLIYKKTLKLSSRVLDKISIGQLVSLLSNNLNKFDEGLALAHF" + // 151-200
        "VWIAPLQVALLMGLIWELLQASAFCGLGFLIVLALFQAGLGRMMMKYRDQ" + // 201-250
        "RAGKISERLVITSEMIENIQSVKAYCWEEAMEKMIENLRQTELKLTRKAA" +  // 251-299
        "YVRYFNSSAFFFSGFFVVFLSVLPYALIKGIILRKIFTTISFCIVLRMAV" + // 300-349
        "TRQFPWAVQTWYDSLGAINKIQDFLQKQEYKTLEYNLTTTEVVMENVTAF" + // 350-399
        "WEEGFGELFEKAKQNNNNRKTSNGDDSLFFSNFSLLGTPVLKDINFKIER" + // 400-449
        "GQLLAVAGSTGAGKTSLLMMIMGELEPSEGKIKHSGRISFCSQFSWIMPG" + // 450-499
        "TIKENIIFGVSYDEYRYRSVIKACQLEEDISKFAEKDNIVLGEGGITLSG" + // 500-549
        "GQRARISLARAVYKDADLYLLDSPFGYLDVLTEKEIFESCVCKLMANKTR" + // 550-599
        "ILVTSKMEHLKKADKILILHEGSSYFYGTFSELQNLQPDFSSKLMGCDS" + // 600-649
        "FDQFSAERRNSILTETLHRFSLEGDAPVSWTETKKQSFKQTGEFGEKRKN" + // 650-699
        "SILNPINSIRKFSIVQKTPLQMNGIEEDSDEPLERRLSLVPDSEQGEAIL" + // 700-749
        "PRISVISTGPTLQARRRQSVLNLMTHSVNQGQNIHRKTTASTRKVSLAP" + // 750-799
        "QANLTELDIYSRRLSQETGLEISEEINEEDLKECFFDDMESIPAVTTWNT" + // 800-849
        "YLRYITVHKSLIFVLIWCLVIFLAEVAASLVVLWLLGNTPLQDKGNSTHS" + // 850-899
        "RNNSYAVIITSTSSYYVFYIYVGVADTLLAMGFFRGLPLVHTLITVSKIL" + // 900-949
        "HHKMLHSVLQAPMSTLNTLKAGGILNRFSKDIAILDDLLPLTIFDFIQLL" + // 950-1000
        "LIVIGAIAVVAVLQPYIFVATVPVIVAFIMLRAYFLQTSQQLKQLESEGR" + // 1001-1050
        "SPIFTHLVTSLKGLWTLRAFGRQPYFETLFHKALNLHTANWFLYLSTLRW" + // 1051-1100
        "FQMRIEMIFVIFFIAVTFISILTTGEGEGRVGIILTLAMNIMSTLQWAVN" +  // 1101-1149
        "SSIDVDSLMRSVSRVFKFIDMPTEGKPTKSTKPYKNGQLSKVMIIENSHV" + // 1150-1199
        "KKDDIWPSGGQMTVKDLTAKYTEGGNAILENISFSISPGQRVGLLGRTGS" + // 1200-1249
        "GKSTLLSAFLRLLNTEGEIQIDGVSWDSITLQQWRKAFGVIPQKVFIFSG" + // 1250-1299
        "TFRKNLDPYEQWSDQEIWKVADEVGLRSVIEQFPGKLDFVLVDGGCVLS" +  // 1300-1349
        "HGHKQLMCLARSVLSKAKILLLDEPSAHLDPVTYQIIRRTLKQAFADCTV" + // 1350-1399
        "ILCEHRIEAMLECQQFLVIEENKVRQYDSIQKLLNERSLFRQAISPSDRVK" + // 1400-1449
        "LFPHRNSSKCKSKPQIAALKEETEEEVQDTRL";                     // 1450-1480

    public static int ProteinLength => ProteinSequence.Length;

    public static int CdsLengthNucleotides => ProteinSequence.Length * 3; // 4440 (excl. stop)

    /// <summary>
    /// CFTR protein domain boundaries (approximate, based on published structural data).
    /// </summary>
    public static readonly IReadOnlyList<ProteinDomain> Domains = new List<ProteinDomain>
    {
        new("TMD1", "Transmembrane Domain 1", 1, 390),
        new("NBD1", "Nucleotide-Binding Domain 1", 391, 655),
        new("R", "Regulatory Domain", 656, 836),
        new("TMD2", "Transmembrane Domain 2", 837, 1172),
        new("NBD2", "Nucleotide-Binding Domain 2", 1173, 1480),
    };

    /// <summary>
    /// Key mutation sites in CFTR.
    /// </summary>
    public static readonly IReadOnlyList<MutationSite> KnownMutations = new List<MutationSite>
    {
        new("ΔF508", 508, "F", "-", "NBD1", "Most common CF mutation (~70% of alleles). " +
            "Deletion of phenylalanine at position 508 causes protein misfolding."),
        new("G551D", 551, "G", "D", "NBD1", "Gating mutation. Protein reaches surface but channel doesn't open properly."),
        new("G542X", 542, "G", "*", "NBD1", "Nonsense mutation. Premature stop codon."),
        new("N1303K", 1303, "N", "K", "NBD2", "Missense mutation affecting NBD2 function."),
        new("W1282X", 1282, "W", "*", "NBD2", "Nonsense mutation. Premature stop codon."),
        new("R117H", 117, "R", "H", "TMD1", "Mild mutation. Reduced chloride conductance."),
    };

    /// <summary>
    /// Get the amino acid at a 1-based position.
    /// </summary>
    public static char GetAminoAcid(int position) =>
        position >= 1 && position <= ProteinLength ? ProteinSequence[position - 1] : '?';

    /// <summary>
    /// Get the domain containing a given amino acid position.
    /// </summary>
    public static ProteinDomain? GetDomain(int position) =>
        Domains.FirstOrDefault(d => position >= d.Start && position <= d.End);
}

public record ProteinDomain(string Name, string FullName, int Start, int End)
{
    public int Length => End - Start + 1;
}

public record MutationSite(
    string Name, int Position, string WildtypeAa, string MutantAa,
    string Domain, string Description);
