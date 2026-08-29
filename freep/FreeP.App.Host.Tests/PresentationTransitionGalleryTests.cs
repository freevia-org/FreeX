using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.Ribbon;
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
            .Should().Equal("None", "Fade", "Push", "Wipe", "Split", "Reveal");
    }

    [StaFact]
    public void Transition_preview_gallery_matches_the_powerpoint_preview_density_ladder()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var registry = FreePRibbonTestRegistry.Compose(editor);

        var expectedByState = new Dictionary<RibbonAdaptiveGroupState, string[]>
        {
            [RibbonAdaptiveGroupState.Full] = ["None", "Fade", "Push", "Wipe", "Split", "Reveal"],
            [RibbonAdaptiveGroupState.SmallWithLabels] = ["None", "Fade", "Push", "Wipe"],
            [RibbonAdaptiveGroupState.IconOnly] = ["None", "Fade", "Push"],
        };

        foreach (var (state, expected) in expectedByState)
        {
            var gallery = PresentationTransitionGallery
                .Build(registry, state)
                .Should().BeOfType<StackPanel>().Subject;

            gallery.Children.OfType<Button>().Select(AutomationProperties.GetName)
                .Should().Equal(expected);
        }

        PresentationTransitionGallery
            .Build(registry, RibbonAdaptiveGroupState.SmallWithLabels, availableRibbonWidth: 750)
            .Should().BeOfType<StackPanel>().Subject
            .Children.OfType<Button>().Select(AutomationProperties.GetName)
            .Should().Equal("None", "Fade", "Push");
    }
}
