using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R68-io-cell-value-types-6-1: an out-of-range date serial -- a <c>DateTimeValue</c> whose serial exceeds
/// <c>DateTime.FromOADate</c>'s representable range (e.g. a Paste-Special-Add result like
/// <c>DateTimeValue(10045306)</c>) -- fell through <c>XlsxClosedXmlCellMapper.MapValueInverse</c>'s
/// <c>DateTimeValue</c> fallback arm to a plain <c>dt.Value.ToString("R")</c>, which made the saved cell a
/// TEXT cell (a string in the shared-strings/inlineStr sense). Excel keeps such a value a NUMBER cell -- a
/// date is just a number with a display format -- so ISNUMBER/SUM must still see it as numeric. The fix
/// emits the raw numeric serial as a NUMBER XLCellValue instead of a string whenever the serial itself is
/// finite (only a genuinely non-finite serial, NaN/Infinity, still falls back to the string form, since that
/// cannot be written as a numeric XML value at all).
/// </summary>
public sealed class R68_OutOfRangeDateSerialCellTypeTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XElement SaveAndGetCellElement(double serial, out MemoryStream saved)
    {
        var workbook = new Workbook("OutOfRangeDate");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(serial));

        saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var sheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var cellElement = sheetXml.Descendants(WorkbookNs + "c").First(c => c.Attribute("r")!.Value == "A1");
        saved.Position = 0;
        return cellElement;
    }

    [Fact]
    public void Save_OutOfRangeDateTimeValueSerial_WritesNumberCell_NotTextCell()
    {
        // DateTime.FromOADate's max is OADate ~2958465.999999999 (year 9999). 10045306 is comfortably
        // beyond that -- e.g. what a Paste-Special "Add" onto a date cell can produce -- but is still a
        // perfectly finite double.
        const double outOfRangeSerial = 10045306d;

        var cellElement = SaveAndGetCellElement(outOfRangeSerial, out var saved);

        cellElement.Attribute("t").Should().BeNull(
            "a NUMBER cell has no t attribute (t is only present for s/str/b/e/inlineStr) -- an out-of-range " +
            "date serial must round-trip as a number, not degrade to a text cell");

        var rawValue = cellElement.Element(WorkbookNs + "v")!.Value;
        double.Parse(rawValue, CultureInfo.InvariantCulture).Should().Be(outOfRangeSerial,
            "the raw numeric serial must be preserved exactly, not reformatted as a round-tripped string");

        // Reload keeps it numeric (ISNUMBER/SUM-compatible), not text.
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedValue = reloaded.GetSheetAt(0).GetCell(1, 1)!.Value;
        reloadedValue.Should().BeOfType<NumberValue>("reloading must keep the out-of-range serial numeric");
        ((NumberValue)reloadedValue).Value.Should().Be(outOfRangeSerial);
    }

    [Fact]
    public void Save_NormalInRangeDateTimeValue_StillWritesAsDate_NoRegression()
    {
        // Sibling no-regression case: an ordinary in-range date must still round-trip through the
        // TryMapDateTimeValue happy path (DateTimeValue -> DateTime -> back to DateTimeValue on reload),
        // unaffected by the new out-of-range fallback arm.
        var knownDate = new DateTime(2026, 6, 5);
        var inRangeSerial = knownDate.ToOADate();

        var cellElement = SaveAndGetCellElement(inRangeSerial, out var saved);

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedValue = reloaded.GetSheetAt(0).GetCell(1, 1)!.Value;
        reloadedValue.Should().BeOfType<DateTimeValue>("an ordinary in-range date must still round-trip as a date value");
        ((DateTimeValue)reloadedValue).Value.Should().BeApproximately(inRangeSerial, 1e-6);
    }
}
