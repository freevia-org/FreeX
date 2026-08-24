using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeP.App.Host.Tests;

public sealed class PresentationAnimationGalleryTests
{
    [StaFact]
    public void Animation_preview_gallery_exposes_the_common_effects_and_more_menu()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Show();
            window.UpdateLayout();

            var tabs = VisualDescendants<TabControl>(window)
                .Single(control => control.Items.OfType<TabItem>().Any(tab => Equals(tab.Header, "Animations")));
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Animations"));
            window.UpdateLayout();

            VisualDescendants<Button>(window)
                .Where(button => !string.IsNullOrEmpty(AutomationProperties.GetName(button)))
                .Select(AutomationProperties.GetName)
                .Should().Contain(["No Animation", "Appear", "Fade In", "Fly In", "Wipe", "Zoom In", "More Effects"]);
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
