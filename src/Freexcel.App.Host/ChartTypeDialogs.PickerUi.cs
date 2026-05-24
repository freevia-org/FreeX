using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed partial class InsertChartDialog
{
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
