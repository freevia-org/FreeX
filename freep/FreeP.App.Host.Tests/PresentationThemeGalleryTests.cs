using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeP.App.Host.Tests;

public sealed class PresentationThemeGalleryTests
{
    [StaFact]
    public void Design_theme_preview_gallery_exposes_the_built_in_themes_and_routes_to_the_existing_command()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Show();
            window.UpdateLayout();

            var tabs = VisualDescendants<TabControl>(window)
                .Single(control => control.Items.OfType<TabItem>().Any(tab => Equals(tab.Header, "Design")));
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Design"));
            window.UpdateLayout();

            var berlin = VisualDescendants<Button>(window)
                .Single(button => Equals(AutomationProperties.GetName(button), "Berlin"));
            berlin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            window.Editor.Presentation.Theme.Name.Should().Be("Berlin");
            VisualDescendants<Button>(window)
                .Where(button => !string.IsNullOrEmpty(AutomationProperties.GetName(button)))
                .Select(AutomationProperties.GetName)
                .Should().Contain(["Office Theme", "Berlin", "Facet", "Ion", "Slice"]);
            VisualDescendants<TextBlock>(window)
                .Count(text => Equals(text.Text, "Aa"))
                .Should().BeGreaterThanOrEqualTo(5,
                    "each built-in theme preview uses the recognizable PowerPoint-style type sample");
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
