using CftrMutationExplorer.Core.Models.Mrna;
using CftrMutationExplorer.Infrastructure.Services.Mrna;

namespace CftrMutationExplorer.Tests;

public class CodonTableTests
{
    [Fact]
    public void AllCodonsMapToAminoAcids()
    {
        Assert.Equal(64, CodonTable.CodonToAminoAcid.Count);
        foreach (var kv in CodonTable.CodonToAminoAcid)
        {
            Assert.Equal(3, kv.Key.Length);
            Assert.Single(kv.Value);
        }
    }

    [Fact]
    public void StopCodonsMapCorrectly()
    {
        Assert.Equal("*", CodonTable.GetAminoAcid("UAA"));
        Assert.Equal("*", CodonTable.GetAminoAcid("UAG"));
        Assert.Equal("*", CodonTable.GetAminoAcid("UGA"));
    }

    [Fact]
    public void SynonymousCodonsContainAllCodonsForLeucine()
    {
        var leucineCodons = CodonTable.GetSynonymousCodons("L");
        Assert.Equal(6, leucineCodons.Length);
        Assert.Contains("CUG", leucineCodons); // most frequent
        Assert.Equal("CUG", leucineCodons[0]);  // sorted by frequency, CUG is #1
    }

    [Fact]
    public void MethionineHasOnlyOneCodon()
    {
        var codons = CodonTable.GetSynonymousCodons("M");
        Assert.Single(codons);
        Assert.Equal("AUG", codons[0]);
    }

    [Fact]
    public void RelativeAdaptivenessInRange()
    {
        foreach (var kv in CodonTable.RelativeAdaptiveness)
        {
            Assert.InRange(kv.Value, 0.01, 1.0);
        }
    }

    [Fact]
    public void BestCodonForLeucineIsCUG()
    {
        Assert.Equal("CUG", CodonTable.BestCodonPerAminoAcid["L"]);
    }

    [Fact]
    public void TranslateProducesCorrectProtein()
    {
        var rna = "AUGGCUUAA"; // M-A-stop
        var protein = CodonTable.Translate(rna);
        Assert.Equal("MA", protein);
    }

    [Fact]
    public void BackTranslateOptimalPreservesProteinSequence()
    {
        var protein = "MAFLK";
        var rna = CodonTable.BackTranslateOptimal(protein);
        var translated = CodonTable.Translate(rna);
        Assert.Equal(protein, translated);
    }

    [Fact]
    public void DnaToRnaConversion()
    {
        Assert.Equal("AUGC", CodonTable.DnaToRna("ATGC"));
    }
}

public class CftrSequenceTests
{
    [Fact]
    public void ProteinSequenceIs1480AminoAcids()
    {
        Assert.Equal(1480, CftrSequence.ProteinLength);
    }

    [Fact]
    public void Position508IsPhenylalanine()
    {
        Assert.Equal('F', CftrSequence.GetAminoAcid(508));
    }

    [Fact]
    public void StartsWithMethionine()
    {
        Assert.Equal('M', CftrSequence.GetAminoAcid(1));
    }

    [Fact]
    public void HasFiveDomains()
    {
        Assert.Equal(5, CftrSequence.Domains.Count);
    }

    [Fact]
    public void Position508IsInNBD1()
    {
        var domain = CftrSequence.GetDomain(508);
        Assert.NotNull(domain);
        Assert.Equal("NBD1", domain.Name);
    }
}

public class CodonScoringServiceTests
{
    private readonly CodonScoringService _scoring = new();

    [Fact]
    public void CaiIsHighForOptimalCodons()
    {
        var protein = CftrSequence.ProteinSequence[..50]; // first 50 aa
        var rna = CodonTable.BackTranslateOptimal(protein);
        var cai = _scoring.CalculateCai(rna);
        Assert.InRange(cai, 0.9, 1.0); // optimal codons → CAI near 1.0
    }

