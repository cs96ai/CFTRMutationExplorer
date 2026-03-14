namespace CftrMutationExplorer.Core.Models;

public class Residue
{
    public int SequenceNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public char ChainId { get; set; }
    public List<Atom> Atoms { get; set; } = new();

    public (double X, double Y, double Z) Centroid
    {
        get
        {
            if (Atoms.Count == 0)
                return (0, 0, 0);

            var x = Atoms.Average(a => a.X);
            var y = Atoms.Average(a => a.Y);
            var z = Atoms.Average(a => a.Z);
            return (x, y, z);
        }
    }

    public double DistanceTo(Residue other)
    {
        var (x1, y1, z1) = Centroid;
        var (x2, y2, z2) = other.Centroid;
        var dx = x1 - x2;
        var dy = y1 - y2;
        var dz = z1 - z2;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static readonly Dictionary<string, string> ThreeToOneLetter = new()
    {
        ["ALA"] = "A", ["ARG"] = "R", ["ASN"] = "N", ["ASP"] = "D",
        ["CYS"] = "C", ["GLN"] = "Q", ["GLU"] = "E", ["GLY"] = "G",
        ["HIS"] = "H", ["ILE"] = "I", ["LEU"] = "L", ["LYS"] = "K",
        ["MET"] = "M", ["PHE"] = "F", ["PRO"] = "P", ["SER"] = "S",
        ["THR"] = "T", ["TRP"] = "W", ["TYR"] = "Y", ["VAL"] = "V"
    };

    public string SingleLetterCode =>
        ThreeToOneLetter.TryGetValue(Name.Trim().ToUpperInvariant(), out var code) ? code : "?";

    public bool IsStandardAminoAcid =>
        ThreeToOneLetter.ContainsKey(Name.Trim().ToUpperInvariant());
}
