using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R26-paste-special-operation-deep-1/2/3: three related Paste Special gaps around
/// Operation/Skip-Blanks/Transpose combinations.
///
/// deep-1: "Paste Special > Values" (mode==Values) leaked the source's rich-text runs, hyperlinks,
/// and merged regions to the destination whenever Skip Blanks was also ticked, because
/// `specialCarriesFormatting` never checked `mode` -- only `Operation`/`ContentKind`. Fixed by
/// gating on `mode == PasteCellsMode.All`, matching the plain (non-special) paste path's identical
/// gate a few lines below.
///
/// deep-2: a tiled Paste Special (destination bigger than the copied block) with an arithmetic
/// Operation unconditionally recreated the source's merged region at every tile, even though an
/// Operation paste is only supposed to combine values and must leave destination merge structure
/// alone (matching the non-tiled path's existing Operation==None gate). Fixed by gating
/// `mergedRegionCommands` on `options.Operation == PasteSpecialOperation.None` too.
///
/// deep-3: "Values and number formats" was the only ContentKind that inherited source formatting
/// during an arithmetic Operation paste; "All except borders"/"All using Source theme"/"Values and
/// source formatting"/"Formulas and number formats" all silently collapsed to plain-value-only.
/// Fixed by queuing a follow-up PasteFormatsCommand with the same per-kind style merge
/// PasteCommandCellFactory.BuildPastedCell already does for a non-Operation paste, but skipped when
/// the arithmetic operation itself is a no-op (matching TryBuildCell's own no-op skip).
/// </summary>
public sealed class R26_PasteSpecialOperationDeepTests
{
    // ---- deep-1 -----------------------------------------------------------------------------

    [Fact]
    public void ValuesMode_SkipBlanks_DoesNotLeakRichTextHyperlinkOrMergedRegion()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source A1:B1 is a merged region, anchor A1 = rich-text/hyperlinked "Hello".
        var sourceAnchor = new CellAddress(sheet.Id, 1, 1);
        var sourceCovered = new CellAddress(sheet.Id, 1, 2);
        var sourceRange = new GridRange(sourceAnchor, sourceCovered);
        var sourceCell = Cell.FromValue(new TextValue("Hello"));
        sheet.SetCell(sourceAnchor, sourceCell);
        sheet.AddMergedRegion(sourceRange);
        sheet.RichTextRuns[sourceAnchor] = [new CellTextRun("H", true, null, null, null, null, null, null)];
        sheet.Hyperlinks[sourceAnchor] = "http://example.com";
        sheet.HyperlinkMetadata[sourceAnchor] = new HyperlinkMetadata();

