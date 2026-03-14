"""
Codon tables, CFTR protein sequence, and human codon usage data.
All data needed for mRNA codon optimization.
"""

CODON_TO_AA = {
    "UUU": "F", "UUC": "F",
    "UUA": "L", "UUG": "L", "CUU": "L", "CUC": "L", "CUA": "L", "CUG": "L",
    "AUU": "I", "AUC": "I", "AUA": "I",
    "AUG": "M",
    "GUU": "V", "GUC": "V", "GUA": "V", "GUG": "V",
    "UCU": "S", "UCC": "S", "UCA": "S", "UCG": "S", "AGU": "S", "AGC": "S",
    "CCU": "P", "CCC": "P", "CCA": "P", "CCG": "P",
    "ACU": "T", "ACC": "T", "ACA": "T", "ACG": "T",
    "GCU": "A", "GCC": "A", "GCA": "A", "GCG": "A",
    "UAU": "Y", "UAC": "Y",
    "CAU": "H", "CAC": "H",
    "CAA": "Q", "CAG": "Q",
    "AAU": "N", "AAC": "N",
    "AAA": "K", "AAG": "K",
    "GAU": "D", "GAC": "D",
    "GAA": "E", "GAG": "E",
    "UGU": "C", "UGC": "C",
    "UGG": "W",
    "CGU": "R", "CGC": "R", "CGA": "R", "CGG": "R", "AGA": "R", "AGG": "R",
    "GGU": "G", "GGC": "G", "GGA": "G", "GGG": "G",
    "UAA": "*", "UAG": "*", "UGA": "*",
}

# Human codon usage (per 1000 codons, Kazusa DB, Homo sapiens taxid 9606)
HUMAN_FREQ = {
    "UUU": 17.6, "UUC": 20.3,
    "UUA": 7.7, "UUG": 12.9, "CUU": 13.2, "CUC": 19.6, "CUA": 7.2, "CUG": 39.6,
    "AUU": 16.0, "AUC": 20.8, "AUA": 7.5,
    "AUG": 22.0,
    "GUU": 11.0, "GUC": 14.5, "GUA": 7.1, "GUG": 28.1,
    "UCU": 15.2, "UCC": 17.7, "UCA": 12.2, "UCG": 4.4, "AGU": 12.1, "AGC": 19.5,
    "CCU": 17.5, "CCC": 19.8, "CCA": 16.9, "CCG": 6.9,
    "ACU": 13.1, "ACC": 18.9, "ACA": 15.1, "ACG": 6.1,
    "GCU": 18.4, "GCC": 27.7, "GCA": 15.8, "GCG": 7.4,
    "UAU": 12.2, "UAC": 15.3,
    "CAU": 10.9, "CAC": 15.1,
    "CAA": 12.3, "CAG": 34.2,
    "AAU": 17.0, "AAC": 19.1,
    "AAA": 24.4, "AAG": 31.9,
    "GAU": 21.8, "GAC": 25.1,
    "GAA": 29.0, "GAG": 39.6,
    "UGU": 10.6, "UGC": 12.6,
    "UGG": 13.2,
    "CGU": 4.5, "CGC": 10.4, "CGA": 6.2, "CGG": 11.4, "AGA": 12.2, "AGG": 12.0,
    "GGU": 10.8, "GGC": 22.2, "GGA": 16.5, "GGG": 16.5,
    "UAA": 1.0, "UAG": 0.8, "UGA": 1.6,
}

# Nucleotide encoding: A=0, U=1, G=2, C=3
NUC_TO_INT = {"A": 0, "U": 1, "G": 2, "C": 3}
INT_TO_NUC = {0: "A", 1: "U", 2: "G", 3: "C"}

