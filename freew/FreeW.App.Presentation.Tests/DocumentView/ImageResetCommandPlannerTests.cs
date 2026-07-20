using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests.DocumentView;

public sealed class ImageResetCommandPlannerTests
{
    [Fact]
    public void BuildNaturalSize_ConvertsOriginalPixelsAtSharedNinetySixDpiPolicy()
    {
        ImageResetCommandPlanner.BuildNaturalSize(200, 100, 240, 120)
            .Should().Be(new ImageResetSize(150, 75));
    }

    [Fact]
    public void BuildNaturalSize_UsesCurrentSizeWhenOriginalDimensionsAreUnavailable()
    {
        ImageResetCommandPlanner.BuildNaturalSize(0, 0, 240, 120)
            .Should().Be(new ImageResetSize(240, 120));
    }
}
