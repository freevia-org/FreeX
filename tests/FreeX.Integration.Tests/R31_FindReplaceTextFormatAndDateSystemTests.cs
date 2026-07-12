using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R31-commands-find-replace-deep-1: Replace must not re-infer Number/Date for a destination
/// cell whose NumberFormat is Text ("@") -- Excel keeps the replacement as literal text there
/// (e.g. a zip code "01234" kept as text to preserve the leading zero), mirroring
/// PasteCommandFactory's IsDestinationTextFormatted check.
///
/// R31-commands-find-replace-deep-2: a Replace-produced date value must be computed in the
/// owning workbook's actual date system (1900 vs 1904), and .NET's default two-digit-year
/// cutoff (2049) must be overridden to Excel's fixed cutoff (2029) so "45" parses as 1945.
/// </summary>
public class R31_FindReplaceTextFormatAndDateSystemTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    [Fact]
    public void ReplaceAll_IntoTextFormattedCell_KeepsReplacementAsTextWithLeadingZero()
    {
        var (wb, sheet, commandBus) = Setup();
        var address = new CellAddress(sheet.Id, 1, 1);
        var textStyleId = wb.RegisterStyle(new CellStyle { NumberFormat = "@" });
        sheet.SetCell(address, new Cell { Value = new TextValue("01234"), StyleId = textStyleId });

        var count = FindReplaceService.ReplaceAll(
            wb, commandBus, "01234", "05678", matchCase: false, matchEntireCell: true);

        count.Should().Be(1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("05678"));
    }

    // Sibling already-working case: the very same replacement text into a General-formatted
    // (non-Text) cell must still be re-parsed into a NumberValue, exactly as before this fix.
    [Fact]
    public void ReplaceAll_IntoGeneralFormattedCell_StillCoercesReplacementToNumber()
    {
        var (wb, sheet, commandBus) = Setup();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("01234"));

        var count = FindReplaceService.ReplaceAll(
            wb, commandBus, "01234", "05678", matchCase: false, matchEntireCell: true);

        count.Should().Be(1);
        sheet.GetCell(address)!.Value.Should().Be(new NumberValue(5678));
    }

    [Fact]
    public void ReplaceAll_OnWorkbookUsing1904DateSystem_StoresSerialInThat1904Epoch()
    {
        var (wb, sheet, commandBus) = Setup();
        wb.Uses1904DateSystem = true;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("OldLabel"));

        var count = FindReplaceService.ReplaceAll(
            wb, commandBus, "OldLabel", "1/15/2026", matchCase: false, matchEntireCell: true);

        count.Should().Be(1);
        var expectedSerial = (new DateTime(2026, 1, 15) - new DateTime(1904, 1, 1)).TotalDays;
        var value = sheet.GetCell(address)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().Be(expectedSerial);
    }

    // Sibling already-working case: the default (1900) date system must still produce the
    // 1900-epoch serial for the same replacement text -- the fix must not change 1900 behavior.
    [Fact]
    public void ReplaceAll_OnDefault1900Workbook_StoresSerialInThat1900Epoch()
    {
        var (wb, sheet, commandBus) = Setup();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("OldLabel"));

        var count = FindReplaceService.ReplaceAll(
            wb, commandBus, "OldLabel", "1/15/2026", matchCase: false, matchEntireCell: true);

        count.Should().Be(1);
        var expectedSerial = (new DateTime(2026, 1, 15) - new DateTime(1899, 12, 30)).TotalDays;
        var value = sheet.GetCell(address)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().Be(expectedSerial);
    }

    [Fact]
    public void ReplaceAll_WithTwoDigitYear_UsesExcelCutoffNotDotNetDefault()
    {
        var (wb, sheet, commandBus) = Setup();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("OldLabel"));

        var count = FindReplaceService.ReplaceAll(
            wb, commandBus, "OldLabel", "6/15/45", matchCase: false, matchEntireCell: true);

        count.Should().Be(1);
        // Excel's fixed two-digit-year window maps 30-99 -> 1930-1999, so "45" must be 1945,
        // not .NET's default cutoff (which trails ~50 years ahead of the current date, e.g. 2045).
        var expectedSerial = (new DateTime(1945, 6, 15) - new DateTime(1899, 12, 30)).TotalDays;
        var value = sheet.GetCell(address)!.Value.Should().BeOfType<NumberValue>().Subject;
        value.Value.Should().Be(expectedSerial);
    }
}
