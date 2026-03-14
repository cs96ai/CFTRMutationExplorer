using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;
using Microsoft.Win32;

namespace CftrMutationExplorer.App.ViewModels;

public partial class ExportViewModel : ObservableObject
{
    private readonly IReportExportService _exportService;
    private readonly IAnnotationRepository _annotationRepository;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _canExport;

    private StructureComparisonResult? _comparisonResult;
    private Func<byte[]?>? _screenshotProvider;

    public ExportViewModel(IReportExportService exportService, IAnnotationRepository annotationRepository)
    {
        _exportService = exportService;
        _annotationRepository = annotationRepository;
    }

    public void SetComparisonResult(StructureComparisonResult? result)
    {
        _comparisonResult = result;
        CanExport = result != null;
    }

    public void SetScreenshotProvider(Func<byte[]?> provider)
    {
        _screenshotProvider = provider;
    }

    [RelayCommand]
    private async Task ExportReport()
    {
        if (_comparisonResult == null)
        {
            StatusText = "No comparison data available";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Comparison Report",
            Filter = "Markdown (*.md)|*.md|All Files (*.*)|*.*",
            DefaultExt = ".md",
            FileName = $"CFTR_Report_{DateTime.Now:yyyyMMdd_HHmmss}.md"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var annotations = await _annotationRepository.GetAllAsync();
                await _exportService.ExportComparisonReportAsync(dialog.FileName, _comparisonResult, annotations);
                StatusText = $"Report exported: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export error: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task ExportAnnotations()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Annotations",
            Filter = "CSV (*.csv)|*.csv|All Files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = $"CFTR_Annotations_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var annotations = await _annotationRepository.GetAllAsync();
                await _exportService.ExportAnnotationsCsvAsync(dialog.FileName, annotations);
                StatusText = $"Annotations exported: {Path.GetFileName(dialog.FileName)} ({annotations.Count} items)";
            }
            catch (Exception ex)
            {
                StatusText = $"Export error: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task ExportScreenshot()
    {
        if (_screenshotProvider == null)
        {
            StatusText = "Screenshot provider not available";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Viewport Screenshot",
            Filter = "PNG Image (*.png)|*.png",
            DefaultExt = ".png",
            FileName = $"CFTR_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var imageData = _screenshotProvider();
                if (imageData == null || imageData.Length == 0)
                {
                    StatusText = "No viewport image available";
                    return;
                }
                await _exportService.ExportScreenshotAsync(dialog.FileName, imageData);
                StatusText = $"Screenshot exported: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export error: {ex.Message}";
            }
        }
    }
}
