using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class PresentationTransitionGalleryTests
{
    [StaFact]
    public void Transition_preview_gallery_exposes_common_effects_and_routes_to_the_existing_command()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Show();
            window.UpdateLayout();

            var tabs = VisualDescendants<TabControl>(window)
                .Single(control => control.Items.OfType<TabItem>().Any(tab => Equals(tab.Header, "Transitions")));
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Transitions"));
            window.UpdateLayout();

            var fade = VisualDescendants<Button>(window)
                .Single(button => Equals(AutomationProperties.GetName(button), "Fade"));
            fade.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            window.Editor.CurrentSlideTransition.Should().NotBeNull();
            window.Editor.CurrentSlideTransition!.Kind.Should().Be(TransitionKind.Fade);
            VisualDescendants<Button>(window)
                .Where(button => !string.IsNullOrEmpty(AutomationProperties.GetName(button)))
                .Select(AutomationProperties.GetName)
                .Should().Contain(["None", "Fade", "Push", "Wipe", "Split", "Reveal", "Cut", "Random Bars"]);
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
