using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R112 decision: an invariant-culture thread (CultureInfo.CurrentCulture.Name == "", e.g. a
/// process built/run with &lt;InvariantGlobalization&gt;true&lt;/InvariantGlobalization&gt;) skips
/// DelimitedTextWorkbookReader's current-culture date path entirely (TryParseCurrentCultureDateTime
/// bails out early whenever CurrentCulture.Name is empty) and falls through to the invariant exact-
/// format DateTimeFormats list instead. R111 added the year-less "M/d"/"M-d" shape to the
/// current-culture path but left that invariant fallback list without a matching year-less entry,
/// so the exact same "3/4" literal that imports as a date under any real culture stayed plain text
/// under invariant globalization. This is now deliberately fixed for parity: "M/d" and "M-d" were
/// added to DelimitedTextWorkbookReader.DateTimeFormats.cs so invariant-culture CSV import matches
/// every other culture (and real Excel, whose own auto-recognition does not vary by OS
/// globalization mode).
/// </summary>
public sealed class R111_InvariantCultureYearlessMonthDayDateCoercionTests
{
    [Fact]
    public void Load_UnderInvariantCulture_CoercesYearlessSlashMonthDayToCurrentYearDate()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("3/4\r\n"));

            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            var expected = new DateTime(DateTime.Now.Year, 3, 4);
            sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
                .Should().Be(DateTimeValue.FromDateTime(expected));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Load_UnderInvariantCulture_CoercesYearlessHyphenMonthDayToCurrentYearDate()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1-2\r\n"));

            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            var expected = new DateTime(DateTime.Now.Year, 1, 2);
            sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
                .Should().Be(DateTimeValue.FromDateTime(expected));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // No-regression: a full date with an explicit year must still parse correctly under invariant
    // culture (already worked before this change, via the pre-existing "M/d/yyyy" entry).
    [Fact]
    public void Load_UnderInvariantCulture_StillCoercesFullDateWithYear()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("3/4/2024\r\n"));

            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
                .Should().Be(DateTimeValue.FromDateTime(new DateTime(2024, 3, 4)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // No-regression: a plain two-digit-group decimal must still be a number, not a date, under
    // invariant culture too.
    [Fact]
    public void Load_UnderInvariantCulture_StillCoercesDottedDecimalAsNumber()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("3.14\r\n"));

            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(3.14));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // No-regression: an ordinary non-date string must still stay text under invariant culture.
    [Fact]
    public void Load_UnderInvariantCulture_StillCoercesNonDateTextAsText()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var adapter = new DelimitedTextFileAdapter(".csv", "Comma-separated values", ',');
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Product\r\n"));

            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Product"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
