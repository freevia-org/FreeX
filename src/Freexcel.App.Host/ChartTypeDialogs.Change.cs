using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ChangeChartTypeDialogResult(ChartType ChartType);

public sealed class ChangeChartTypeDialog : Window
{
    private readonly ListBox _categoryList = new();
    private readonly ListBox _subtypeGallery = new();

    public ChartType SelectedChartType { get; private set; }
    public ChangeChartTypeDialogResult Result { get; private set; }

    public ChangeChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = "Change Chart Type";
        Width = 640;
        Height = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = false };
        var heading = new TextBlock
        {
            Text = "Choose a chart type",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var panel = InsertChartDialog.CreateAllChartsPanel(_categoryList, _subtypeGallery, currentType);
        panel.Height = 290;
        DockPanel.SetDock(panel, Dock.Top);
        root.Children.Add(panel);
        var buttons = CreateButtonRow();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static ChangeChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);

    private StackPanel CreateButtonRow() => InsertChartDialog.CreateButtonRow(() =>
    {
        if (_subtypeGallery.SelectedItem is ChartTypeGalleryChoice option)
            SelectedChartType = option.Type;
        Result = CreateResult(SelectedChartType);
        DialogResult = true;
    });

    private void FocusInitialKeyboardTarget()
    {
        _subtypeGallery.Focus();
        Keyboard.Focus(_subtypeGallery);
    }
}
