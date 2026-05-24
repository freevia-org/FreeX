using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record InsertChartDialogResult(ChartType ChartType, bool UseRecommendedLayout);

public sealed class InsertChartDialog : Window
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

    internal static Grid CreateRecommendedChartsPanel(ListBox gallery)
    {
        var grid = CreatePickerGrid();
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Choose a chart type",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        heading.Children.Add(CreateInlineHelp("Recently used and suggested for your data"));
        grid.Children.Add(heading);
        gallery.Margin = new Thickness(0, 34, 12, 0);
        AutomationProperties.SetName(gallery, "Chart subtype gallery");
        Grid.SetRow(gallery, 1);
        grid.Children.Add(gallery);
        var preview = CreatePreviewPanel("Preview", "Recommended chart preview");
        Grid.SetColumn(preview, 1);
        Grid.SetRowSpan(preview, 2);
        grid.Children.Add(preview);
        return grid;
    }

    internal static Grid CreateAllChartsPanel(
        ListBox categoryList,
        ListBox subtypeGallery,
        ChartType? selectedType = null)
    {
        var categories = ChartTypePickerPlanner.GetCategories();
        var grid = CreatePickerGrid();
        categoryList.ItemsSource = categories;
        categoryList.DisplayMemberPath = nameof(ChartTypePickerCategory.Name);
        categoryList.Width = 150;
        categoryList.Margin = new Thickness(0, 24, 12, 0);
        AutomationProperties.SetName(categoryList, "Chart categories");
        subtypeGallery.DisplayMemberPath = nameof(ChartTypeGalleryChoice.SubtypeName);
        subtypeGallery.Margin = new Thickness(0, 24, 12, 0);
        AutomationProperties.SetName(subtypeGallery, "Chart subtype gallery");
        categoryList.SelectionChanged += (_, _) =>
        {
            if (categoryList.SelectedItem is not ChartTypePickerCategory category)
                return;

            subtypeGallery.ItemsSource = ChartTypePickerPlanner.GetGalleryChoices(category.Name);
            subtypeGallery.SelectedIndex = 0;
        };

        var selectedCategory = categories.FirstOrDefault(category =>
            selectedType is not null && category.Options.Any(option => option.Type == selectedType.Value))
            ?? categories.FirstOrDefault();
        categoryList.SelectedItem = selectedCategory;
        if (selectedType is not null && subtypeGallery.ItemsSource is IEnumerable<ChartTypeGalleryChoice> choices)
        {
            subtypeGallery.SelectedItem = choices.FirstOrDefault(choice => choice.Type == selectedType.Value)
                ?? choices.FirstOrDefault();
        }

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "All Charts",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        heading.Children.Add(CreateInlineHelp("Choose a subtype to see how the chart will represent categories and values."));
        grid.Children.Add(heading);
        Grid.SetRow(categoryList, 1);
        grid.Children.Add(categoryList);
        Grid.SetColumn(subtypeGallery, 1);
        Grid.SetRow(subtypeGallery, 1);
        grid.Children.Add(subtypeGallery);
        var preview = CreatePreviewPanel("Preview", "Chart preview");
        Grid.SetColumn(preview, 2);
        Grid.SetRowSpan(preview, 2);
        grid.Children.Add(preview);
        return grid;
    }

    private static Grid CreatePickerGrid()
    {
        var grid = new Grid { Margin = new Thickness(12), MinHeight = 250 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static Border CreatePreviewPanel(string title, string body) =>
        new()
        {
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 24, 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 12)
                    },
                    new TextBlock
                    {
                        Text = body,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 14)
                    },
                    new TextBlock
                    {
                        Text = "Chart preview sample",
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    new Grid
                    {
                        Height = 92,
                        Children =
                        {
                            new Border
                            {
                                BorderBrush = SystemColors.ControlDarkBrush,
                                BorderThickness = new Thickness(0, 0, 0, 1),
                                VerticalAlignment = System.Windows.VerticalAlignment.Bottom
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                                Children =
                                {
                                    CreatePreviewBar(26),
                                    CreatePreviewBar(54),
                                    CreatePreviewBar(38),
                                    CreatePreviewBar(72)
                                }
                            }
                        }
                    }
                }
            }
        };

    private static Border CreatePreviewBar(double height) =>
        new()
        {
            Width = 22,
            Height = height,
            Margin = new Thickness(4, 0, 4, 0),
            Background = SystemColors.HighlightBrush
        };

    internal static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 76);
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
