using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.App.ViewModels;

public partial class MutationAnalysisViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _hasAnalysis;

    [ObservableProperty]
    private int _targetResidueNumber;

    [ObservableProperty]
    private string _mutationSummary = string.Empty;

    [ObservableProperty]
    private string _referenceResidueInfo = string.Empty;

    [ObservableProperty]
    private string _mutantResidueInfo = string.Empty;

    [ObservableProperty]
    private string _structuralImpactSummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<NeighborResidueItem> _referenceNeighbors = new();

    [ObservableProperty]
    private ObservableCollection<NeighborResidueItem> _mutantNeighbors = new();

    [ObservableProperty]
    private bool _residueIsMissing;

    [ObservableProperty]
    private string _foldingAssessment = string.Empty;

    public void LoadAnalysis(
        ProteinStructure reference,
        ProteinStructure mutant,
        int residueNumber,
        List<Residue> refNeighborhood,
        List<Residue> mutNeighborhood,
        StructureComparisonResult comparison)
    {
        TargetResidueNumber = residueNumber;
        HasAnalysis = true;

        var refResidue = reference.AllResidues.FirstOrDefault(r => r.SequenceNumber == residueNumber);
        var mutResidue = mutant.AllResidues.FirstOrDefault(r => r.SequenceNumber == residueNumber);

        ResidueIsMissing = mutResidue == null;

        if (refResidue != null)
        {
            var (x, y, z) = refResidue.Centroid;
            ReferenceResidueInfo = $"{refResidue.Name}{refResidue.SequenceNumber} (Chain {refResidue.ChainId})\n" +
                                   $"Atoms: {refResidue.Atoms.Count}\n" +
                                   $"Centroid: ({x:F2}, {y:F2}, {z:F2})";
        }
        else
        {
            ReferenceResidueInfo = $"Residue {residueNumber} not found in reference";
        }

        if (mutResidue != null)
        {
            var (x, y, z) = mutResidue.Centroid;
            MutantResidueInfo = $"{mutResidue.Name}{mutResidue.SequenceNumber} (Chain {mutResidue.ChainId})\n" +
                                $"Atoms: {mutResidue.Atoms.Count}\n" +
                                $"Centroid: ({x:F2}, {y:F2}, {z:F2})";
        }
        else
        {
            MutantResidueInfo = $"Residue {residueNumber} DELETED in mutant (ΔF508)";
        }

        if (ResidueIsMissing)
        {
            MutationSummary = $"ΔF508 — Phenylalanine at position {residueNumber} is deleted.\n" +
                              "This is the most common cystic fibrosis-causing mutation,\n" +
                              "affecting protein folding and chloride channel function.";

            FoldingAssessment = "The deletion of Phe-508 disrupts the local folding of NBD1,\n" +
                                "preventing proper domain-domain interactions required for\n" +
                                "CFTR maturation and trafficking to the cell surface.\n\n" +
                                "(Demo assessment — simplified structural analysis)";
        }
        else
        {
            MutationSummary = $"Residue {residueNumber} present in both structures.";
            FoldingAssessment = "Both structures contain this residue — compare neighborhood geometry.";
        }

        ReferenceNeighbors.Clear();
        if (refResidue != null)
        {
            foreach (var n in refNeighborhood.Take(15))
            {
                ReferenceNeighbors.Add(new NeighborResidueItem
                {
                    Label = $"{n.Name}{n.SequenceNumber}",
                    ChainId = n.ChainId,
                    Distance = refResidue.DistanceTo(n),
                    AtomCount = n.Atoms.Count
                });
            }
        }

        MutantNeighbors.Clear();
        var mutAnchor = mutResidue ?? mutant.AllResidues.FirstOrDefault(r => r.SequenceNumber == residueNumber - 1);
        if (mutAnchor != null)
        {
            foreach (var n in mutNeighborhood.Take(15))
            {
                MutantNeighbors.Add(new NeighborResidueItem
                {
                    Label = $"{n.Name}{n.SequenceNumber}",
                    ChainId = n.ChainId,
                    Distance = mutAnchor.DistanceTo(n),
                    AtomCount = n.Atoms.Count
                });
            }
        }

        var neighborDiff = refNeighborhood.Count - mutNeighborhood.Count;
        StructuralImpactSummary =
            $"Reference neighborhood: {refNeighborhood.Count} residues within 10Å\n" +
            $"Mutant neighborhood: {mutNeighborhood.Count} residues within 10Å\n" +
            $"Neighbor difference: {neighborDiff}\n" +
            $"RMSD: {comparison.RmsdDisplayText}\n" +
            $"Residue difference: {comparison.ResidueDifference}\n" +
            $"Missing in mutant: {comparison.MissingInMutant.Count}";
    }
}

public class NeighborResidueItem
{
    public string Label { get; set; } = string.Empty;
    public char ChainId { get; set; }
    public double Distance { get; set; }
    public int AtomCount { get; set; }

    public string DistanceDisplay => $"{Distance:F1}Å";
}
