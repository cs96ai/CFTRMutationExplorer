using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CftrMutationExplorer.App.ViewModels;

namespace CftrMutationExplorer.App.Views;

public partial class ProteinViewport : UserControl
{
    public ProteinViewport()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ViewportViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is ViewportViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewportViewModel.SceneModel))
        {
            // Delay so the visual tree has time to update with the new model
            Dispatcher.InvokeAsync(() =>
            {
                Viewport3D.ZoomExtents(400);
            }, DispatcherPriority.Background);
        }
    }

    private void ResetCamera_Click(object sender, RoutedEventArgs e)
    {
        Viewport3D.ResetCamera();
    }

    private void ZoomExtents_Click(object sender, RoutedEventArgs e)
    {
        Viewport3D.ZoomExtents(500);
    }

    private void ColorSchemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ViewportViewModel vm && sender is ComboBox cb)
        {
            vm.CurrentColorScheme = cb.SelectedIndex switch
            {
                0 => ColorScheme.ByChain,
                1 => ColorScheme.ByResidueType,
                2 => ColorScheme.ByTemperatureFactor,
                3 => ColorScheme.SingleColor,
                _ => ColorScheme.ByChain
            };
        }
    }
}
