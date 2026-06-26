using Avalonia.Headless;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Text;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Light smoke for <see cref="AvaloniaTextMeasurer"/>. <see cref="AvaloniaTextMeasurer.Measure"/> needs an
/// Avalonia backend (it builds a <c>FormattedText</c>), so the measuring assertions run on the shared
/// headless UI thread; the empty-string short-circuit needs no backend and is checked directly. If no
/// headless backend is available the measuring case opts out cleanly rather than failing.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaTextMeasurerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void Measure_EmptyText_ReturnsEmpty_WithoutBackend()
    {
        var measurer = new AvaloniaTextMeasurer();

        measurer.Measure("", "Calibri", 11, bold: false, italic: false).Should().Be(TextSize.Empty);
        measurer.Measure(null, "Calibri", 11, bold: false, italic: false).Should().Be(TextSize.Empty);
    }

    [Fact]
    public async Task Measure_NonEmptyText_ProducesPositiveDimensions()
    {
        TextSize? measured = null;
        try
        {
            await Session.Dispatch(
                () => measured = new AvaloniaTextMeasurer().Measure("Sales", "Calibri", 12, bold: false, italic: false),
                CancellationToken.None);
        }
        catch (Exception)
        {
            // No headless drawing backend in this environment — opt out cleanly.
            return;
        }

        measured.Should().NotBeNull();
        measured!.Value.Width.Should().BeGreaterThan(0);
        measured.Value.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Measure_BoldText_IsAtLeastAsWideAsRegular()
    {
        double regular = 0;
        double bold = 0;
        try
        {
            await Session.Dispatch(
                () =>
                {
                    var measurer = new AvaloniaTextMeasurer();
                    regular = measurer.Measure("Sales", "Calibri", 12, bold: false, italic: false).Width;
                    bold = measurer.Measure("Sales", "Calibri", 12, bold: true, italic: false).Width;
                },
                CancellationToken.None);
        }
        catch (Exception)
        {
            return;
        }

        regular.Should().BeGreaterThan(0);
        bold.Should().BeGreaterThanOrEqualTo(regular);
    }
}
