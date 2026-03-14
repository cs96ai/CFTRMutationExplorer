using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Core.Interfaces;

public interface IReportExportService
{
    Task ExportAnnotationsCsvAsync(string filePath, List<Annotation> annotations);
    Task ExportComparisonReportAsync(string filePath, StructureComparisonResult comparison, List<Annotation>? annotations = null);
    Task ExportScreenshotAsync(string filePath, byte[] imageData);
}
