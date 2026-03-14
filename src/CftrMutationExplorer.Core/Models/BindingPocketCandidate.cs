namespace CftrMutationExplorer.Core.Models;

public enum PocketConfidence
{
    Low,
    Medium,
    High
}

public class BindingPocketCandidate
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public List<Residue> Residues { get; set; } = new();
    public PocketConfidence Confidence { get; set; } = PocketConfidence.Low;
    public double ApproximateVolume { get; set; }
    public (double X, double Y, double Z) CenterOfMass { get; set; }
    public string Description { get; set; } = string.Empty;

    public int ResidueCount => Residues.Count;
}
