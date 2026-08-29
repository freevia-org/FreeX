using Avalonia.Automation;
using Avalonia.Controls;
using Free.Shared.Ribbon;

namespace FreeP.App.Avalonia.Tests;

public sealed class PresentationTransitionGalleryTests
{
    [Fact]
    public async Task Transition_preview_gallery_matches_the_powerpoint_preview_density_ladder()
    {
        var ran = await HeadlessUiThread.Run(() =>
        {
            var expectedByState = new Dictionary<RibbonAdaptiveGroupState, string[]>
            {
                [RibbonAdaptiveGroupState.Full] = ["None", "Fade", "Push", "Wipe", "Split", "Reveal"],
                [RibbonAdaptiveGroupState.SmallWithLabels] = ["None", "Fade", "Push", "Wipe"],
                [RibbonAdaptiveGroupState.IconOnly] = ["None", "Fade", "Push"],
            };

            foreach (var (state, expected) in expectedByState)
            {
                var gallery = PresentationTransitionGallery
                    .Build(new RibbonCommandRegistry(), state)
                    .Should().BeOfType<StackPanel>().Subject;

                gallery.Children.OfType<Button>().Select(AutomationProperties.GetName)
                    .Should().Equal(expected);
            }

            PresentationTransitionGallery
                .Build(new RibbonCommandRegistry(), RibbonAdaptiveGroupState.SmallWithLabels, availableRibbonWidth: 750)
                .Should().BeOfType<StackPanel>().Subject
                .Children.OfType<Button>().Select(AutomationProperties.GetName)
                .Should().Equal("None", "Fade", "Push");
        });

        if (!ran)
            return;
    }
}
