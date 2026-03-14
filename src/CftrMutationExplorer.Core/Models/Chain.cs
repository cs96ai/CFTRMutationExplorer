namespace CftrMutationExplorer.Core.Models;

public class Chain
{
    public char Id { get; set; }
    public List<Residue> Residues { get; set; } = new();

    public int AtomCount => Residues.Sum(r => r.Atoms.Count);

    public Residue? FindResidue(int sequenceNumber) =>
        Residues.FirstOrDefault(r => r.SequenceNumber == sequenceNumber);

    public List<Residue> FindResiduesInRange(int start, int end) =>
        Residues.Where(r => r.SequenceNumber >= start && r.SequenceNumber <= end).ToList();
}
