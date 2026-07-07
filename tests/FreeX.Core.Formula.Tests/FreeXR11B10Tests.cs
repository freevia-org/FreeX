using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-11 fix-bucket R10 regression coverage.
/// </summary>
public sealed class FreeXR11B10Tests
{
    private readonly FormulaEvaluator _eval = new();

    // R11-number-format-1: FormatElapsedTime only substituted the DOUBLED mm/ss sub-tokens.
    // A single-letter m or s sub-token (e.g. "[h]:m", "[m]:s", "[h]:m:s") fell through to the
    // literal-character branch and emitted the raw format letter instead of the remainder value.
    [Fact]
    public void ElapsedTimeFormat_SingleLetterMinuteToken_SubstitutesRemainderMinutes()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("S");
        // 1.5 days == 36 hours exactly => [h] lead = 36, remainder minutes = 0.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(1.5));

        _eval.Evaluate("=TEXT(A1,\"[h]:m\")", sheet, workbook)
            .Should().Be(new TextValue("36:0"));
    }

    [Fact]
    public void ElapsedTimeFormat_SingleLetterSecondToken_AfterMinuteLead_SubstitutesRemainderSeconds()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("S");
        // 90 seconds expressed as a day fraction => [m] lead = 1 minute, remainder seconds = 30.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(90.0 / 86400.0));

        _eval.Evaluate("=TEXT(A1,\"[m]:s\")", sheet, workbook)
            .Should().Be(new TextValue("1:30"));
    }

    [Fact]
    public void ElapsedTimeFormat_SingleLetterMinuteAndSecondTokens_AfterHourLead_SubstituteBothRemainders()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("S");
        // 1.5 days == 36 hours exactly => [h] lead = 36, remainder minutes = 0, remainder seconds = 0.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(1.5));

        _eval.Evaluate("=TEXT(A1,\"[h]:m:s\")", sheet, workbook)
            .Should().Be(new TextValue("36:0:0"));
    }
}
