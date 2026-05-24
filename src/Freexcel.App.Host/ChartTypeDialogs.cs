using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record InsertChartDialogResult(ChartType ChartType, bool UseRecommendedLayout);

public sealed partial class InsertChartDialog : Window
{
    private readonly ListBox _recommendedGallery = new();
    private readonly ListBox _categoryList = new();
    private readonly ListBox _subtypeGallery = new();
    private readonly CheckBox _recommendedBox = new() { Content = "Use _recommended layout" };

    public InsertChartDialogResult Result { get; private set; } = CreateRecommendedResult();

    public InsertChartDialog()
    {
        Title = "Insert Chart";
        Width = 660;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = false };
        var tabs = new TabControl { Height = 310, Margin = new Thickness(0, 0, 0, 12) };
        _recommendedGallery.ItemsSource = ChartTypePickerPlanner.GetRecommendedGalleryChoices();
        _recommendedGallery.DisplayMemberPath = nameof(ChartTypeGalleryChoice.SubtypeName);
        _recommendedGallery.SelectedIndex = 0;
        tabs.Items.Add(new TabItem
        {
            Header = "Recommended Charts",
            Content = CreateRecommendedChartsPanel(_recommendedGallery)
        });
        tabs.Items.Add(new TabItem
        {
            Header = "All Charts",
            Content = CreateAllChartsPanel(_categoryList, _subtypeGallery)
        });
        DockPanel.SetDock(tabs, Dock.Top);
        root.Children.Add(tabs);
        _recommendedBox.IsChecked = true;
        _recommendedBox.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(_recommendedBox, Dock.Top);
        root.Children.Add(_recommendedBox);
        var buttons = CreateButtonRow(Accept);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static InsertChartDialogResult CreateResult(ChartType chartType) =>
        new(chartType, UseRecommendedLayout: false);

    public static InsertChartDialogResult CreateRecommendedResult() =>
        new(ChartTypePickerPlanner.GetRecommendedOptions().First().Type, UseRecommendedLayout: true);

    private void Accept()
    {
        var selected = SelectedGalleryChoice(_recommendedGallery, _subtypeGallery)?.Type ?? ChartType.Column;
        Result = new InsertChartDialogResult(selected, _recommendedBox.IsChecked == true);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _recommendedGallery.Focus();
        Keyboard.Focus(_recommendedGallery);
    }

    private static ChartTypeGalleryChoice? SelectedGalleryChoice(ListBox recommendedGallery, ListBox subtypeGallery) =>
        subtypeGallery.SelectedItem as ChartTypeGalleryChoice
        ?? recommendedGallery.SelectedItem as ChartTypeGalleryChoice;

}

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
