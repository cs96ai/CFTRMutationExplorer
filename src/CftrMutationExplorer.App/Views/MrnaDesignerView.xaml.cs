using System.Windows.Controls;

namespace CftrMutationExplorer.App.Views;

public partial class MrnaDesignerView : UserControl
{
    public MrnaDesignerView()
    {
        InitializeComponent();
        DebugLogTextBox.TextChanged += (_, _) =>
        {
            DebugLogTextBox.ScrollToEnd();
        };
    }
}
