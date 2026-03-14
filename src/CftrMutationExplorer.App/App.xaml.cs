using System.Windows;
using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Infrastructure.Parsing;
using CftrMutationExplorer.Infrastructure.Persistence;
using CftrMutationExplorer.Infrastructure.Services;
using CftrMutationExplorer.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CftrMutationExplorer.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        DatabaseInitializer.EnsureCreated();

        var services = new ServiceCollection();

        services.AddSingleton<IPdbParser, PdbParser>();
        services.AddSingleton<IStructureComparisonService, StructureComparisonService>();
        services.AddSingleton<IAnnotationRepository, SqliteAnnotationRepository>();
        services.AddSingleton<ISessionPersistenceService, SqliteSessionPersistenceService>();
        services.AddSingleton<IReportExportService, ReportExportService>();
        services.AddSingleton<IBindingPocketService, BindingPocketService>();

        services.AddSingleton<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        var mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainWindowViewModel>()
        };
        mainWindow.Show();
    }
}
