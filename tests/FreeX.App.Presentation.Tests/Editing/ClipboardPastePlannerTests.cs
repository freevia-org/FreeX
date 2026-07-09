using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

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
    [InlineData("FreeX copy", "FreeX copy", true)]
    [InlineData("FreeX copy", "External app copy", false)]
    [InlineData("FreeX copy", "", false)]
    [InlineData("FreeX copy", null, true)]
    public void ShouldUseInternalClipboard_RejectsStaleInternalCopyWhenSystemClipboardChanged(
        string internalText,
        string? currentClipboardText,
        bool expected)
    {
        ClipboardPastePlanner.ShouldUseInternalClipboard(internalText, currentClipboardText)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData(PasteMode.All, "A\tB\r\n1\t2", true, false)]
    [InlineData(PasteMode.All, "Single cell", true, false)]
    [InlineData(PasteMode.All, "", true, true)]
    [InlineData(PasteMode.All, "   ", true, true)]
    [InlineData(PasteMode.All, null, true, true)]
    [InlineData(PasteMode.Values, null, true, false)]
    [InlineData(PasteMode.Formats, null, true, false)]
    [InlineData(PasteMode.Formulas, null, true, false)]
    public void ShouldPasteClipboardImageForNormalPaste_PrefersTabularTextOverImage(
        PasteMode mode,
        string? clipboardText,
        bool hasImage,
        bool expected)
    {
        ClipboardPastePlanner.ShouldPasteClipboardImageForNormalPaste(mode, clipboardText, hasImage)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ShouldPreserveClipboardVisualAfterPaste_KeepsCopyModeButClearsCutMode(
        bool isCut,
        bool expected)
    {
        ClipboardPastePlanner.ShouldPreserveClipboardVisualAfterPaste(isCut)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ShouldFillSelectedDestinationRange_AllowsCopiedCellsAndOperationsButKeepsCutAtSourceFootprint()
    {
        ClipboardPastePlanner.ShouldFillSelectedDestinationRange(isCut: false, default)
            .Should()
            .BeTrue();
        ClipboardPastePlanner.ShouldFillSelectedDestinationRange(isCut: true, default)
            .Should()
            .BeFalse();
        // An arithmetic Operation must still tile/fill the selected destination range, exactly like
        // a plain paste — Excel applies Add/Subtract/Multiply/Divide cell-by-cell across the whole
        // selection, not just the anchor cell (R16-paste-special-matrix-1).
        ClipboardPastePlanner.ShouldFillSelectedDestinationRange(
                isCut: false,
                new PasteSpecialOptions(Operation: PasteSpecialOperation.Add))
            .Should()
            .BeTrue();
        ClipboardPastePlanner.ShouldFillSelectedDestinationRange(
                isCut: false,
                new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats))
            .Should()
            .BeFalse();
    }
}
