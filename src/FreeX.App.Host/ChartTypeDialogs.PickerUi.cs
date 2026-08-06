using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

using static FreeX.App.Host.ChartDialogHelpers;
using PresentationChartTypePickerPlanner = FreeX.App.Presentation.Charts.Editing.ChartTypePickerPlanner;

namespace FreeX.App.Host;

public sealed partial class InsertChartDialog
{
    internal static Grid CreateRecommendedChartsPanel(ListBox gallery)
    {
        var panel = PresentationChartTypePickerPlanner.GetRecommendedPanel();
        var grid = CreatePickerGrid();
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = UiText.Get(panel.HeadingResourceKey),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        heading.Children.Add(CreateInlineHelp(UiText.Get(panel.HelpResourceKey)));
        grid.Children.Add(heading);
        gallery.Margin = new Thickness(0, 34, ChartTypeChangePlanner.PickerColumnGap, 0);
        AutomationProperties.SetName(gallery, UiText.Get(panel.SubtypeGalleryAutomationNameResourceKey));
        Grid.SetRow(gallery, 1);
        grid.Children.Add(gallery);
        var preview = CreatePreviewPanel(panel.Preview);
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
        var categories = ChartTypePickerPlanner.GetCategories(WpfResourceKeyTextResolver.Instance);
        var panel = PresentationChartTypePickerPlanner.GetAllChartsPanel();
        var grid = CreatePickerGrid();
        categoryList.ItemsSource = categories;
        categoryList.DisplayMemberPath = nameof(ChartTypePickerCategory.Name);
        categoryList.Width = ChartTypeChangePlanner.PickerCategoryWidth;
        categoryList.Margin = new Thickness(0, 24, ChartTypeChangePlanner.PickerColumnGap, 0);
        AutomationProperties.SetName(categoryList, UiText.Get(panel.CategoryListAutomationNameResourceKey!));
        subtypeGallery.DisplayMemberPath = nameof(ChartTypeGalleryChoice.SubtypeName);
        subtypeGallery.Margin = new Thickness(0, 24, ChartTypeChangePlanner.PickerColumnGap, 0);
        AutomationProperties.SetName(subtypeGallery, UiText.Get(panel.SubtypeGalleryAutomationNameResourceKey));
        categoryList.SelectionChanged += (_, _) =>
        {
            if (categoryList.SelectedItem is not ChartTypePickerCategory category)
                return;

            subtypeGallery.ItemsSource = ChartTypePickerPlanner.GetGalleryChoices(category.Name, WpfResourceKeyTextResolver.Instance);
            subtypeGallery.SelectedIndex = 0;
        };

        var selectedCategory = SelectInitialCategory(categories, selectedType);
        categoryList.SelectedItem = selectedCategory;
        if (selectedType is not null && subtypeGallery.ItemsSource is IEnumerable<ChartTypeGalleryChoice> choices)
        {
            subtypeGallery.SelectedItem = SelectInitialGalleryChoice(choices, selectedType.Value);
        }

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = UiText.Get(panel.HeadingResourceKey),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        heading.Children.Add(CreateInlineHelp(UiText.Get(panel.HelpResourceKey)));
        grid.Children.Add(heading);
        Grid.SetRow(categoryList, 1);
        grid.Children.Add(categoryList);
        Grid.SetColumn(subtypeGallery, 1);
        Grid.SetRow(subtypeGallery, 1);
        grid.Children.Add(subtypeGallery);
        var preview = CreatePreviewPanel(panel.Preview);
        Grid.SetColumn(preview, 2);
        Grid.SetRowSpan(preview, 2);
        grid.Children.Add(preview);
        return grid;
    }

    private static ChartTypePickerCategory? SelectInitialCategory(
        IReadOnlyList<ChartTypePickerCategory> categories,
        ChartType? selectedType)
    {
        if (selectedType is not null)
        {
            foreach (var category in categories)
                if (category.Options.Any(option => option.Type == selectedType.Value))
                    return category;
        }

        return categories.Count > 0 ? categories[0] : null;
    }

    private static ChartTypeGalleryChoice? SelectInitialGalleryChoice(
        IEnumerable<ChartTypeGalleryChoice> choices,
        ChartType selectedType)
    {
        ChartTypeGalleryChoice? fallback = null;
        foreach (var choice in choices)
        {
            fallback ??= choice;
            if (choice.Type == selectedType)
                return choice;
        }

        return fallback;
    }

    private static Grid CreatePickerGrid()
    {
        var grid = new Grid { Margin = new Thickness(ChartTypeChangePlanner.PickerColumnGap), MinHeight = 250 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChartTypeChangePlanner.PickerPreviewWidth) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static Border CreatePreviewPanel(FreeX.App.Presentation.Charts.Editing.ChartTypePickerPreviewDescriptor preview) =>
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
                        Text = UiText.Get(preview.TitleResourceKey),
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 12)
                    },
                    new TextBlock
                    {
                        Text = UiText.Get(preview.BodyResourceKey),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 14)
                    },
                    new TextBlock
                    {
                        Text = UiText.Get(preview.SampleLabelResourceKey),
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
        DialogButtonRowFactory.Create(accept, buttonWidth: ChartTypeChangePlanner.PickerButtonWidth);
}
