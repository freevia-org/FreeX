using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

/// <summary>
/// R105: this is the fifth find in the recurring points-vs-pixels unit-space bug class (r102
/// SetRowHeightCommand, r103 the Avalonia auto-fit clamp, r104 the WPF ribbon font-size path). Here
/// the offender was <c>GridResizeSizePlanner.MaximumRowSizePixels</c>, which clamped a pixel-space
/// interactive drag delta against 409.5 -- Excel's row-height ceiling expressed in POINTS -- instead
/// of that same ceiling converted to pixels (409.5 * 96/72 = 546, already exposed as
/// <see cref="AutoFitSizingService.MaximumRowHeight"/>). The practical symptom: dragging a row
/// border could never commit a height above ~409px, even though <see cref="SetRowHeightCommand"/>
/// (the command GridView's resize-drag handlers construct with the clamped pixel value, unchanged)
/// legally accepts up to 546px.
/// </summary>
public sealed class R105_GridResizeRowHeightPixelUnitTests
{
    [Fact]
    public void ClampRowSize_DragPastPointsCeiling_CommitsAbove409PixelsUpToTruePixelCeiling()
    {
        // A drag delta between the old (wrong) 409.5 cap and the true 546px pixel ceiling must pass
        // through unclamped -- this is exactly the range r105 found silently truncated.
        const double requestedPixels = 480;
        requestedPixels.Should().BeGreaterThan(409.5, "the regression only shows up above the old, wrong cap");

        GridResizeSizePlanner.ClampRowSize(requestedPixels).Should().Be(requestedPixels);
    }

    [Fact]
    public void ClampRowSize_AgreesWithSetRowHeightCommandsOwnPixelCeiling()
    {
        // Rule 4: clamping correctly is pointless if the command then rejects the clamped value.
        // Drive the real product entry point end to end -- clamp a drag that overshoots, then feed
        // the clamped pixel result straight into SetRowHeightCommand exactly as
        // MainWindow.GridStatus.cs's OnRowResized does -- and confirm the command accepts it.
        var clamped = GridResizeSizePlanner.ClampRowSize(GridResizeSizePlanner.MaximumRowSizePixels + 1000);
        clamped.Should().Be(AutoFitSizingService.MaximumRowHeight);

        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new FakeCommandContext(workbook);

        var command = new SetRowHeightCommand(sheet.Id, 1, 1, clamped);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.RowHeights[1].Should().Be(AutoFitSizingService.MaximumRowHeight);
    }

    private sealed class FakeCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
