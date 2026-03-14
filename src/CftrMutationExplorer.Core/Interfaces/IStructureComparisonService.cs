using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Core.Interfaces;

public interface IStructureComparisonService
{
    StructureComparisonResult Compare(ProteinStructure reference, ProteinStructure mutant);
    double? CalculateSimplifiedRmsd(ProteinStructure reference, ProteinStructure mutant, char chainId);
    List<Residue> GetMutationNeighborhood(ProteinStructure structure, int residueNumber, double radiusAngstroms = 10.0);
}