# CFTR protein sequence (UniProt P13569, 1480 amino acids)
CFTR_PROTEIN = (
    "MQRSPLEKASVVSKLFFSWTRPILRKGYRQRLELSDIYQIPSVDSADNLS"
    "EKLEREWDRELASKKNPKLINALRRCFFWRFMFYGIFLYLGEVTKAVQPL"
    "LLGRIIASYDPDNKEERSIAIYLGIGLCLLFIVRTLLLHPAIFGLHHIGM"
    "QMRIAMFSLIYKKTLKLSSRVLDKISIGQLVSLLSNNLNKFDEGLALAHF"
    "VWIAPLQVALLMGLIWELLQASAFCGLGFLIVLALFQAGLGRMMMKYRDQ"
    "RAGKISERLVITSEMIENIQSVKAYCWEEAMEKMIENLRQTELKLTRKAA"
    "YVRYFNSSAFFFSGFFVVFLSVLPYALIKGIILRKIFTTISFCIVLRMAV"
    "TRQFPWAVQTWYDSLGAINKIQDFLQKQEYKTLEYNLTTTEVVMENVTAF"
    "WEEGFGELFEKAKQNNNNRKTSNGDDSLFFSNFSLLGTPVLKDINFKIER"
    "GQLLAVAGSTGAGKTSLLMMIMGELEPSEGKIKHSGRISFCSQFSWIMPG"
    "TIKENIIFGVSYDEYRYRSVIKACQLEEDISKFAEKDNIVLGEGGITLSG"
    "GQRARISLARAVYKDADLYLLDSPFGYLDVLTEKEIFESCVCKLMANKTR"
    "ILVTSKMEHLKKADKILILHEGSSYFYGTFSELQNLQPDFSSKLMGCDS"
    "FDQFSAERRNSILTETLHRFSLEGDAPVSWTETKKQSFKQTGEFGEKRKN"
    "SILNPINSIRKFSIVQKTPLQMNGIEEDSDEPLERRLSLVPDSEQGEAIL"
    "PRISVISTGPTLQARRRQSVLNLMTHSVNQGQNIHRKTTASTRKVSLAP"
    "QANLTELDIYSRRLSQETGLEISEEINEEDLKECFFDDMESIPAVTTWNT"
    "YLRYITVHKSLIFVLIWCLVIFLAEVAASLVVLWLLGNTPLQDKGNSTHS"
    "RNNSYAVIITSTSSYYVFYIYVGVADTLLAMGFFRGLPLVHTLITVSKIL"
    "HHKMLHSVLQAPMSTLNTLKAGGILNRFSKDIAILDDLLPLTIFDFIQLL"
    "LIVIGAIAVVAVLQPYIFVATVPVIVAFIMLRAYFLQTSQQLKQLESEGR"
    "SPIFTHLVTSLKGLWTLRAFGRQPYFETLFHKALNLHTANWFLYLSTLRW"
    "FQMRIEMIFVIFFIAVTFISILTTGEGEGRVGIILTLAMNIMSTLQWAVN"
    "SSIDVDSLMRSVSRVFKFIDMPTEGKPTKSTKPYKNGQLSKVMIIENSHV"
    "KKDDIWPSGGQMTVKDLTAKYTEGGNAILENISFSISPGQRVGLLGRTGS"
    "GKSTLLSAFLRLLNTEGEIQIDGVSWDSITLQQWRKAFGVIPQKVFIFSG"
    "TFRKNLDPYEQWSDQEIWKVADEVGLRSVIEQFPGKLDFVLVDGGCVLS"
    "HGHKQLMCLARSVLSKAKILLLDEPSAHLDPVTYQIIRRTLKQAFADCTV"
    "ILCEHRIEAMLECQQFLVIEENKVRQYDSIQKLLNERSLFRQAISPSDRVK"
    "LFPHRNSSKCKSKPQIAALKEETEEEVQDTRL"
)


def build_synonymous_codons():
    """Build amino acid -> sorted synonymous codons (by human frequency, descending)."""
    from collections import defaultdict
    aa_codons = defaultdict(list)
    for codon, aa in CODON_TO_AA.items():
        if aa != "*":
            aa_codons[aa].append(codon)
    for aa in aa_codons:
        aa_codons[aa].sort(key=lambda c: HUMAN_FREQ.get(c, 0), reverse=True)
    return dict(aa_codons)


def build_relative_adaptiveness():
    """Relative adaptiveness w(c) = freq(c) / max_freq_for_same_aa."""
    from collections import defaultdict
    aa_codons = defaultdict(list)
    for codon, aa in CODON_TO_AA.items():
        if aa != "*":
            aa_codons[aa].append(codon)

    ra = {}
    for aa, codons in aa_codons.items():
        max_freq = max(HUMAN_FREQ.get(c, 0.01) for c in codons)
        for c in codons:
            ra[c] = HUMAN_FREQ.get(c, 0.01) / max_freq
    return ra


SYNONYMOUS_CODONS = build_synonymous_codons()
RELATIVE_ADAPTIVENESS = build_relative_adaptiveness()
