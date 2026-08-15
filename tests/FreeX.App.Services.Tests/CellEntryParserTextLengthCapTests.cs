using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Real Excel enforces a hard 32,767-character cap on how much literal text a single cell can
/// hold, truncating any pasted/typed field that exceeds it rather than accepting it unbounded
/// (mirrors PasteCommandFactoryTextLengthCapTests' identical coverage of the external-clipboard
/// paste path). CellEntryParser.CreateCell/ParseScalarValue -- FreeX's shared typed-cell-entry
/// path used by both the WPF and Avalonia shells via WorkbookCellEditService/CellEntryCommitPlanner
/// -- previously had no such cap: typing text longer than the limit was accepted verbatim, so a
/// cell could carry text real Excel would truncate on open, producing silent data loss the user
/// believes was saved intact. Covers the General-format fallback branch, the leading-apostrophe
/// text-escape branch, and the Text ("@")-formatted-destination branch (which bypasses
/// ParseScalarValue's coercion entirely).
/// </summary>
public sealed class CellEntryParserTextLengthCapTests
{
    private const int ExcelCellTextLimit = 32767;
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_TruncatesOversizedGeneralFormatTextToExcelCellLimit()
    {
        var oversized = new string('a', ExcelCellTextLimit + 500);

        var cell = CellEntryParser.CreateCell(oversized, Anchor, useR1C1ReferenceStyle: false);

        var text = cell.Value.Should().BeOfType<TextValue>().Which.Value;
        text.Length.Should().Be(ExcelCellTextLimit);
        text.Should().Be(oversized[..ExcelCellTextLimit]);
    }

    [Fact]
    public void CreateCell_TruncatesOversizedApostropheEscapedTextToExcelCellLimit()
    {
        // The leading-apostrophe branch inside ParseScalarValue returns its own TextValue
        // independently of the plain-fallback branch below it -- pin it separately so a fix that
        // only capped the fallback branch would still be caught.
        var oversized = "'" + new string('b', ExcelCellTextLimit + 500);

        var cell = CellEntryParser.CreateCell(oversized, Anchor, useR1C1ReferenceStyle: false);

        var text = cell.Value.Should().BeOfType<TextValue>().Which.Value;
        text.Length.Should().Be(ExcelCellTextLimit);
    }

    [Fact]
    public void CreateCell_TruncatesOversizedTextFormattedDestinationToExcelCellLimit()
    {
        // The Text ("@")-formatted-destination early-return branch (IsTargetTextFormatted) bypasses
        // ParseScalarValue's coercion entirely and wraps the typed text into a TextValue directly,
        // so it needs its own independent length enforcement -- this pins that separate site.
        var oversized = new string('c', ExcelCellTextLimit + 500);
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);
        var textStyle = workbook.GetStyle(StyleId.Default).Clone();
        textStyle.NumberFormat = "@";
        var textStyleId = workbook.RegisterStyle(textStyle);
        sheet.SetCell(address, new Cell { Value = BlankValue.Instance, StyleId = textStyleId });

        var cell = CellEntryParser.CreateCell(oversized, address, useR1C1ReferenceStyle: false, workbook);

        var text = cell.Value.Should().BeOfType<TextValue>().Which.Value;
        text.Length.Should().Be(ExcelCellTextLimit);
        text.Should().Be(oversized[..ExcelCellTextLimit]);
    }

    [Fact]
    public void CreateCell_LeavesTextAtExactlyExcelCellLimitUntruncated()
    {
        // No-regression sibling: text landing exactly on the boundary (not over it) must survive
        // whole, and ordinary short typed text must be completely unaffected by the new cap.
        var exactlyAtLimit = new string('x', ExcelCellTextLimit);

        var cell = CellEntryParser.CreateCell(exactlyAtLimit, Anchor, useR1C1ReferenceStyle: false);

        var text = cell.Value.Should().BeOfType<TextValue>().Which.Value;
        text.Length.Should().Be(ExcelCellTextLimit);
        text.Should().Be(exactlyAtLimit);
    }

    [Fact]
    public void CreateCell_OrdinaryShortTypedTextIsUnchanged()
    {
        var cell = CellEntryParser.CreateCell("plain text", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("plain text");
    }
}
