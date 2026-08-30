using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;

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
                .Should().Contain(["No Animation", "Appear", "Fade In", "More Effects"]);

            var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf);
            definition.FindTab("animations")!.FindGroup("animation-effects")!.Controls
                .Select(control => control.Label)
                .Should().Contain(["Fly In", "Wipe", "Zoom In"],
                    "the compact strip's More Effects menu is populated from the complete animation group");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void Animation_preview_gallery_keeps_three_effects_directly_selectable_when_compact()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var registry = FreePRibbonTestRegistry.Compose(editor);
        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf);
        var tab = definition.FindTab("animations")!;

        var gallery = PresentationAnimationGallery
            .Build(tab, registry, new RibbonStateStore(), RibbonAdaptiveGroupState.SmallWithLabels)
            .Should().BeOfType<StackPanel>().Subject;

        gallery.Children.OfType<Button>().Select(AutomationProperties.GetName)
            .Should().Equal("No Animation", "Appear", "Fade In", "More Effects");
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
