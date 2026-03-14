using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;
using Microsoft.Win32;

namespace CftrMutationExplorer.App.ViewModels;

public partial class StructureLoaderViewModel : ObservableObject
{
    private readonly IPdbParser _parser;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private ProteinStructure? _structure;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _loadProgress;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasStructure;

    [ObservableProperty]
    private string _structureSummary = "No structure loaded";

    private CancellationTokenSource? _loadCts;

    public event EventHandler<ProteinStructure>? StructureLoaded;

    public StructureLoaderViewModel(IPdbParser parser, string label)
    {
        _parser = parser;
        _label = label;
    }

    [RelayCommand]
    private async Task BrowseAndLoad()
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Open PDB File — {Label}",
            Filter = "PDB Files (*.pdb)|*.pdb|All Files (*.*)|*.*",
            DefaultExt = ".pdb"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFileAsync(dialog.FileName);
        }
    }

    public async Task LoadFileAsync(string filePath)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        IsLoading = true;
        LoadProgress = 0;
        ErrorMessage = null;
        FileName = Path.GetFileName(filePath);

        try
        {
            var progress = new Progress<int>(p => LoadProgress = p);
            var result = await Task.Run(
                () => _parser.ParseAsync(filePath, progress, _loadCts.Token),
                _loadCts.Token);

            result.FilePath = filePath;
            Structure = result;
            HasStructure = true;
            StructureSummary = $"{result.ChainCount} chains · {result.ResidueCount} residues · {result.AtomCount:N0} atoms";

            StructureLoaded?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Loading cancelled";
            StructureSummary = "Loading cancelled";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StructureSummary = $"Error: {ex.Message}";
            HasStructure = false;
            Structure = null;
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 100;
        }
    }

    [RelayCommand]
    private void CancelLoad()
    {
        _loadCts?.Cancel();
    }
}
