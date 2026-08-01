using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R111: real Excel's General-format auto-recognition converts a bare, year-less "M/d" or "M-d"
/// CSV token (e.g. "3/4", "1-2") to a date using the current year -- the same underlying mechanism
/// behind the well-known "gene names turn into dates" class of bugs, just triggered here by a plain
/// numeric month/day token instead of a month-name abbreviation. Before this fix,
/// LooksLikeCurrentCultureDateCandidate required either a letter or 3+ digit groups, so a 2-group
/// slash/dash token with no year fell all the way through to a literal TextValue instead of a date.
/// </summary>
public sealed class R111_YearlessMonthDayDateCoercionTests
{
    [Fact]
    public void Load_CoercesYearlessSlashMonthDayToCurrentYearDate()
    {
        var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("3/4\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        var expected = new DateTime(DateTime.Now.Year, 3, 4);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(expected));
    }

    [Fact]
    public void Load_CoercesYearlessHyphenMonthDayToCurrentYearDate()
    {
        var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1-2\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        var expected = new DateTime(DateTime.Now.Year, 1, 2);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(expected));
    }

    // Sibling/no-regression coverage: a plain two-digit-group decimal number sharing the "."
    // character (which doubles as a date separator in the 3+-group heuristic) must still be
    // coerced as a NUMBER, not misparsed as a date, on the common cultures where "." is the
    // decimal separator. Guards the deliberate exclusion of "." from the new 2-group date path.
    [Fact]
    public void Load_StillCoercesTwoGroupDottedDecimalsAsNumbers()
    {
        var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("3.14,1.5\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(3.14));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(1.5));
    }

    // Sibling coverage: a two-group colon-only time (no date separator) must still be recognized
    // as a standalone time, not swept up by the new slash/dash date-candidate path.
    [Fact]
    public void Load_StillCoercesStandaloneTimeWithoutDateSeparator()
    {
        var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("9:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
    }

    // Choke-point coverage: DelimitedTextWorkbookWriter has its own, independent date-shape
    // heuristic (HasSupportedDateTimeShape) that decides whether a TextValue needs a leading
    // apostrophe marker so it survives a save/reload round trip as text instead of being
    // reinterpreted by the (now more permissive) reader. Without updating that heuristic to match,
    // an explicit TextValue("1/2") -- e.g. a user who typed "1/2" and had Excel/FreeX treat it as
    // literal text -- would silently turn into a date the next time the file is opened.
    [Fact]
    public void Save_RoundTripsYearlessSlashMonthDayTextValueAsLiteralText()
    {
        var (workbook, sheet) = TextFileAdapterTestHelper.CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("1/2"));

        var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
        var roundTripped = TextFileAdapterTestHelper.SaveAndLoad(adapter, workbook);

        roundTripped.Sheets.Single().GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(new TextValue("1/2"));
    }
}
