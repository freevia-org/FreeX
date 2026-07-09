using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// Regression tests for round-16 Paste Special findings:
///   R16-paste-special-matrix-1: an arithmetic Operation must tile across a larger selected
///     destination (ClipboardPastePlanner.ShouldFillSelectedDestinationRange), not just hit the
///     anchor cell.
///   R16-paste-special-matrix-2: Divide must only produce #DIV/0! for an actual zero divisor, not
///     a tiny non-zero one (PasteArithmetic.ApplyOperation).
///   R16-paste-special-matrix-3: an arithmetic Operation paste must preserve the destination
///     cell's existing hyperlink/rich-text runs (PasteSpecialCellsCommand.Apply).
/// </summary>
public sealed class R16_paste_Tests
{
    // ── R16-paste-special-matrix-1 ────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldFillSelectedDestinationRange_TrueForNonCutOperationPaste()
    {
        // Before the fix, an Operation paste never expanded to fill the selected destination
        // range, so the UI paste path collapsed a multi-cell selection down to a single anchor
        // cell before even reaching PasteCommandFactory.
        ClipboardPastePlanner.ShouldFillSelectedDestinationRange(
                isCut: false,
                new PasteSpecialOptions(Operation: PasteSpecialOperation.Add))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void PasteSpecialAdd_OneCellClipboardOntoThreeCellDestination_AddsToAllThreeTiled()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceCell = Cell.FromValue(new NumberValue(5));
        sheet.SetCell(source, sourceCell);

        var destinationStart = new CellAddress(sheet.Id, 4, 4);
        var destinationEnd = new CellAddress(sheet.Id, 4, 6);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), Cell.FromValue(new NumberValue(3)));

        // Mirrors the real UI paste path: ShouldFillSelectedDestinationRange decides whether the
        // destination range passed to PasteCommandFactory is the full selection (here 1x3) or
        // collapsed to the single anchor cell.
        var expand = ClipboardPastePlanner.ShouldFillSelectedDestinationRange(
            isCut: false,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));
        var destinationRange = expand
            ? new GridRange(destinationStart, destinationEnd)
            : new GridRange(destinationStart, destinationStart);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(4, 4).Should().Be(new NumberValue(6));
        sheet.GetValue(4, 5).Should().Be(new NumberValue(7));
        sheet.GetValue(4, 6).Should().Be(new NumberValue(8));
    }

    // ── R16-paste-special-matrix-2 ────────────────────────────────────────────────────────────

    [Fact]
    public void PasteSpecialDivide_ByTinyNonZeroDivisor_YieldsRealQuotientNotDivByZero()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(dest, new NumberValue(10));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1e-15)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Divide));

        command.Apply(ctx).Success.Should().BeTrue();

        var value = sheet.GetValue(dest);
        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value!).Value.Should().Be(10.0 / 1e-15);
    }

    [Fact]
    public void PasteSpecialDivide_ByActualZero_StillReturnsDivByZero()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(dest, new NumberValue(10));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Divide));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(ErrorValue.DivByZero);
    }

    // ── R16-paste-special-matrix-3 ────────────────────────────────────────────────────────────

    [Fact]
    public void PasteSpecialAdd_OntoCellWithHyperlink_KeepsHyperlink()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var dest = new CellAddress(sheet.Id, 2, 2);

        sheet.SetCell(dest, Cell.FromValue(new NumberValue(10)));
        sheet.Hyperlinks[dest] = "https://foo.example";
        var destMetadata = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, "Foo tip", "");
        sheet.HyperlinkMetadata[dest] = destMetadata;

        var sourceCells = new[]
        {
            (source, Cell.FromValue(new NumberValue(5)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(source, source),
            sourceCells,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(new NumberValue(15));
        sheet.Hyperlinks.Should().ContainKey(dest);
        sheet.Hyperlinks[dest].Should().Be("https://foo.example");
        sheet.HyperlinkMetadata.Should().ContainKey(dest);
        sheet.HyperlinkMetadata[dest].Should().Be(destMetadata);

        command.Revert(ctx);

        sheet.Hyperlinks[dest].Should().Be("https://foo.example");
        sheet.HyperlinkMetadata[dest].Should().Be(destMetadata);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