    [Fact]
    public void CaiIsLowerForRareCodons()
    {
        // Use the rarest codon for each amino acid
        var protein = "MAFLK";
        var synCodons = new Dictionary<string, string>
        {
            ["M"] = "AUG", ["A"] = "GCG", ["F"] = "UUU", ["L"] = "UUA", ["K"] = "AAA"
        };
        var rna = string.Concat(protein.Select(aa => synCodons[aa.ToString()]));
        var cai = _scoring.CalculateCai(rna);

        var optimalRna = CodonTable.BackTranslateOptimal(protein);
        var optimalCai = _scoring.CalculateCai(optimalRna);

        Assert.True(cai < optimalCai);
    }

    [Fact]
    public void GcContentScoreIsHighInOptimalRange()
    {
        // Construct a sequence with ~50% GC
        var seq = string.Concat(Enumerable.Repeat("GCAU", 100)); // exactly 50% GC
        var score = _scoring.CalculateGcContentScore(seq);
        Assert.InRange(score, 0.5, 1.0);
    }

    [Fact]
    public void CpgScoreDecreasesWithMoreCpg()
    {
        var lowCpg = string.Concat(Enumerable.Repeat("AUAUAU", 50));
        var highCpg = string.Concat(Enumerable.Repeat("CGCGCG", 50));
        var lowScore = _scoring.CalculateCpgScore(lowCpg);
        var highScore = _scoring.CalculateCpgScore(highCpg);
        Assert.True(lowScore > highScore);
    }

    [Fact]
    public void UridineScoreDecreasesWithMoreUridine()
    {
        var lowU = string.Concat(Enumerable.Repeat("GCC", 100));
        var highU = string.Concat(Enumerable.Repeat("UUU", 100));
        var lowScore = _scoring.CalculateUridineScore(lowU);
        var highScore = _scoring.CalculateUridineScore(highU);
        Assert.True(lowScore > highScore);
    }

    [Fact]
    public void RepeatScorePenalizesHomopolymers()
    {
        var noRepeats = "AUGCAUGCAUGCAUGC";
        var withRepeats = "AAAAAAAAAUGCAUGC";
        var noRepScore = _scoring.CalculateRepeatScore(noRepeats);
        var repScore = _scoring.CalculateRepeatScore(withRepeats);
        Assert.True(noRepScore >= repScore);
    }

    [Fact]
    public void BatchScoreProcessesMultipleCandidates()
    {
        var protein = "MAFLK";
        var candidates = new List<MrnaCandidate>();
        for (int i = 0; i < 10; i++)
        {
            var c = new MrnaCandidate(protein.Length);
            candidates.Add(c);
        }

        _scoring.ScoreBatch(candidates, protein, new ObjectiveWeights());

        foreach (var c in candidates)
        {
            Assert.True(c.Score.Cai > 0);
            Assert.True(c.Score.CompositeFitness > 0);
        }
    }
}

public class RnaFoldingServiceTests
{
    private readonly RnaFoldingService _folding = new();

    [Fact]
    public void PredictMfeReturnsNegativeForComplementarySequence()
    {
        // This sequence can form a simple hairpin
        var rna = "GGGGAAAACCCC"; // GGGG pairs with CCCC
        var mfe = _folding.PredictMfe(rna);
        Assert.True(mfe < 0, $"MFE should be negative for complementary sequence, got {mfe}");
    }

    [Fact]
    public void PredictMfeIsZeroForVeryShortSequence()
    {
        var mfe = _folding.PredictMfe("AU");
        Assert.Equal(0, mfe);
    }

    [Fact]
    public void FivePrimeFoldingScoreInRange()
    {
        var rna = string.Concat(Enumerable.Repeat("AUGCAUGC", 20));
        var score = _folding.ScoreFivePrimeFolding(rna);
        Assert.InRange(score, 0.0, 1.0);
    }

    [Fact]
    public void SlidingWindowMfeReturnsMultipleValues()
    {
        var rna = string.Concat(Enumerable.Repeat("AUGCAUGC", 50));
        var results = _folding.SlidingWindowMfe(rna, 50, 10);
        Assert.True(results.Count > 1);
    }
}

