using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Core.Interfaces;

public interface IBindingPocketService
{
    List<BindingPocketCandidate> DetectCandidatePockets(
        ProteinStructure structure,
        double clusterRadiusAngstroms = 8.0,
        int minResiduesPerCluster = 4);
}
