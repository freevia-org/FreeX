using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression coverage for G12: TEXT() and general/date-display formatting must interpret
/// date serials under the workbook's 1904 date system (serial 0 == 1904-01-01), matching the
/// already-1904-aware DATE/YEAR/MONTH/etc. functions in BuiltInFunctions.DateTime.cs, instead of
/// silently formatting every date-formatted cell 1462 days (4 years) off via the 1900 epoch.
/// </summary>
public sealed class NumberFormatter1904DateSystemTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Text_WithDateFormat_Uses1904Epoch_WhenWorkbookUses1904DateSystem()
    {
        var workbook = new Workbook { Uses1904DateSystem = true };
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(0));

        // Serial 0 under the 1904 date system is 1904-01-01 (see
        // DateFunctions_UseWorkbook1904DateSystem in ExcelParityDateSerialTests.cs, which asserts
        // YEAR(0)==1904 for the same workbook setting). TEXT() must agree with YEAR()/MONTH()/DAY().
        _eval.Evaluate("=TEXT(A1,\"yyyy-mm-dd\")", sheet, workbook)
            .Should().Be(new TextValue("1904-01-01"));
    }

    [Fact]
    public void Text_WithDateFormat_Uses1900Epoch_WhenWorkbookDoesNotUse1904DateSystem()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(1));

        // Baseline: serial 1 under the default 1900 date system is 1900-01-01.
        _eval.Evaluate("=TEXT(A1,\"yyyy-mm-dd\")", sheet, workbook)
            .Should().Be(new TextValue("1900-01-01"));
    }

    [Fact]
    public void Format_WithUses1904Flag_RendersDateSerialAgainst1904Epoch()
    {
        // Direct NumberFormatter unit coverage (the engine TEXT() and grid display both funnel
        // through), independent of the formula layer.
        NumberFormatter.Format(new DateTimeValue(0), "yyyy-mm-dd", uses1904DateSystem: true)
            .Should().Be("1904-01-01");

        NumberFormatter.Format(new DateTimeValue(0), "yyyy-mm-dd", uses1904DateSystem: false)
            .Should().Be("1899-12-31");
    }

    [Fact]
    public void Format_GeneralFormat_WithUses1904Flag_RendersDateSerialAgainst1904Epoch()
    {
        // The no-explicit-format ("General") display path must also honor the 1904 system,
        // matching plain (unformatted) cell display in the grid.
        NumberFormatter.Format(new DateTimeValue(0), "General", uses1904DateSystem: true)
            .Should().Be("01/01/1904");
    }

    [Fact]
    public void FormatWithColor_WithUses1904Flag_RendersDateSerialAgainst1904Epoch()
    {
        var result = NumberFormatter.FormatWithColor(
            new DateTimeValue(0),
            "yyyy-mm-dd",
            uses1904DateSystem: true);

        result.Text.Should().Be("1904-01-01");
    }
}
