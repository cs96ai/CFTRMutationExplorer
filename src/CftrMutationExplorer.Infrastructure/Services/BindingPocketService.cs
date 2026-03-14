using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Infrastructure.Services;

/// <summary>
/// Heuristic-based binding pocket candidate detection.
/// This is a demo screening aid using spatial clustering — not a validated docking algorithm.
/// </summary>
public class BindingPocketService : IBindingPocketService
{
    public List<BindingPocketCandidate> DetectCandidatePockets(
        ProteinStructure structure,
        double clusterRadiusAngstroms = 8.0,
        int minResiduesPerCluster = 4)
    {
        var candidates = new List<BindingPocketCandidate>();
        var residues = structure.AllResidues.Where(r => r.IsStandardAminoAcid).ToList();

        if (residues.Count == 0)
            return candidates;

        var visited = new HashSet<int>();
        int pocketId = 0;

        // Simplified spatial clustering: find groups of residues where
        // hydrophobic or polar residues cluster together (potential cavities)
        var interestingResidues = residues
            .Where(r => IsBindingRelevant(r.Name))
            .ToList();

        foreach (var seed in interestingResidues)
        {
            if (visited.Contains(GetResidueKey(seed)))
                continue;

            var cluster = new List<Residue> { seed };
            visited.Add(GetResidueKey(seed));

            // Grow cluster by finding nearby interesting residues
            var queue = new Queue<Residue>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var nearby = interestingResidues
                    .Where(r => !visited.Contains(GetResidueKey(r))
                                && current.DistanceTo(r) <= clusterRadiusAngstroms)
                    .ToList();

                foreach (var neighbor in nearby)
                {
                    visited.Add(GetResidueKey(neighbor));
                    cluster.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            if (cluster.Count >= minResiduesPerCluster)
            {
                pocketId++;
                var center = (
                    cluster.Average(r => r.Centroid.X),
                    cluster.Average(r => r.Centroid.Y),
                    cluster.Average(r => r.Centroid.Z)
                );

                var maxDist = cluster.Max(r =>
                {
                    var dx = r.Centroid.X - center.Item1;
                    var dy = r.Centroid.Y - center.Item2;
                    var dz = r.Centroid.Z - center.Item3;
                    return Math.Sqrt(dx * dx + dy * dy + dz * dz);
                });

                // Rough volume approximation (sphere)
                var volume = (4.0 / 3.0) * Math.PI * maxDist * maxDist * maxDist;

                var confidence = cluster.Count switch
                {
                    >= 10 => PocketConfidence.High,
                    >= 6 => PocketConfidence.Medium,
                    _ => PocketConfidence.Low
                };

                candidates.Add(new BindingPocketCandidate
                {
                    Id = pocketId,
                    Label = $"Pocket {pocketId}",
                    Residues = cluster,
                    Confidence = confidence,
                    ApproximateVolume = volume,
                    CenterOfMass = center,
                    Description = $"Cluster of {cluster.Count} binding-relevant residues " +
                                  $"(radius ~{maxDist:F1}Å, volume ~{volume:F0}ų). " +
                                  $"Demo heuristic — not a validated pocket detection."
                });
            }
        }

        return candidates.OrderByDescending(c => c.Confidence).ThenByDescending(c => c.ResidueCount).ToList();
    }

    private static bool IsBindingRelevant(string residueName)
    {
        var name = residueName.Trim().ToUpperInvariant();
        // Hydrophobic residues often line binding pockets
        if ("LEU ILE VAL PHE TRP MET ALA PRO".Contains(name))
            return true;
        // Polar residues can form hydrogen bonds with ligands
        if ("SER THR CYS TYR ASN GLN HIS".Contains(name))
            return true;
        // Charged residues at pocket edges
        if ("ASP GLU ARG LYS".Contains(name))
            return true;
        return false;
    }

    private static int GetResidueKey(Residue r) =>
        HashCode.Combine(r.ChainId, r.SequenceNumber);
}
