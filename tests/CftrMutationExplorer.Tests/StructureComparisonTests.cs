using System.Text;
using CftrMutationExplorer.Infrastructure.Parsing;
using CftrMutationExplorer.Infrastructure.Services;

namespace CftrMutationExplorer.Tests;

public class StructureComparisonTests
{
    private readonly PdbParser _parser = new();
    private readonly StructureComparisonService _comparisonService = new();

    private Stream MakeStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task CompareIdenticalStructures_ZeroRmsd()
    {
        var pdb = """
            ATOM      1  CA  MET A   1      27.340  24.430  10.220  1.00 20.00           C
            ATOM      2  CA  GLN A   2      26.500  27.720   8.780  1.00 18.00           C
            END
            """;

        using var s1 = MakeStream(pdb);
        using var s2 = MakeStream(pdb);
        var ref1 = await _parser.ParseFromStreamAsync(s1, "ref.pdb");
        var mut1 = await _parser.ParseFromStreamAsync(s2, "mut.pdb");

        var result = _comparisonService.Compare(ref1, mut1);

        Assert.Equal(0, result.ResidueDifference);
        Assert.Equal(0, result.AtomDifference);
        Assert.NotNull(result.SimplifiedRmsd);
        Assert.Equal(0.0, result.SimplifiedRmsd!.Value, 5);
    }

    [Fact]
    public async Task DetectsMissingResidues()
    {
        var refPdb = """
            ATOM      1  CA  MET A   1      27.340  24.430  10.220  1.00 20.00           C
            ATOM      2  CA  PHE A 508      50.170  50.710  14.580  1.00 10.00           C
            END
            """;

        var mutPdb = """
            ATOM      1  CA  MET A   1      27.340  24.430  10.220  1.00 20.00           C
            END
            """;

        using var s1 = MakeStream(refPdb);
        using var s2 = MakeStream(mutPdb);
        var reference = await _parser.ParseFromStreamAsync(s1, "ref.pdb");
        var mutant = await _parser.ParseFromStreamAsync(s2, "mut.pdb");

        var result = _comparisonService.Compare(reference, mutant);

        Assert.Single(result.MissingInMutant);
        Assert.Contains("PHE508", result.MissingInMutant[0]);
    }

    [Fact]
    public async Task CalculatesRmsdForShiftedStructure()
    {
        var refPdb = """
            ATOM      1  CA  MET A   1       0.000   0.000   0.000  1.00 20.00           C
            ATOM      2  CA  GLN A   2      10.000   0.000   0.000  1.00 18.00           C
            END
            """;

        var mutPdb = """
            ATOM      1  CA  MET A   1       1.000   0.000   0.000  1.00 20.00           C
            ATOM      2  CA  GLN A   2      11.000   0.000   0.000  1.00 18.00           C
            END
            """;

        using var s1 = MakeStream(refPdb);
        using var s2 = MakeStream(mutPdb);
        var reference = await _parser.ParseFromStreamAsync(s1, "ref.pdb");
        var mutant = await _parser.ParseFromStreamAsync(s2, "mut.pdb");

        var rmsd = _comparisonService.CalculateSimplifiedRmsd(reference, mutant, 'A');

        Assert.NotNull(rmsd);
        Assert.Equal(1.0, rmsd!.Value, 2);
    }

    [Fact]
    public async Task GetMutationNeighborhood_ReturnsNearbyResidues()
    {
        var pdb = """
            ATOM      1  CA  ILE A 507      48.500  48.530  11.910  1.00 11.00           C
            ATOM      2  CA  PHE A 508      50.170  50.710  14.580  1.00 10.00           C
            ATOM      3  CA  GLY A 509      52.000  52.880  12.030  1.00 11.00           C
            ATOM      4  CA  MET A   1      90.000  90.000  90.000  1.00 20.00           C
            END
            """;

        using var stream = MakeStream(pdb);
        var structure = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        var neighbors = _comparisonService.GetMutationNeighborhood(structure, 508, 10.0);

        Assert.Equal(2, neighbors.Count);
        Assert.Contains(neighbors, r => r.SequenceNumber == 507);
        Assert.Contains(neighbors, r => r.SequenceNumber == 509);
        Assert.DoesNotContain(neighbors, r => r.SequenceNumber == 1);
    }

    [Fact]
    public async Task CompareDetectsChainDifferences()
    {
        var refPdb = """
            ATOM      1  CA  MET A   1      27.340  24.430  10.220  1.00 20.00           C
            ATOM      2  CA  GLY B   1      30.000  25.000  11.000  1.00 20.00           C
            END
            """;

        var mutPdb = """
            ATOM      1  CA  MET A   1      27.340  24.430  10.220  1.00 20.00           C
            END
            """;

        using var s1 = MakeStream(refPdb);
        using var s2 = MakeStream(mutPdb);
        var reference = await _parser.ParseFromStreamAsync(s1, "ref.pdb");
        var mutant = await _parser.ParseFromStreamAsync(s2, "mut.pdb");

        var result = _comparisonService.Compare(reference, mutant);

        Assert.Contains(result.Warnings, w => w.Contains("Chain B"));
    }
}
