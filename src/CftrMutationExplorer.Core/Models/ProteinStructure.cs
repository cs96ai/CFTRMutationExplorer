namespace CftrMutationExplorer.Core.Models;

public class ProteinStructure
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
    public string? Header { get; set; }
    public string? Title { get; set; }
    public List<string> Remarks { get; set; } = new();
    public List<Chain> Chains { get; set; } = new();

    public int ChainCount => Chains.Count;
    public int ResidueCount => Chains.Sum(c => c.Residues.Count);
    public int AtomCount => Chains.Sum(c => c.AtomCount);

    public IEnumerable<Atom> AllAtoms =>
        Chains.SelectMany(c => c.Residues).SelectMany(r => r.Atoms);

    public IEnumerable<Residue> AllResidues =>
        Chains.SelectMany(c => c.Residues);

    public Chain? FindChain(char chainId) =>
        Chains.FirstOrDefault(c => c.Id == chainId);

    public Residue? FindResidue(char chainId, int sequenceNumber) =>
        FindChain(chainId)?.FindResidue(sequenceNumber);

    public List<Residue> FindResiduesNear(Residue target, double distanceAngstroms) =>
        AllResidues
            .Where(r => r != target && r.DistanceTo(target) <= distanceAngstroms)
            .OrderBy(r => r.DistanceTo(target))
            .ToList();

    public (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ) BoundingBox
    {
        get
        {
            var atoms = AllAtoms.ToList();
            if (atoms.Count == 0)
                return (0, 0, 0, 0, 0, 0);

            return (
                atoms.Min(a => a.X), atoms.Min(a => a.Y), atoms.Min(a => a.Z),
                atoms.Max(a => a.X), atoms.Max(a => a.Y), atoms.Max(a => a.Z)
            );
        }
    }
}
