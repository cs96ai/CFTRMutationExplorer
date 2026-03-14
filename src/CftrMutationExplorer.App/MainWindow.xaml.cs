using System.Windows;
using System.Windows.Controls;

namespace CftrMutationExplorer.App;

public partial class MainWindow : Window
{
    private const int MrnaTabIndex = 7;
    private GridLength _savedLeftWidth = new(300);
    private GridLength _savedRightWidth = new(320);

    public MainWindow()
    {
        InitializeComponent();
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabControl) return;

        bool isMrna = MainTabControl.SelectedIndex == MrnaTabIndex;
        SetSidePanelVisibility(!isMrna);
    }

    private void SetSidePanelVisibility(bool visible)
    {
        var vis = visible ? Visibility.Visible : Visibility.Collapsed;

        if (visible)
        {
            LeftColumn.Width = _savedLeftWidth;
            LeftColumn.MinWidth = 200;
            RightColumn.Width = _savedRightWidth;
            RightColumn.MinWidth = 200;
        }
        else
        {
            _savedLeftWidth = LeftColumn.Width;
            _savedRightWidth = RightColumn.Width;
            LeftColumn.MinWidth = 0;
            LeftColumn.Width = new GridLength(0);
            RightColumn.MinWidth = 0;
            RightColumn.Width = new GridLength(0);
        }

        LeftPanel.Visibility = vis;
        LeftSplitter.Visibility = vis;
        RightPanel.Visibility = vis;
        RightSplitter.Visibility = vis;
        LoadDemoButton.Visibility = vis;
        RunComparisonButton.Visibility = vis;

        LeftSplitterColumn.Width = visible ? GridLength.Auto : new GridLength(0);
        RightSplitterColumn.Width = visible ? GridLength.Auto : new GridLength(0);
    }
}