        // Destination B3:C3, plain "World", no hyperlink.
        var destinationAnchor = new CellAddress(sheet.Id, 3, 2);
        var destinationCovered = new CellAddress(sheet.Id, 3, 3);
        var destinationRange = new GridRange(destinationAnchor, destinationCovered);
        sheet.SetCell(destinationAnchor, Cell.FromValue(new TextValue("World")));

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in sourceRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destinationRange,
            PasteCellsMode.Values,
            new PasteSpecialOptions(SkipBlanks: true));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destinationAnchor).Should().Be(new TextValue("Hello"));
        sheet.RichTextRuns.Should().NotContainKey(destinationAnchor, "Paste Values must strip rich-text runs");
        sheet.Hyperlinks.Should().NotContainKey(destinationAnchor, "Paste Values must strip hyperlinks");
        sheet.HyperlinkMetadata.Should().NotContainKey(destinationAnchor, "Paste Values must strip hyperlink metadata");
        sheet.MergedRegions.Should().NotContain(destinationRange, "Paste Values must not recreate the source's merge");
    }

    [Fact]
    public void AllMode_SkipBlanks_StillCarriesRichTextRuns_NoRegression()
    {
        // Sibling case: a plain "All" paste (mode==All, ContentKind==Default) combined with Skip
        // Blanks must keep carrying rich-text runs -- this already worked before the fix and must
        // keep working afterward.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceCell = Cell.FromValue(new TextValue("Hello"));
        sheet.SetCell(source, sourceCell);
        sheet.RichTextRuns[source] = [new CellTextRun("H", true, null, null, null, null, null, null)];

        var destination = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(destination, Cell.FromValue(new TextValue("World")));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(SkipBlanks: true));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new TextValue("Hello"));
        sheet.RichTextRuns.Should().ContainKey(destination);
        sheet.RichTextRuns[destination][0].Text.Should().Be("H");
    }

    // ---- deep-2 -----------------------------------------------------------------------------

    [Fact]
    public void TiledPaste_WithOperation_DoesNotRecreateMergedRegion()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source A1:B1 merged, anchor value 5.
        var mergeStart = new CellAddress(sheet.Id, 1, 1);
        var mergeEnd = new CellAddress(sheet.Id, 1, 2);
        var mergeRange = new GridRange(mergeStart, mergeEnd);
        sheet.SetCell(mergeStart, Cell.FromValue(new NumberValue(5)));
        // The covered (non-anchor) cell of a merge is normally left empty by the UI, but nothing in
        // the model stops it from holding a value; give it the same value as the anchor purely so
        // every tiled destination cell below combines deterministically (1 + 5 = 6), regardless of
        // which of the two source cells a given tile happens to wrap onto.
        sheet.SetCell(mergeEnd, Cell.FromValue(new NumberValue(5)));
        sheet.AddMergedRegion(mergeRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in mergeRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        // Destination is 2 rows x 4 cols (D1:G2), bigger than the 1x2 source, so it tiles.
        var destinationStart = new CellAddress(sheet.Id, 1, 4);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 2, 7));
        foreach (var addr in destinationRange.AllCells())
            sheet.SetCell(addr, Cell.FromValue(new NumberValue(1)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            mergeRange,
            sourceCells,
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Values combine arithmetically (1 + 5 = 6) at every tiled destination cell...
        foreach (var addr in destinationRange.AllCells())
            sheet.GetValue(addr).Should().Be(new NumberValue(6));

        // ...but no merge structure gets created anywhere in the destination.
        sheet.MergedRegions.Should().NotContain(r => r.Overlaps(destinationRange));
    }

    [Fact]
    public void TiledPaste_AllMode_NoOperation_StillRecreatesMergedRegion_NoRegression()
    {
        // Sibling case: a plain tiled "All" paste (Operation==None) of a merged source must still
        // recreate the merge at every tile -- this already worked before the fix (H41) and must
        // keep working afterward.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var mergeStart = new CellAddress(sheet.Id, 1, 1);
        var mergeEnd = new CellAddress(sheet.Id, 1, 2);
        var mergeRange = new GridRange(mergeStart, mergeEnd);
        sheet.SetCell(mergeStart, Cell.FromValue(new TextValue("Header")));
        sheet.AddMergedRegion(mergeRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in mergeRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        var destinationStart = new CellAddress(sheet.Id, 4, 1);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 5, 2));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            mergeRange,
            sourceCells,
            destinationRange,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 4, 2)));
        sheet.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 2)));
    }

    // ---- deep-3 -----------------------------------------------------------------------------

    [Fact]
    public void AllExceptBorders_WithOperation_MergesSourceFormattingButKeepsDestinationBorders()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(255, 0, 0),
            BorderTop = new CellBorder(BorderStyle.Thin, CellColor.Black)
        });
        var destinationStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = false,
            BorderTop = new CellBorder(BorderStyle.Thick, CellColor.Black)
        });

        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationCell = Cell.FromValue(new NumberValue(10));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(
                Operation: PasteSpecialOperation.Add,
                ContentKind: PasteSpecialContentKind.AllExceptBorders));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(15));

        var pastedStyle = wb.GetStyle(sheet.GetCell(destination)!.StyleId);
        pastedStyle.Bold.Should().BeTrue("non-border formatting should come from the source");
        pastedStyle.FillColor.Should().Be(new CellColor(255, 0, 0));
        pastedStyle.BorderTop.Style.Should().Be(BorderStyle.Thick, "borders must stay the destination's, not the source's");
    }

    [Fact]
    public void ValuesAndSourceFormatting_WithOperation_MergesSourceStyleWholesale()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            BorderTop = new CellBorder(BorderStyle.Thin, CellColor.Black)
        });
        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(10)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(
                Operation: PasteSpecialOperation.Add,
                ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(15));
        var pastedStyle = wb.GetStyle(sheet.GetCell(destination)!.StyleId);
        pastedStyle.Bold.Should().BeTrue();
        pastedStyle.BorderTop.Style.Should().Be(BorderStyle.Thin, "source formatting (including its own borders) is applied wholesale");
    }

    [Fact]
    public void FormulasAndNumberFormats_WithOperation_MergesOnlySourceNumberFormat()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "0.00%", Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "General", Bold = false });

        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationCell = Cell.FromValue(new NumberValue(10));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(
                Operation: PasteSpecialOperation.Add,
                ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(15));
        var pastedStyle = wb.GetStyle(sheet.GetCell(destination)!.StyleId);
        pastedStyle.NumberFormat.Should().Be("0.00%", "the number format should merge in from the source");
        pastedStyle.Bold.Should().BeFalse("only the number format merges -- other formatting stays the destination's");
    }

    [Fact]
    public void ValuesAndNumberFormats_WithOperation_StillMergesNumberFormat_NoRegression()
    {
        // Sibling case: ValuesAndNumberFormats + Operation already worked before this fix (it was
        // the one ContentKind PasteSpecialCellsCommand's TryBuildCell already special-cased) and
        // must keep working identically afterward.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(10)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(
                Operation: PasteSpecialOperation.Add,
                ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(15));
        wb.GetStyle(sheet.GetCell(destination)!.StyleId).NumberFormat.Should().Be("0.00%");
    }

    [Fact]
    public void DefaultContentKind_WithOperation_StaysPlainValueOnly_NoRegression()
    {
        // Sibling case: the baseline "All"/Values-with-no-special-ContentKind + Operation combo
        // must keep applying no formatting at all -- only the four named special content kinds
        // gained format-merging; the plain default must not regress into picking up source style.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true, NumberFormat = "0.00%" });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Bold = false, NumberFormat = "General" });

        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationCell = Cell.FromValue(new NumberValue(10));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(15));
        var pastedStyle = wb.GetStyle(sheet.GetCell(destination)!.StyleId);
        pastedStyle.Bold.Should().BeFalse("plain Operation paste must not pick up source formatting");
        pastedStyle.NumberFormat.Should().Be("General");
    }

    [Fact]
    public void AllExceptBorders_WithOperation_DestinationNonNumeric_NoOpLeavesFormatUntouchedToo()
    {
        // Guards the no-op path: when the arithmetic itself is a no-op (destination is non-numeric
        // text), Excel leaves the destination's value AND format entirely untouched -- the new
        // format-merge must not fire just because a source cell exists.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 0, 0) });
        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationStyle = wb.RegisterStyle(new CellStyle { Bold = false });
        var destinationCell = Cell.FromValue(new TextValue("keep me"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(
                Operation: PasteSpecialOperation.Add,
                ContentKind: PasteSpecialContentKind.AllExceptBorders));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new TextValue("keep me"));
        var pastedStyle = wb.GetStyle(sheet.GetCell(destination)!.StyleId);
        pastedStyle.Bold.Should().BeFalse("a no-op arithmetic paste must leave the destination's format untouched too");
    }

    [Fact]
    public void TiledPaste_AllExceptBorders_WithOperation_MergesEachTilesFormatting()
    {
        // The tiled path (destination bigger than the copied source) shares PasteSpecialCellsCommand
        // with the non-tiled path, so it needs the identical fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            BorderTop = new CellBorder(BorderStyle.Thin, CellColor.Black)
        });
        var sourceCell = Cell.FromValue(new NumberValue(2));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = false,
            BorderTop = new CellBorder(BorderStyle.Thick, CellColor.Black)
        });
        var destinationRange = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 2, 4));
        foreach (var addr in destinationRange.AllCells())
        {
            var cell = Cell.FromValue(new NumberValue(10));
            cell.StyleId = destinationStyle;
            sheet.SetCell(addr, cell);
        }

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions(
                Operation: PasteSpecialOperation.Add,
                ContentKind: PasteSpecialContentKind.AllExceptBorders));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        foreach (var addr in destinationRange.AllCells())
        {
            sheet.GetValue(addr).Should().Be(new NumberValue(12));
            var pastedStyle = wb.GetStyle(sheet.GetCell(addr)!.StyleId);
            pastedStyle.Bold.Should().BeTrue();
            pastedStyle.BorderTop.Style.Should().Be(BorderStyle.Thick);
        }
    }
}
