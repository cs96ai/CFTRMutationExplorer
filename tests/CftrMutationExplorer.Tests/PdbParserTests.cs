using System.Text;
using CftrMutationExplorer.Infrastructure.Parsing;

namespace CftrMutationExplorer.Tests;

public class PdbParserTests
{
    private readonly PdbParser _parser = new();

    private Stream MakeStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task ParsesHeaderAndTitle()
    {
        var pdb = """
            HEADER    TRANSPORT PROTEIN                        01-JAN-26   DEMO
            TITLE     CFTR NORMAL REFERENCE STRUCTURE
            ATOM      1  N   MET A   1      27.340  24.430  10.220  1.00 20.00           N
            END
            """;

        using var stream = MakeStream(pdb);
        var result = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        Assert.Contains("TRANSPORT PROTEIN", result.Header);
        Assert.Contains("CFTR NORMAL REFERENCE STRUCTURE", result.Title);
    }

    [Fact]
    public async Task ParsesAtomCoordinates()
    {
        var pdb = "ATOM      1  N   MET A   1      27.340  24.430  10.220  1.00 20.00           N\nEND\n";

        using var stream = MakeStream(pdb);
        var result = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        var atom = result.AllAtoms.First();
        Assert.Equal(27.340, atom.X, 3);
        Assert.Equal(24.430, atom.Y, 3);
        Assert.Equal(10.220, atom.Z, 3);
        Assert.Equal("N", atom.Element);
        Assert.Equal("MET", atom.ResidueName);
        Assert.Equal('A', atom.ChainId);
        Assert.Equal(1, atom.ResidueSequenceNumber);
        Assert.Equal(1.00, atom.Occupancy, 2);
        Assert.Equal(20.00, atom.TemperatureFactor, 2);
    }

    [Fact]
    public async Task GroupsAtomsIntoResiduesAndChains()
    {
        var pdb = """
            ATOM      1  N   MET A   1      27.340  24.430  10.220  1.00 20.00           N
            ATOM      2  CA  MET A   1      26.620  25.340   9.200  1.00 20.00           C
            ATOM      3  N   GLN A   2      26.500  27.720   8.780  1.00 18.00           N
            ATOM      4  CA  GLN A   2      27.020  29.090   8.670  1.00 18.00           C
            END
            """;

        using var stream = MakeStream(pdb);
        var result = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        Assert.Single(result.Chains);
        Assert.Equal(2, result.ResidueCount);
        Assert.Equal(4, result.AtomCount);

        var met = result.Chains[0].Residues[0];
        Assert.Equal("MET", met.Name);
        Assert.Equal(1, met.SequenceNumber);
        Assert.Equal(2, met.Atoms.Count);

        var gln = result.Chains[0].Residues[1];
        Assert.Equal("GLN", gln.Name);
        Assert.Equal(2, gln.SequenceNumber);
        Assert.Equal(2, gln.Atoms.Count);
    }

    [Fact]
    public async Task ParsesMultipleChains()
    {
        var pdb = """
            ATOM      1  N   MET A   1      27.340  24.430  10.220  1.00 20.00           N
            ATOM      2  N   GLY B   1      30.000  25.000  11.000  1.00 20.00           N
            END
            """;

        using var stream = MakeStream(pdb);
        var result = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        Assert.Equal(2, result.ChainCount);
        Assert.Equal('A', result.Chains[0].Id);
        Assert.Equal('B', result.Chains[1].Id);
    }

    [Fact]
    public async Task ParsesHetAtm()
    {
        var pdb = "HETATM  500  O   HOH A 100      10.000  20.000  30.000  1.00 15.00           O\nEND\n";

        using var stream = MakeStream(pdb);
        var result = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        var atom = result.AllAtoms.First();
        Assert.True(atom.IsHetAtom);
        Assert.Equal("HOH", atom.ResidueName);
    }

    [Fact]
    public async Task HandlesEmptyFile()
    {
        using var stream = MakeStream("END\n");
        var result = await _parser.ParseFromStreamAsync(stream, "empty.pdb");

        Assert.Empty(result.Chains);
        Assert.Equal(0, result.AtomCount);
    }

    [Fact]
    public async Task HandlesMalformedLines()
    {
        var pdb = """
            ATOM      1  N   MET A   1      27.340  24.430  10.220  1.00 20.00           N
            THIS IS NOT A VALID LINE
            SHORT
            ATOM      2  CA  MET A   1      26.620  25.340   9.200  1.00 20.00           C
            END
            """;

        using var stream = MakeStream(pdb);
        var result = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        Assert.Equal(2, result.AtomCount);
    }

    [Fact]
    public async Task ParsesRemarks()
    {
        var pdb = """
            REMARK   1 THIS IS A TEST REMARK
            REMARK   2 ANOTHER REMARK
            ATOM      1  N   MET A   1      27.340  24.430  10.220  1.00 20.00           N
            END
            """;

        using var stream = MakeStream(pdb);
        var result = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        Assert.Equal(2, result.Remarks.Count);
    }

    [Fact]
    public async Task ResidueCentroidIsCorrect()
    {
        var pdb = """
            ATOM      1  N   GLY A   1       0.000   0.000   0.000  1.00 20.00           N
            ATOM      2  CA  GLY A   1      10.000   0.000   0.000  1.00 20.00           C
            END
            """;

        using var stream = MakeStream(pdb);
        var result = await _parser.ParseFromStreamAsync(stream, "test.pdb");

        var residue = result.Chains[0].Residues[0];
        var centroid = residue.Centroid;
        Assert.Equal(5.0, centroid.X, 1);
        Assert.Equal(0.0, centroid.Y, 1);
        Assert.Equal(0.0, centroid.Z, 1);
    }

    [Fact]
    public async Task CanCancelParsing()
    {
        var pdb = string.Join("\n", Enumerable.Range(1, 10000)
            .Select(i => $"ATOM  {i,5}  N   GLY A {i,4}      27.340  24.430  10.220  1.00 20.00           N"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var stream = MakeStream(pdb);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _parser.ParseFromStreamAsync(stream, "test.pdb", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ReportsProgress()
    {
        var lines = Enumerable.Range(1, 5000)
            .Select(i => $"ATOM  {i,5}  N   GLY A {i % 1000,4}      27.340  24.430  10.220  1.00 20.00           N");
        var pdb = string.Join("\n", lines) + "\nEND\n";

        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));

        using var stream = MakeStream(pdb);
        await _parser.ParseFromStreamAsync(stream, "test.pdb", progress);

        Assert.NotEmpty(progressValues);
        Assert.Contains(100, progressValues);
    }
}
