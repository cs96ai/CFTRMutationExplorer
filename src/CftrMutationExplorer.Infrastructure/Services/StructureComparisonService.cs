using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Infrastructure.Services;

public class StructureComparisonService : IStructureComparisonService
{
    public StructureComparisonResult Compare(ProteinStructure reference, ProteinStructure mutant)
    {
        var result = new StructureComparisonResult
        {
            ReferenceFileName = reference.FileName,
            MutantFileName = mutant.FileName,
            ReferenceChainCount = reference.ChainCount,
            MutantChainCount = mutant.ChainCount,
            ReferenceResidueCount = reference.ResidueCount,
            MutantResidueCount = mutant.ResidueCount,
            ReferenceAtomCount = reference.AtomCount,
            MutantAtomCount = mutant.AtomCount
        };

        var commonChains = reference.Chains
            .Select(c => c.Id)
            .Intersect(mutant.Chains.Select(c => c.Id))
            .ToList();

        foreach (var chainId in commonChains)
        {
            var refChain = reference.FindChain(chainId)!;
            var mutChain = mutant.FindChain(chainId)!;

            CompareChainResidues(refChain, mutChain, result);

            var rmsd = CalculateSimplifiedRmsd(reference, mutant, chainId);
            if (rmsd.HasValue && !result.SimplifiedRmsd.HasValue)
                result.SimplifiedRmsd = rmsd;
        }

        var refOnlyChains = reference.Chains.Select(c => c.Id).Except(mutant.Chains.Select(c => c.Id));
        foreach (var chainId in refOnlyChains)
            result.Warnings.Add($"Chain {chainId} present in reference but missing in mutant");

        var mutOnlyChains = mutant.Chains.Select(c => c.Id).Except(reference.Chains.Select(c => c.Id));
        foreach (var chainId in mutOnlyChains)
            result.Warnings.Add($"Chain {chainId} present in mutant but missing in reference");

        return result;
    }

    public double? CalculateSimplifiedRmsd(ProteinStructure reference, ProteinStructure mutant, char chainId)
    {
        var refChain = reference.FindChain(chainId);
        var mutChain = mutant.FindChain(chainId);

        if (refChain == null || mutChain == null)
            return null;

        // Pair residues by sequence number and use CA (alpha carbon) positions
        var pairedDistances = new List<double>();

        foreach (var refResidue in refChain.Residues)
        {
            var mutResidue = mutChain.FindResidue(refResidue.SequenceNumber);
            if (mutResidue == null) continue;

            var refCa = refResidue.Atoms.FirstOrDefault(a => a.Name == "CA");
            var mutCa = mutResidue.Atoms.FirstOrDefault(a => a.Name == "CA");

            if (refCa == null || mutCa == null) continue;

            var dx = refCa.X - mutCa.X;
            var dy = refCa.Y - mutCa.Y;
            var dz = refCa.Z - mutCa.Z;
            pairedDistances.Add(dx * dx + dy * dy + dz * dz);
        }

        if (pairedDistances.Count == 0)
            return null;

        return Math.Sqrt(pairedDistances.Average());
    }

    public List<Residue> GetMutationNeighborhood(ProteinStructure structure, int residueNumber, double radiusAngstroms = 10.0)
    {
        var targetResidue = structure.AllResidues.FirstOrDefault(r => r.SequenceNumber == residueNumber);
        if (targetResidue == null)
            return new List<Residue>();

        return structure.FindResiduesNear(targetResidue, radiusAngstroms);
    }

    private static void CompareChainResidues(Chain refChain, Chain mutChain, StructureComparisonResult result)
    {
        var refResidueNumbers = refChain.Residues.Select(r => r.SequenceNumber).ToHashSet();
        var mutResidueNumbers = mutChain.Residues.Select(r => r.SequenceNumber).ToHashSet();

        var allResidueNumbers = refResidueNumbers.Union(mutResidueNumbers).OrderBy(n => n);

        foreach (var resNum in allResidueNumbers)
        {
            var refResidue = refChain.FindResidue(resNum);
            var mutResidue = mutChain.FindResidue(resNum);

            if (refResidue != null && mutResidue == null)
            {
                result.MissingInMutant.Add($"{refChain.Id}:{refResidue.Name}{resNum}");
                result.ResidueComparisons.Add(new ResidueComparisonEntry
                {
                    ResidueNumber = resNum,
                    ChainId = refChain.Id,
                    ReferenceResidueName = refResidue.Name,
                    MutantResidueName = "(missing)",
                    IsMissing = true
                });
            }
            else if (refResidue == null && mutResidue != null)
            {
                result.MissingInReference.Add($"{mutChain.Id}:{mutResidue.Name}{resNum}");
                result.ResidueComparisons.Add(new ResidueComparisonEntry
                {
                    ResidueNumber = resNum,
                    ChainId = mutChain.Id,
                    ReferenceResidueName = "(missing)",
                    MutantResidueName = mutResidue.Name,
                    IsMissing = true
                });
            }
            else if (refResidue != null && mutResidue != null)
            {
                var entry = new ResidueComparisonEntry
                {
                    ResidueNumber = resNum,
                    ChainId = refChain.Id,
                    ReferenceResidueName = refResidue.Name,
                    MutantResidueName = mutResidue.Name,
                    CentroidDistance = refResidue.DistanceTo(mutResidue)
                };
                result.ResidueComparisons.Add(entry);
            }
        }
    }
}
