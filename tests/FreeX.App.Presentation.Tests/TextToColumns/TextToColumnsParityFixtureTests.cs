using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsParityFixtureTests
{
    [Fact]
    public void DialogGeometryAndPreviewLimit_AreSharedAcrossCaptureHosts()
    {
        TextToColumnsDialogMetrics.WindowWidth.Should().Be(560);
        TextToColumnsDialogMetrics.WindowHeight.Should().Be(560);
        TextToColumnsDialogMetrics.MinimumWindowWidth.Should().Be(520);
        TextToColumnsDialogMetrics.MinimumWindowHeight.Should().Be(500);
        TextToColumnsDialogMetrics.PreviewRowLimit.Should().Be(3);
    }

    [Fact]
    public void SampleRows_AreTheStableFourRowFixtureUsedByBothCaptureHosts()
    {
        TextToColumnsParityFixture.SampleRows.Should().Equal(
            "North,Widget,120",
            "South,Gadget,85",
            "East,Sprocket,200",
            "West,Gizmo,64");
    }
}
