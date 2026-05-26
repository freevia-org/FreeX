using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotChartTypeDialogResult(ChartType ChartType);

public sealed class PivotChartTypeDialog : Window
{
    private readonly TabControl _tabs = new();
    private readonly ListBox _recommendedGallery = new();
    private readonly ListBox _categoryList = new();
    private readonly ListBox _subtypeGallery = new();

    public ChartType SelectedChartType { get; private set; }
    public PivotChartTypeDialogResult Result { get; private set; }

    public PivotChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = "Change PivotChart Type";
        Width = 640;
        Height = 410;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        _tabs.Margin = new Thickness(0, 0, 0, 12);
        _tabs.Height = 290;
        _recommendedGallery.ItemsSource = ChartTypePickerPlanner.GetRecommendedGalleryChoices();
        _recommendedGallery.DisplayMemberPath = nameof(ChartTypeGalleryChoice.SubtypeName);
        _recommendedGallery.SelectedItem = ChartTypePickerPlanner.GetRecommendedGalleryChoices()
            .FirstOrDefault(choice => choice.Type == currentType);
        if (_recommendedGallery.SelectedItem is null)
            _recommendedGallery.SelectedIndex = 0;
        _tabs.Items.Add(new TabItem
        {
            Header = "_Recommended PivotCharts",
            Content = InsertChartDialog.CreateRecommendedChartsPanel(_recommendedGallery)
        });

        var allChartsPanel = InsertChartDialog.CreateAllChartsPanel(_categoryList, _subtypeGallery, currentType);
        allChartsPanel.ToolTip = "Chart categories and Chart subtype gallery match the Insert Chart picker.";
        _tabs.Items.Add(new TabItem { Header = "_All Charts", Content = allChartsPanel });
        stack.Children.Add(_tabs);
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(() =>
        {
            if (SelectedGalleryChoice() is { } option)
                SelectedChartType = option.Type;
            Result = CreateResult(SelectedChartType);
            DialogResult = true;
        }));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);

    private ChartTypeGalleryChoice? SelectedGalleryChoice() =>
        _tabs.SelectedIndex == 0
            ? _recommendedGallery.SelectedItem as ChartTypeGalleryChoice
            : _subtypeGallery.SelectedItem as ChartTypeGalleryChoice;

    private void FocusInitialKeyboardTarget()
    {
        _recommendedGallery.Focus();
        Keyboard.Focus(_recommendedGallery);
    }
}
