using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class ClipboardFeedbackPlannerTests
{
    [Theory]
    [InlineData(false, "ClipboardFeedback_CopyMultipleSelectionUnsupported", "Copy does not support multiple selected ranges yet.")]
    [InlineData(true, "ClipboardFeedback_CutMultipleSelectionUnsupported", "Cut does not support multiple selected ranges yet.")]
    public void MultiRangeSelectionUnsupported_ReturnsResourceBackedExactFeedback(
        bool isCut,
        string resourceKey,
        string fallback)
    {
        var descriptor = ClipboardFeedbackPlanner.MultiRangeSelectionUnsupported(isCut);

        descriptor.ResourceKey.Should().Be(resourceKey);
        descriptor.FallbackText.Should().Be(fallback);
    }

    [Fact]
    public void ClipboardReadFailed_ReturnsResourceBackedExactFeedback()
    {
        var descriptor = ClipboardFeedbackPlanner.ReadFailed;

        descriptor.ResourceKey.Should().Be("ClipboardFeedback_ReadFailed");
        descriptor.FallbackText.Should().Be("The clipboard is busy. Try pasting again.");
    }
}
