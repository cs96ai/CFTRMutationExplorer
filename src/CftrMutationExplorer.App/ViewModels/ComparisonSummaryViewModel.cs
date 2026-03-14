using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.App.ViewModels;

public partial class ComparisonSummaryViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _referenceFileName = string.Empty;

    [ObservableProperty]
    private string _mutantFileName = string.Empty;

    [ObservableProperty]
    private int _referenceChainCount;

    [ObservableProperty]
    private int _mutantChainCount;

    [ObservableProperty]
    private int _referenceResidueCount;

    [ObservableProperty]
    private int _mutantResidueCount;

    [ObservableProperty]
    private int _referenceAtomCount;

    [ObservableProperty]
    private int _mutantAtomCount;

    [ObservableProperty]
    private string _rmsdDisplay = "N/A";

    [ObservableProperty]
    private int _residueDifference;

    [ObservableProperty]
    private int _atomDifference;

    [ObservableProperty]
    private int _missingInMutantCount;

    [ObservableProperty]
    private int _missingInReferenceCount;

    [ObservableProperty]
    private int _alteredResidueCount;

    [ObservableProperty]
    private ObservableCollection<ResidueComparisonEntry> _residueComparisons = new();

    [ObservableProperty]
    private ObservableCollection<string> _warnings = new();

    [ObservableProperty]
    private ObservableCollection<string> _missingInMutant = new();

    [ObservableProperty]
    private ObservableCollection<string> _missingInReference = new();

    private StructureComparisonResult? _result;

    public void LoadResult(StructureComparisonResult result)
    {
        _result = result;
        HasResult = true;

        ReferenceFileName = result.ReferenceFileName;
        MutantFileName = result.MutantFileName;
        ReferenceChainCount = result.ReferenceChainCount;
        MutantChainCount = result.MutantChainCount;
        ReferenceResidueCount = result.ReferenceResidueCount;
        MutantResidueCount = result.MutantResidueCount;
        ReferenceAtomCount = result.ReferenceAtomCount;
        MutantAtomCount = result.MutantAtomCount;
        RmsdDisplay = result.RmsdDisplayText;
        ResidueDifference = result.ResidueDifference;
        AtomDifference = result.AtomDifference;
        MissingInMutantCount = result.MissingInMutant.Count;
        MissingInReferenceCount = result.MissingInReference.Count;
        AlteredResidueCount = result.ResidueComparisons.Count(e => e.IsAltered);

        ResidueComparisons.Clear();
        foreach (var entry in result.ResidueComparisons)
            ResidueComparisons.Add(entry);

        Warnings.Clear();
        foreach (var w in result.Warnings)
            Warnings.Add(w);

        MissingInMutant.Clear();
        foreach (var m in result.MissingInMutant)
            MissingInMutant.Add(m);

        MissingInReference.Clear();
        foreach (var m in result.MissingInReference)
            MissingInReference.Add(m);
    }
}
