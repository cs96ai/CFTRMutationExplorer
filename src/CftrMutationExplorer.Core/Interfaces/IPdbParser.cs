using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Core.Interfaces;

public interface IPdbParser
{
    Task<ProteinStructure> ParseAsync(string filePath, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    Task<ProteinStructure> ParseFromStreamAsync(Stream stream, string fileName, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
}
