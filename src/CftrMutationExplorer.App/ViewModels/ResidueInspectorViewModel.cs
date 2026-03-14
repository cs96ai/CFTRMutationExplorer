using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.App.ViewModels;

public partial class ResidueInspectorViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<ResidueDisplayItem> _residues = new();

    [ObservableProperty]
    private ObservableCollection<ResidueDisplayItem> _filteredResidues = new();

    [ObservableProperty]
    private ResidueDisplayItem? _selectedResidue;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _chainFilter;

    [ObservableProperty]
    private ObservableCollection<string> _availableChains = new();

    [ObservableProperty]
    private string _selectedResidueDetails = string.Empty;

    private ProteinStructure? _structure;

    public void LoadResidues(ProteinStructure structure)
    {
        _structure = structure;
        Residues.Clear();
        AvailableChains.Clear();
        AvailableChains.Add("All Chains");

        foreach (var chain in structure.Chains)
        {
            AvailableChains.Add($"Chain {chain.Id}");
            foreach (var residue in chain.Residues)
            {
                Residues.Add(new ResidueDisplayItem
                {
                    SequenceNumber = residue.SequenceNumber,
                    Name = residue.Name,
                    SingleLetter = residue.SingleLetterCode,
                    ChainId = residue.ChainId,
                    AtomCount = residue.Atoms.Count,
                    Centroid = residue.Centroid,
                    IsStandard = residue.IsStandardAminoAcid
                });
            }
        }

        ChainFilter = "All Chains";
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnChainFilterChanged(string? value) => ApplyFilter();

    partial void OnSelectedResidueChanged(ResidueDisplayItem? value)
    {
        if (value == null)
        {
            SelectedResidueDetails = string.Empty;
            return;
        }

        var lines = new List<string>
        {
            $"Residue: {value.Name} ({value.SingleLetter})",
            $"Sequence #: {value.SequenceNumber}",
            $"Chain: {value.ChainId}",
            $"Atoms: {value.AtomCount}",
            $"Standard AA: {(value.IsStandard ? "Yes" : "No")}",
            $"Centroid: ({value.Centroid.X:F2}, {value.Centroid.Y:F2}, {value.Centroid.Z:F2})"
        };

        if (_structure != null)
        {
            var residue = _structure.FindResidue(value.ChainId, value.SequenceNumber);
            if (residue != null)
            {
                var neighbors = _structure.FindResiduesNear(residue, 8.0);
                lines.Add($"Neighbors (8Å): {neighbors.Count}");
                foreach (var n in neighbors.Take(5))
                {
                    lines.Add($"  {n.Name}{n.SequenceNumber} ({n.ChainId}) — {residue.DistanceTo(n):F1}Å");
                }
                if (neighbors.Count > 5)
                    lines.Add($"  ... and {neighbors.Count - 5} more");
            }
        }

        SelectedResidueDetails = string.Join(Environment.NewLine, lines);
    }

    [RelayCommand]
    private void JumpToResidue508()
    {
        SearchText = "508";
        var target = FilteredResidues.FirstOrDefault(r => r.SequenceNumber == 508);
        if (target != null)
            SelectedResidue = target;
    }

    private void ApplyFilter()
    {
        FilteredResidues.Clear();

        foreach (var residue in Residues)
        {
            if (!string.IsNullOrEmpty(ChainFilter) && ChainFilter != "All Chains")
            {
                var filterChain = ChainFilter.Replace("Chain ", "");
                if (residue.ChainId.ToString() != filterChain)
                    continue;
            }

            if (!string.IsNullOrEmpty(SearchText))
            {
                var search = SearchText.Trim();
                var matchesNumber = int.TryParse(search, out var num) && residue.SequenceNumber == num;
                var matchesName = residue.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
                if (!matchesNumber && !matchesName)
                    continue;
            }

            FilteredResidues.Add(residue);
        }
    }
}

public class ResidueDisplayItem
{
    public int SequenceNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SingleLetter { get; set; } = string.Empty;
    public char ChainId { get; set; }
    public int AtomCount { get; set; }
    public (double X, double Y, double Z) Centroid { get; set; }
    public bool IsStandard { get; set; }

    public string DisplayLabel => $"{Name}{SequenceNumber} ({ChainId})";
}