public class MrnaCandidateTests
{
    [Fact]
    public void CandidateToRnaSequencePreservesProtein()
    {
        var protein = "MAFLKW";
        var candidate = new MrnaCandidate(protein.Length);
        // All zeros = first (most frequent) codon for each aa
        var rna = candidate.ToRnaSequence(protein);
        var translated = CodonTable.Translate(rna);
        Assert.Equal(protein, translated);
    }

    [Fact]
    public void CloneProducesIndependentCopy()
    {
        var candidate = new MrnaCandidate(10);
        candidate.Codons[0] = 1;
        candidate.Score.Cai = 0.9;

        var clone = candidate.Clone();
        clone.Codons[0] = 2;
        clone.Score.Cai = 0.5;

        Assert.Equal(1, candidate.Codons[0]);
        Assert.Equal(0.9, candidate.Score.Cai);
    }

    [Fact]
    public void DominatesReturnsTrueWhenAllObjectivesBetter()
    {
        var a = new MrnaCandidate(1)
        {
            Score = new OptimizationScore { Cai = 0.9, GcContentScore = 0.9, CpgScore = 0.9,
                UridineScore = 0.9, RareCodonScore = 0.9, RepeatScore = 0.9, FoldingScore = 0.9, CodonPairScore = 0.9 }
        };
        var b = new MrnaCandidate(1)
        {
            Score = new OptimizationScore { Cai = 0.5, GcContentScore = 0.5, CpgScore = 0.5,
                UridineScore = 0.5, RareCodonScore = 0.5, RepeatScore = 0.5, FoldingScore = 0.5, CodonPairScore = 0.5 }
        };

        Assert.True(a.Dominates(b));
        Assert.False(b.Dominates(a));
    }
}

public class ConstructDesignTests
{
    [Fact]
    public void FullSequenceIncludesAllComponents()
    {
        var construct = new ConstructDesign
        {
            FivePrimeUtrSequence = "AAAA",
            KozakSequence = "GCCACC",
            CodingSequence = "GCUGCU",
            StopCodon = "UGA",
            ThreePrimeUtrSequence = "CCCC",
            PolyALength = 100,
        };

        // UTR(4) + Kozak(6) + AUG(3) + CDS(6) + Stop(3) + UTR(4) = 26
        Assert.Equal("AAAAGCCACCAUGGCUGCUUGACCCC", construct.FullSequence);
        Assert.Equal(26 + 100, construct.TotalLength);
    }

    [Fact]
    public void ToFastaProducesValidFormat()
    {
        var construct = new ConstructDesign
        {
            Name = "test",
            FivePrimeUtrSequence = "",
            KozakSequence = "",
            CodingSequence = "AUGGCU",
            StopCodon = "UGA",
            ThreePrimeUtrSequence = "",
            PolyALength = 10,
            ProteinSequenceEncoded = "MA",
        };

        var fasta = construct.ToFasta();
        Assert.StartsWith(">test", fasta);
        Assert.Contains("AUGGCUUGA", fasta);
    }
}

public class UtrLibraryTests
{
    [Fact]
    public void FivePrimeUtrsAreNotEmpty()
    {
        foreach (var utr in UtrLibrary.FivePrimeUtrs.Values)
        {
            Assert.False(string.IsNullOrEmpty(utr.Sequence));
            Assert.True(utr.Length > 0);
        }
    }

    [Fact]
    public void ThreePrimeUtrsAreNotEmpty()
    {
        foreach (var utr in UtrLibrary.ThreePrimeUtrs.Values)
        {
            Assert.False(string.IsNullOrEmpty(utr.Sequence));
            Assert.True(utr.Length > 0);
        }
    }

    [Fact]
    public void GetFivePrimeUtrReturnsFallback()
    {
        var utr = UtrLibrary.GetFivePrimeUtr("nonexistent");
        Assert.Equal("HBA1 (α-globin)", utr.Name);
    }
}
