using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class DrawingClipboardTextLayoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData("First\nSecond\nThird")]
    [InlineData("Many words that must wrap within the narrow drawing")]
    public Task ClipboardText_UsesMultipleLinesWithinPaddedBounds(string text) => Session.Dispatch(() =>
    {
        var single = DrawingObjectClipboardImageRenderer.CreateTextLayout("First", 88, 88);
        var wrapped = DrawingObjectClipboardImageRenderer.CreateTextLayout(text, 88, 88);
        wrapped.Height.Should().BeGreaterThan(single.Height);
        wrapped.MaxTextWidth.Should().Be(80);
        wrapped.MaxTextHeight.Should().Be(80);
        wrapped.Trimming.Should().Be(global::Avalonia.Media.TextTrimming.CharacterEllipsis);
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task ClipboardText_LimitsLongMultilineContentToAvailableHeight() => Session.Dispatch(() =>
    {
        var text = string.Join("\n", Enumerable.Repeat("First", 20));
        var bounded = DrawingObjectClipboardImageRenderer.CreateTextLayout(text, 88, 48);
        var full = DrawingObjectClipboardImageRenderer.CreateTextLayout(text, 88, 1000);
        bounded.Height.Should().BeLessThan(full.Height);
        bounded.Height.Should().BeLessThanOrEqualTo(40);
        return true;
    }, CancellationToken.None);
}
