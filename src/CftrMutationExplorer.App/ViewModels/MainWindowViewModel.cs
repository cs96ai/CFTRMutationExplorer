using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;
using CftrMutationExplorer.Infrastructure.Services.Mrna;

namespace CftrMutationExplorer.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IPdbParser _parser;
    private readonly IStructureComparisonService _comparisonService;

    [ObservableProperty]
    private StructureLoaderViewModel _referenceLoader;

    [ObservableProperty]
    private StructureLoaderViewModel _mutantLoader;

    [ObservableProperty]
    private ResidueInspectorViewModel _residueInspector;

    [ObservableProperty]
    private ComparisonSummaryViewModel _comparisonSummary;

    [ObservableProperty]
    private MutationAnalysisViewModel _mutationAnalysis;

    [ObservableProperty]
    private ViewportViewModel _viewport;

    [ObservableProperty]
    private AnnotationListViewModel _annotationList;

    [ObservableProperty]
    private ExportViewModel _export;

    [ObservableProperty]
    private BindingPocketViewModel _bindingPockets;

    [ObservableProperty]
    private MrnaDesignerViewModel _mrnaDesigner;

    [ObservableProperty]
    private Phase5ViewModel _phase5;

    [ObservableProperty]
    private string _statusMessage = "Ready — Load a protein structure to begin";

    [ObservableProperty]
    private bool _isComparisonAvailable;

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainWindowViewModel(
        IPdbParser parser,
        IStructureComparisonService comparisonService,
        IAnnotationRepository annotationRepository,
        IReportExportService reportExportService,
        IBindingPocketService bindingPocketService,
        PythonServiceManager pythonServiceManager,
        MrnaApiClient mrnaApiClient)
    {
        _parser = parser;
        _comparisonService = comparisonService;

        _referenceLoader = new StructureLoaderViewModel(parser, "Reference (Normal)");
        _mutantLoader = new StructureLoaderViewModel(parser, "Mutant (F508del)");
        _residueInspector = new ResidueInspectorViewModel();
        _comparisonSummary = new ComparisonSummaryViewModel();
        _mutationAnalysis = new MutationAnalysisViewModel();
        _viewport = new ViewportViewModel();
        _annotationList = new AnnotationListViewModel(annotationRepository);
        _export = new ExportViewModel(reportExportService, annotationRepository);
        _bindingPockets = new BindingPocketViewModel(bindingPocketService);
        _mrnaDesigner = new MrnaDesignerViewModel(pythonServiceManager, mrnaApiClient);
        _phase5 = new Phase5ViewModel(mrnaApiClient);

        _referenceLoader.StructureLoaded += OnStructureLoaded;
        _mutantLoader.StructureLoaded += OnStructureLoaded;

        _ = LoadDemoData();
    }

    private void OnStructureLoaded(object? sender, ProteinStructure structure)
    {
        if (sender == ReferenceLoader)
        {
            ResidueInspector.LoadResidues(structure);
            Viewport.SetReferenceStructure(structure);
            BindingPockets.SetStructure(structure);
            StatusMessage = $"Loaded reference: {structure.FileName} — {structure.AtomCount:N0} atoms, {structure.ResidueCount} residues";
        }
        else
        {
            Viewport.SetMutantStructure(structure);
            StatusMessage = $"Loaded mutant: {structure.FileName} — {structure.AtomCount:N0} atoms, {structure.ResidueCount} residues";
        }

        IsComparisonAvailable = ReferenceLoader.Structure != null && MutantLoader.Structure != null;

        if (IsComparisonAvailable)
        {
            RunComparison();
        }
    }

    [RelayCommand]
    private void SetSelectedTab(object? param)
    {
        if (param is int i)
            SelectedTabIndex = i;
        else if (param is string s && int.TryParse(s, out var n))
            SelectedTabIndex = n;
    }

    [RelayCommand]
    private void RunComparison()
    {
        if (ReferenceLoader.Structure == null || MutantLoader.Structure == null)
            return;

        try
        {
            var result = _comparisonService.Compare(ReferenceLoader.Structure, MutantLoader.Structure);
            ComparisonSummary.LoadResult(result);
            Export.SetComparisonResult(result);

            var neighborhood = _comparisonService.GetMutationNeighborhood(ReferenceLoader.Structure, 508);
            var mutantNeighborhood = _comparisonService.GetMutationNeighborhood(MutantLoader.Structure, 508);
            MutationAnalysis.LoadAnalysis(ReferenceLoader.Structure, MutantLoader.Structure, 508, neighborhood, mutantNeighborhood, result);

            StatusMessage = $"Comparison complete — RMSD: {result.RmsdDisplayText}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Comparison error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadDemoData()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dataPath = Path.Combine(basePath, "..", "..", "..", "..", "..", "data");

        if (!Directory.Exists(dataPath))
            dataPath = Path.Combine(basePath, "data");

        if (!Directory.Exists(dataPath))
        {
            var current = Directory.GetCurrentDirectory();
            dataPath = Path.Combine(current, "data");
        }

        // Walk up from base directory to find data folder
        if (!Directory.Exists(dataPath))
        {
            var dir = new DirectoryInfo(basePath);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "data");
                if (Directory.Exists(candidate))
                {
                    dataPath = candidate;
                    break;
                }
                dir = dir.Parent;
            }
        }

        var normalFile = Path.Combine(dataPath, "CFTR_Normal.pdb");
        var mutantFile = Path.Combine(dataPath, "CFTR_F508del.pdb");

        if (!File.Exists(normalFile) || !File.Exists(mutantFile))
        {
            StatusMessage = "Demo data not found. Please place CFTR_Normal.pdb and CFTR_F508del.pdb in the data folder.";
            return;
        }

        StatusMessage = "Loading demo data...";
        await ReferenceLoader.LoadFileAsync(normalFile);
        await MutantLoader.LoadFileAsync(mutantFile);
        StatusMessage = "Demo data loaded — both structures ready for analysis";
    }
}
