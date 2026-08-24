using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class RibbonContextualTabsTests
{
    [StaFact]
    public void MainWindow_shows_text_and_table_format_tabs_only_for_the_matching_selection()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Show();
            window.UpdateLayout();
            var tabs = VisualDescendants<TabControl>(window)
                .Single(control => control.Items.OfType<TabItem>().Any(tab => Equals(tab.Header, "File")));
            var textFormat = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Text Format"));
            var tableLayout = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Table Layout"));
            textFormat.Visibility.Should().Be(Visibility.Collapsed);
            tableLayout.Visibility.Should().Be(Visibility.Collapsed);

            var slide = window.Editor.CurrentSlide!;
            slide.Shapes.Add(new SlideShape
            {
                Id = 300,
                Kind = SlideShapeKind.AutoShape,
                TextBody = new TextBody(),
            });
            window.Editor.Select(300);
            textFormat.Visibility.Should().Be(Visibility.Visible);
            tableLayout.Visibility.Should().Be(Visibility.Collapsed);

            slide.Shapes.Add(new SlideShape
            {
                Id = 301,
                Kind = SlideShapeKind.Table,
                Table = new TableShape(),
            });
            window.Editor.Select(301);
            textFormat.Visibility.Should().Be(Visibility.Collapsed);
            tableLayout.Visibility.Should().Be(Visibility.Visible);

            window.Editor.ClearSelection();
            textFormat.Visibility.Should().Be(Visibility.Collapsed);
            tableLayout.Visibility.Should().Be(Visibility.Collapsed);
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in VisualDescendants<T>(child))
                yield return descendant;
        }
    }
}
