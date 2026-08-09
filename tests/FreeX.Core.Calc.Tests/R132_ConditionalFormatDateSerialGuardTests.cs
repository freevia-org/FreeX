using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R132-commands-autofilter-date-serial-guard-1 [HIGH sibling]: ViewportConditionalFormatEvaluator's
/// GetString (used by the ContainsText/BeginsWith/EndsWith/Duplicate-Values rule matchers) called
/// DateTimeValue.ToDateTime() unguarded, so a "Text that Contains"/"Duplicate Values" conditional
/// format applied to a range containing an out-of-range date serial (e.g. from date-subtraction
/// arithmetic gone negative, or a value loaded from a file) crashed evaluating the WHOLE viewport,
/// not just that one cell's format. Fixed via DateTimeValue.TryToDateTime, falling back to the raw
/// serial text -- matching FilterValueFormatter.ToText's established fallback for the same case.
/// </summary>
public sealed class R132_ConditionalFormatDateSerialGuardTests
{
    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void ContainsTextRule_OutOfRangeDateSerial_DoesNotCrash_AndNormalDateStillMatches()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // A1: an ordinary literal date -- sibling no-regression check that Contains "2026" still
        // matches its "yyyy-MM-dd" formatted text.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));

        // B1: a DateTimeValue whose serial is far outside DateTime's representable range. Calling
        // GetString on this used to throw ArgumentOutOfRangeException from inside ToDateTime(),
        // aborting the whole GetViewport call (every cell's conditional format, not just B1's).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(-99999999));

        var blue = new CellStyle { FillColor = new CellColor(189, 215, 238) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "2026",
            FormatIfTrue = blue
        });

        var act = () => new ViewportService().GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var vp = act.Should().NotThrow("an out-of-range date serial must not crash evaluating conditional formats for the whole viewport").Which;

        GetCell(vp, 1, 1).Style?.FillColor.Should().Be(new CellColor(189, 215, 238),
            "the ordinary in-range date cell must still match Contains '2026' (sibling no-regression)");
        GetCell(vp, 1, 2).Style?.FillColor.Should().NotBe(new CellColor(189, 215, 238),
            "the unconvertible serial's raw fallback text ('-99999999') doesn't contain '2026' and must not match");
    }
}
