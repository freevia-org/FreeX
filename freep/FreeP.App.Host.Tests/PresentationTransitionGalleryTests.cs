using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class PresentationTransitionGalleryTests
{
    [StaFact]
    public void Transition_preview_gallery_exposes_common_effects_and_routes_to_the_existing_command()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var registry = FreePRibbonTestRegistry.Compose(editor);
        var gallery = PresentationTransitionGallery.Build(registry).Should().BeOfType<StackPanel>().Subject;
        var buttons = gallery.Children.OfType<Button>().ToArray();

        buttons.Single(button => Equals(AutomationProperties.GetName(button), "Fade"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        editor.CurrentSlideTransition.Should().NotBeNull();
        editor.CurrentSlideTransition!.Kind.Should().Be(TransitionKind.Fade);
        buttons.Select(AutomationProperties.GetName)
            .Should().Contain(["None", "Fade", "Push", "Wipe", "Split", "Reveal", "Cut", "Random Bars"]);
    }
}
