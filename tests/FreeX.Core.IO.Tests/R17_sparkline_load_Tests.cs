using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R17-chart-3d-combo-secondary-1: the sheet-layout-application copy step that rebuilds
/// <see cref="Sheet.Sparklines"/> from the parsed &lt;x14:sparklineGroup&gt; layout must
/// preserve <see cref="SparklineModel.DateAxisRange"/>, otherwise the date-axis setting
/// silently reverts to "general" the moment the workbook is resaved.
/// </summary>
public sealed class R17_sparkline_load_Tests
{
    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    private static IEnumerable<XElement> SparklineGroups(XDocument wsXml) =>
        wsXml.Descendants().Where(e =>
            string.Equals(e.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Load_PreservesDateAxisRange_OnSparklineGroup()
    {
        var workbook = new Workbook("SparklineDateAxis");
        var sheet    = workbook.AddSheet("Data");

        for (uint col = 1; col <= 5; col++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));
            sheet.SetCell(new CellAddress(sheet.Id, 2, col), new NumberValue(45658 + col)); // date-serial values
        }

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange     = Range(sheet, 1, 1, 1, 5),
            Location      = new CellAddress(sheet.Id, 1, 6),
            Kind          = SparklineKind.Line,
            DateAxisRange = Range(sheet, 2, 1, 2, 5),
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        // The initial save must actually emit the dateAxis="1" attribute plus a bare <xm:f> child
        // (the real CT_SparklineGroup shape) so the round trip is meaningful.
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            using var entryStream = entry.Open();
            var wsXml = XDocument.Load(entryStream);
            var grp = SparklineGroups(wsXml).Single();
            grp.Attribute("dateAxis")!.Value.Should().Be("1");
            grp.Elements().Should().Contain(e =>
                string.Equals(e.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase));
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(1);
        var reloadedSparkline = reloadedSheet.Sparklines[0];

        // This is the crux of the fix: DateAxisRange must survive the load-time
        // layout-application copy, or a resave will drop <x14:dateAxis> entirely.
        reloadedSparkline.DateAxisRange.Should().NotBeNull();
        reloadedSparkline.DateAxisRange!.Value.Start.Row.Should().Be(2u);
        reloadedSparkline.DateAxisRange!.Value.Start.Col.Should().Be(1u);
        reloadedSparkline.DateAxisRange!.Value.End.Row.Should().Be(2u);
        reloadedSparkline.DateAxisRange!.Value.End.Col.Should().Be(5u);
        reloadedSparkline.DateAxisRange!.Value.Start.Sheet.Should().Be(reloadedSheet.Id,
            "the copied range must be remapped onto the reloaded sheet's id, not the parser's temporary sheet id");

        // Resaving the reloaded workbook must re-emit the dateAxis="1" attribute and <xm:f> too.
        using var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(reloaded, resaved);
        resaved.Position = 0;
        using (var archive = new ZipArchive(resaved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            using var entryStream = entry.Open();
            var wsXml = XDocument.Load(entryStream);
            var grp = SparklineGroups(wsXml).Single();
            grp.Attribute("dateAxis")!.Value.Should().Be("1",
                "resaving a reloaded workbook must not silently drop the date-axis setting");
            grp.Elements().Should().Contain(e =>
                string.Equals(e.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase),
                "resaving a reloaded workbook must not silently drop the date-axis range formula");
        }
    }
}
