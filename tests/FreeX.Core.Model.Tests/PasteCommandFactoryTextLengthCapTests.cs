using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Real Excel enforces a hard 32,767-character cap on how much literal text a single cell can
/// hold, truncating any pasted/typed field that exceeds it rather than accepting it unbounded.
/// PasteCommandFactory.ParseClipboardValue (the external-clipboard paste coercion) previously had
/// no such cap: an oversized field was wrapped into a TextValue verbatim, so the resulting
/// workbook could save an XLSX cell whose text real Excel would then truncate/reject on open --
/// silent data loss the user believes was saved intact. Covers both the coerced-value path
/// (ParseClipboardValue, reached for a General-formatted destination) and the raw literal-text path
/// (ExternalTextPasteValuesCommand's Text("@")-formatted-destination / preserveText branch, which
/// bypasses ParseClipboardValue entirely and wraps the pasted field directly into a TextValue).
/// </summary>
public sealed class PasteCommandFactoryTextLengthCapTests
{
    private const int ExcelCellTextLimit = 32767;

    [Fact]
    public void ExternalTextPaste_TruncatesOversizedGeneralDestinationTextToExcelCellLimit()
    {
        var oversized = new string('a', ExcelCellTextLimit + 500);
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[oversized]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var value = sheet.GetValue(address);
        value.Should().BeOfType<TextValue>();
        var text = ((TextValue)value).Value;
        text.Length.Should().Be(ExcelCellTextLimit);
        text.Should().Be(oversized[..ExcelCellTextLimit]);
    }

    [Fact]
    public void ExternalTextPaste_TruncatesOversizedTextFormattedDestinationToExcelCellLimit()
    {
        // The Text ("@") destination-format branch (ExternalTextPasteValuesCommand.Apply) bypasses
        // ParseClipboardValue's coercion entirely and wraps the pasted field into a TextValue
        // directly, so it needs its own independent length enforcement -- this pins that second
        // site, not just the ParseClipboardValue coercion path covered above.
        var oversized = new string('b', ExcelCellTextLimit + 500);
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var textStyle = wb.GetStyle(StyleId.Default).Clone();
        textStyle.NumberFormat = "@";
        var textStyleId = wb.RegisterStyle(textStyle);
        sheet.SetCell(address, new Cell { Value = BlankValue.Instance, StyleId = textStyleId });

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[oversized]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var value = sheet.GetValue(address);
        value.Should().BeOfType<TextValue>();
        var text = ((TextValue)value).Value;
        text.Length.Should().Be(ExcelCellTextLimit);
        text.Should().Be(oversized[..ExcelCellTextLimit]);
    }

    [Fact]
    public void ExternalTextPaste_TruncatesOversizedApostropheEscapedTextToExcelCellLimit()
    {
        // The leading-apostrophe text-escape branch inside ParseClipboardValue returns its own
        // TextValue independently of the plain-fallback branch below it -- pin it separately so a
        // fix that only capped the fallback branch would still be caught.
        var oversized = "'" + new string('c', ExcelCellTextLimit + 500);
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[oversized]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var value = sheet.GetValue(address);
        value.Should().BeOfType<TextValue>();
        var text = ((TextValue)value).Value;
        text.Length.Should().Be(ExcelCellTextLimit);
    }

    [Fact]
    public void ExternalTextPaste_LeavesTextAtExactlyExcelCellLimitUntruncated()
    {
        // No-regression sibling: text landing exactly on the boundary (not over it) must survive
        // whole, and text well under the limit must be completely unaffected by the new cap.
        var exactlyAtLimit = new string('x', ExcelCellTextLimit);
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[exactlyAtLimit]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var value = sheet.GetValue(address);
        value.Should().BeOfType<TextValue>();
        var text = ((TextValue)value).Value;
        text.Length.Should().Be(ExcelCellTextLimit);
        text.Should().Be(exactlyAtLimit);
    }

    [Fact]
    public void ExternalTextPaste_OrdinaryShortTextPastesUnchanged()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [["hello world"]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue("hello world"));
    }
}
