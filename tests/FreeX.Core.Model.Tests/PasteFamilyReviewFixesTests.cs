using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for review findings G4, G25, G26, and G36 (paste-family hyperlink/rich-text/
/// arithmetic-tiling/merged-region fidelity).
/// </summary>
public sealed class PasteFamilyReviewFixesTests
{
    // ── G4: hyperlinks are transferred on Paste All / carried Paste Special modes, and stale
    // destination hyperlinks are cleared per mode. ─────────────────────────────────────────────

    [Fact]
    public void PasteCommandFactory_AllModeTransfersHyperlinkAndClearsStaleDestinationHyperlink()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceCell = Cell.FromValue(new TextValue("Visit"));
        sheet.SetCell(source, sourceCell);
        sheet.Hyperlinks[source] = "https://foo.example";
        var sourceMetadata = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, "Foo tip", "");
        sheet.HyperlinkMetadata[source] = sourceMetadata;

        // Destination already has an unrelated, now-stale hyperlink that must not survive the paste.
        var destinationCell = Cell.FromValue(new TextValue("old"));
        sheet.SetCell(destination, destinationCell);
        sheet.Hyperlinks[destination] = "https://stale.example";
        sheet.HyperlinkMetadata[destination] = new HyperlinkMetadata(HyperlinkTargetKind.EmailAddress, "Stale", "");

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Hyperlinks[destination].Should().Be("https://foo.example");
        sheet.HyperlinkMetadata[destination].Should().Be(sourceMetadata);

        command.Revert(ctx);

        sheet.Hyperlinks[destination].Should().Be("https://stale.example");
        sheet.HyperlinkMetadata[destination].Should().Be(new HyperlinkMetadata(HyperlinkTargetKind.EmailAddress, "Stale", ""));
    }

    [Fact]
    public void PasteCommandFactory_ValuesModeClearsStaleDestinationHyperlinkWithoutAddingNewOne()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        // Source has no hyperlink of its own.
        var sourceCell = Cell.FromValue(new TextValue("plain"));
        sheet.SetCell(source, sourceCell);

        var destinationCell = Cell.FromValue(new TextValue("old"));
        sheet.SetCell(destination, destinationCell);
        sheet.Hyperlinks[destination] = "https://stale.example";
        sheet.HyperlinkMetadata[destination] = new HyperlinkMetadata();

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(destination).Should().Be(new TextValue("plain"));
        sheet.Hyperlinks.Should().NotContainKey(destination);
        sheet.HyperlinkMetadata.Should().NotContainKey(destination);

        command.Revert(ctx);

        sheet.Hyperlinks[destination].Should().Be("https://stale.example");
        sheet.HyperlinkMetadata.Should().ContainKey(destination);
    }

    [Fact]
    public void PasteSpecialCellsCommand_DefaultContentKindTransfersHyperlink()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceCell = Cell.FromValue(new TextValue("Visit"));
        sheet.SetCell(source, sourceCell);
        sheet.Hyperlinks[source] = "https://foo.example";

        // SkipBlanks routes this through the Paste Special (non-tiled) branch rather than the
        // plain Ctrl+V branch, exercising PasteSpecialCellsCommand's hyperlink transfer.
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(SkipBlanks: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Hyperlinks[destination].Should().Be("https://foo.example");
    }

    // ── G25: EditCellsCommand (Paste Values/Formulas) clears stale destination rich-text runs. ──

    [Fact]
    public void PasteCommandFactory_ValuesModeClearsStaleDestinationRichTextRuns()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceCell = Cell.FromValue(new TextValue("Hello World"));
        sheet.SetCell(source, sourceCell);

        var destinationCell = Cell.FromValue(new TextValue("Existing text"));
        sheet.SetCell(destination, destinationCell);
        var staleRuns = new List<CellTextRun>
        {
            new("Existing", Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)
        };
        sheet.RichTextRuns[destination] = staleRuns;

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(destination).Should().Be(new TextValue("Hello World"));
        sheet.RichTextRuns.Should().NotContainKey(destination);

        command.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(destination);
        sheet.RichTextRuns[destination].Should().BeEquivalentTo(staleRuns);
    }

    [Fact]
    public void EditCellsCommand_OverwritingCellClearsStaleRichTextRunsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);

        sheet.SetCell(addr, Cell.FromValue(new TextValue("Existing text")));
        var staleRuns = new List<CellTextRun>
        {
            new("Existing", Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)
        };
        sheet.RichTextRuns[addr] = staleRuns;

        var command = new EditCellsCommand(sheet.Id, addr, new TextValue("new"));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("new"));
        sheet.RichTextRuns.Should().NotContainKey(addr);

        command.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(addr);
        sheet.RichTextRuns[addr].Should().BeEquivalentTo(staleRuns);
    }

    // ── G26: Paste Special with an arithmetic operation tiles across a larger destination. ──────

    [Fact]
    public void PasteCommandFactory_AddOperationTilesAcrossLargerSelectedDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceCell = Cell.FromValue(new NumberValue(5));
        sheet.SetCell(source, sourceCell);

        for (uint row = 4; row <= 6; row++)
            for (uint col = 4; col <= 6; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(row * 10 + col)));

        var destinationStart = new CellAddress(sheet.Id, 4, 4);
        var destinationEnd = new CellAddress(sheet.Id, 6, 6);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        for (uint row = 4; row <= 6; row++)
        {
            for (uint col = 4; col <= 6; col++)
            {
                var expected = (row * 10 + col) + 5;
                sheet.GetValue(row, col).Should().Be(new NumberValue(expected), $"cell ({row},{col}) should have had 5 added to it");
            }
        }
    }

    [Fact]
    public void PasteCommandFactory_AddOperationTiledUndoRestoresAllDestinationCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceCell = Cell.FromValue(new NumberValue(1));
        sheet.SetCell(source, sourceCell);

        var destinationStart = new CellAddress(sheet.Id, 4, 4);
        var destinationEnd = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(destinationStart, Cell.FromValue(new NumberValue(100)));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(destinationStart).Should().Be(new NumberValue(101));

        command.Revert(ctx);

        sheet.GetValue(destinationStart).Should().Be(new NumberValue(100));
        sheet.GetCell(new CellAddress(sheet.Id, 5, 5)).Should().BeNull();
    }

    // ── G36: pasting a copied merged region recreates the merge at the destination. ─────────────

    [Fact]
    public void PasteCommandFactory_AllModeRecreatesMergedRegionAtDestinationAndUndoRemoves()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var mergeStart = new CellAddress(sheet.Id, 1, 1);
        var mergeEnd = new CellAddress(sheet.Id, 2, 2);
        var mergeRange = new GridRange(mergeStart, mergeEnd);
        sheet.SetCell(mergeStart, Cell.FromValue(new TextValue("Header")));
        sheet.AddMergedRegion(mergeRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in mergeRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        var destination = new CellAddress(sheet.Id, 4, 4);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            mergeRange,
            sourceCells,
            destination,
            PasteCellsMode.All,
            default);

        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue(applyOutcome.ErrorMessage);

        var expectedDestinationRange = new GridRange(destination, new CellAddress(sheet.Id, 5, 5));
        sheet.MergedRegions.Should().Contain(expectedDestinationRange);
        sheet.GetValue(destination).Should().Be(new TextValue("Header"));

        command.Revert(ctx);

        sheet.MergedRegions.Should().NotContain(expectedDestinationRange);
    }

    [Fact]
    public void PasteCommandFactory_AllModeDoesNotRecreateMergeWhenDestinationAlreadyHasOverlappingMerge()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var mergeStart = new CellAddress(sheet.Id, 1, 1);
        var mergeEnd = new CellAddress(sheet.Id, 2, 2);
        var mergeRange = new GridRange(mergeStart, mergeEnd);
        sheet.SetCell(mergeStart, Cell.FromValue(new TextValue("Header")));
        sheet.AddMergedRegion(mergeRange);

        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var addr in mergeRange.AllCells())
            sourceCells.Add((addr, sheet.GetCell(addr)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));

        var destination = new CellAddress(sheet.Id, 4, 4);
        var existingDestinationMerge = new GridRange(destination, new CellAddress(sheet.Id, 5, 5));
        sheet.AddMergedRegion(existingDestinationMerge);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            mergeRange,
            sourceCells,
            destination,
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        // The pre-existing destination merge is left alone rather than duplicated or rejected.
        sheet.MergedRegions.Should().ContainSingle(r => r.Equals(existingDestinationMerge));
    }

    [Fact]
    public void PasteCommandFactory_AllModeWithoutSourceMergeDoesNotAddDestinationMerge()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceCell = Cell.FromValue(new TextValue("plain"));
        sheet.SetCell(source, sourceCell);

        var destination = new CellAddress(sheet.Id, 4, 4);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.MergedRegions.Should().BeEmpty();
    }
}
