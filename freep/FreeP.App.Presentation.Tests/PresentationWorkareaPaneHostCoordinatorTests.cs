using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationWorkareaPaneHostCoordinatorTests
{
    [Fact]
    public void Show_SequencesPortableStateProjectionNativeVisibilityAndAccessibility()
    {
        var panes = new PresentationWorkareaPaneSession();
        var calls = new List<string>();
        var coordinator = new PresentationWorkareaPaneHostCoordinator<string>(
            panes,
            PresentationWorkareaPane.SmartArtText,
            () =>
            {
                calls.Add("projection");
                return "rows";
            },
            visible => calls.Add($"visible:{visible}"),
            () => calls.Add("accessibility"));

        coordinator.Show().Should().Be("rows");

        panes.IsVisible(PresentationWorkareaPane.SmartArtText).Should().BeTrue();
        calls.Should().Equal("projection", "visible:True", "accessibility");
    }

    [Fact]
    public void Hide_UpdatesPortableAndNativeVisibilityBeforeAccessibility()
    {
        var panes = new PresentationWorkareaPaneSession();
        panes.Show(PresentationWorkareaPane.Selection);
        var calls = new List<string>();
        var coordinator = new PresentationWorkareaPaneHostCoordinator<object>(
            panes,
            PresentationWorkareaPane.Selection,
            () => new object(),
            visible => calls.Add($"visible:{visible}"),
            () => calls.Add("accessibility"));

        coordinator.Hide();

        panes.IsVisible(PresentationWorkareaPane.Selection).Should().BeFalse();
        calls.Should().Equal("visible:False", "accessibility");
    }
}
