namespace CftrMutationExplorer.Core.Models;

public class Atom
{
    public int SerialNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public char AltLoc { get; set; }
    public string ResidueName { get; set; } = string.Empty;
    public char ChainId { get; set; }
    public int ResidueSequenceNumber { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Occupancy { get; set; }
    public double TemperatureFactor { get; set; }
    public string Element { get; set; } = string.Empty;
    public bool IsHetAtom { get; set; }

    public (double X, double Y, double Z) Position => (X, Y, Z);

    public double DistanceTo(Atom other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
