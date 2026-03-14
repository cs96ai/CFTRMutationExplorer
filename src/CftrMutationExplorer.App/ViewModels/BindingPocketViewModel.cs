using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.App.ViewModels;

public partial class BindingPocketViewModel : ObservableObject
{
    private readonly IBindingPocketService _pocketService;

    [ObservableProperty]
    private ObservableCollection<PocketDisplayItem> _pockets = new();

    [ObservableProperty]
    private PocketDisplayItem? _selectedPocket;

    [ObservableProperty]
    private string _selectedPocketDetails = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _clusterRadius = 8.0;

    [ObservableProperty]
    private int _minResidues = 4;

    private ProteinStructure? _structure;

    public BindingPocketViewModel(IBindingPocketService pocketService)
    {
        _pocketService = pocketService;
    }

    public void SetStructure(ProteinStructure? structure)
    {
        _structure = structure;
    }

    [RelayCommand]
    private void DetectPockets()
    {
        if (_structure == null)
        {
            StatusText = "Load a structure first";
            return;
        }

        var candidates = _pocketService.DetectCandidatePockets(_structure, ClusterRadius, MinResidues);

        Pockets.Clear();
        foreach (var pocket in candidates)
        {
            Pockets.Add(new PocketDisplayItem
            {
                Id = pocket.Id,
                Label = pocket.Label,
                ResidueCount = pocket.ResidueCount,
                Confidence = pocket.Confidence.ToString(),
                Volume = pocket.ApproximateVolume,
                CenterX = pocket.CenterOfMass.X,
                CenterY = pocket.CenterOfMass.Y,
                CenterZ = pocket.CenterOfMass.Z,
                Description = pocket.Description,
                ResidueList = string.Join(", ", pocket.Residues.Take(10).Select(r => $"{r.Name}{r.SequenceNumber}"))
                              + (pocket.Residues.Count > 10 ? $"... +{pocket.Residues.Count - 10} more" : "")
            });
        }

        HasResults = Pockets.Count > 0;
        StatusText = $"Found {Pockets.Count} candidate pocket(s) — demo heuristic";
    }

    partial void OnSelectedPocketChanged(PocketDisplayItem? value)
    {
        if (value == null)
        {
            SelectedPocketDetails = string.Empty;
            return;
        }

        SelectedPocketDetails =
            $"Pocket: {value.Label}\n" +
            $"Confidence: {value.Confidence}\n" +
            $"Residues: {value.ResidueCount}\n" +
            $"Volume: ~{value.Volume:F0} ų\n" +
            $"Center: ({value.CenterX:F1}, {value.CenterY:F1}, {value.CenterZ:F1})\n\n" +
            $"Residues: {value.ResidueList}\n\n" +
            $"{value.Description}";
    }
}

public class PocketDisplayItem
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int ResidueCount { get; set; }
    public string Confidence { get; set; } = string.Empty;
    public double Volume { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ResidueList { get; set; } = string.Empty;

    public string VolumeDisplay => $"~{Volume:F0} ų";
}
