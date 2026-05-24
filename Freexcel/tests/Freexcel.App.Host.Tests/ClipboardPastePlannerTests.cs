using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ClipboardPastePlannerTests
{
    [Theory]
    [InlineData(PasteMode.All, PasteCellsMode.All)]
    [InlineData(PasteMode.Values, PasteCellsMode.Values)]
    [InlineData(PasteMode.Formulas, PasteCellsMode.Formulas)]
    [InlineData(PasteMode.Formats, PasteCellsMode.Formats)]
    public void ToCorePasteMode_MapsUiModeToCommandMode(PasteMode mode, PasteCellsMode expected)
    {
        ClipboardPastePlanner.ToCorePasteMode(mode).Should().Be(expected);
    }

    [Fact]
    public void ShouldClearCutSourceAfterPaste_ClearsCutOnlyAfterNonOverlappingMovePaste()
    {
        var sheetId = SheetId.New();
        var source = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 3, 3));
        var target = new GridRange(new CellAddress(sheetId, 8, 8), new CellAddress(sheetId, 8, 8));

        ClipboardPastePlanner.ShouldClearCutSourceAfterPaste(
                isCut: true,
                source,
                target,
                PasteMode.All,
                default,
                keepColumnWidths: false)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(false, PasteMode.All, false)]
    [InlineData(true, PasteMode.Formats, false)]
    [InlineData(true, PasteMode.All, true)]
    public void ShouldClearCutSourceAfterPaste_KeepsSourceForCopyFormatsAndColumnWidthPaste(
        bool isCut,
        PasteMode mode,
        bool keepColumnWidths)
    {
        var sheetId = SheetId.New();
        var source = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 3, 3));
        var target = new GridRange(new CellAddress(sheetId, 8, 8), new CellAddress(sheetId, 8, 8));

        ClipboardPastePlanner.ShouldClearCutSourceAfterPaste(
                isCut,
                source,
                target,
                mode,
                default,
                keepColumnWidths)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ShouldClearCutSourceAfterPaste_UsesTransposedPastedFootprintForOverlapCheck()
    {
        var sheetId = SheetId.New();
        var source = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 3));
        var target = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

        ClipboardPastePlanner.ShouldClearCutSourceAfterPaste(
                isCut: true,
                source,
                target,
                PasteMode.All,
                new PasteSpecialOptions(Transpose: true),
                keepColumnWidths: false)
            .Should()
            .BeFalse("the transposed 2x3 paste footprint overlaps the original cut range");
    }

    [Theory]
    [InlineData("Freexcel copy", "Freexcel copy", true)]
    [InlineData("Freexcel copy", "External app copy", false)]
    [InlineData("Freexcel copy", "", false)]
    [InlineData("Freexcel copy", null, true)]
    public void ShouldUseInternalClipboard_RejectsStaleInternalCopyWhenSystemClipboardChanged(
        string internalText,
        string? currentClipboardText,
        bool expected)
    {
        ClipboardPastePlanner.ShouldUseInternalClipboard(internalText, currentClipboardText)
            .Should()
            .Be(expected);
    }
}
