using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for group H-sparklines finding H46: independently created sparklines that
/// share the same Kind but were never assigned a shared GroupId (GroupId == 0, the default for
/// in-app-created sparklines that were never round-tripped through an XLSX read) must save as
/// SEPARATE &lt;x14:sparklineGroup&gt; elements, not be silently merged into one group keyed by
/// Kind — merging would discard one sparkline's distinct markers/colors/axis settings on save.
/// </summary>
public sealed class HSparklinesGroupIdentityTests
{
    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    private static MemoryStream SaveXlsx(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void IndependentlyCreatedSameKindSparklines_WithDefaultGroupId_SaveAsSeparateGroups()
    {
        var workbook = new Workbook("SparklineIndependentGroups");
        var sheet = workbook.AddSheet("Data");

        for (uint col = 1; col <= 5; col++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));
            sheet.SetCell(new CellAddress(sheet.Id, 2, col), new NumberValue(col * 2));
        }

        // Two Line sparklines created independently (e.g. via separate "Insert Sparkline"
        // commands) — neither is ever assigned a nonzero GroupId, mirroring AddSparklineCommand's
        // real construction path (GroupId defaults to 0 for both).
        var first = new SparklineModel
        {
            DataRange = Range(sheet, 1, 1, 1, 5),
            Location = new CellAddress(sheet.Id, 1, 6),
            Kind = SparklineKind.Line,
            ShowMarkers = true,
            SeriesColor = new CellColor(0xFF, 0x00, 0x00),
        };
        var second = new SparklineModel
        {
            DataRange = Range(sheet, 2, 1, 2, 5),
            Location = new CellAddress(sheet.Id, 2, 6),
            Kind = SparklineKind.Line,
            ShowHighPoint = true,
            SeriesColor = new CellColor(0x00, 0x00, 0xFF),
        };
        sheet.Sparklines.Add(first);
        sheet.Sparklines.Add(second);

        first.GroupId.Should().Be(0);
        second.GroupId.Should().Be(0);

        using var saved = SaveXlsx(workbook);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument wsXml;
        using (var s = entry.Open())
            wsXml = XDocument.Load(s);

        var groups = wsXml.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase))
            .ToList();

        groups.Should().HaveCount(2,
            because: "two independently-created same-kind sparklines with default GroupId must " +
                      "not be collapsed into one shared x14:sparklineGroup");

        // Each group must retain its own distinct display settings.
        var markerAttrs = groups.Select(g => g.Attribute("markers")?.Value).ToList();
        markerAttrs.Should().ContainSingle(v => v == "1",
            because: "only the first sparkline's ShowMarkers=true should appear, on its own group");

        var highAttrs = groups.Select(g => g.Attribute("high")?.Value).ToList();
        highAttrs.Should().ContainSingle(v => v == "1",
            because: "only the second sparkline's ShowHighPoint=true should appear, on its own group");

        // Reload and confirm both sparklines survive with their own distinct settings.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(2);

        var reloadedFirst = reloadedSheet.Sparklines.Single(s => s.SeriesColor == new CellColor(0xFF, 0x00, 0x00));
        var reloadedSecond = reloadedSheet.Sparklines.Single(s => s.SeriesColor == new CellColor(0x00, 0x00, 0xFF));

        reloadedFirst.ShowMarkers.Should().BeTrue();
        reloadedFirst.ShowHighPoint.Should().BeFalse();
        reloadedSecond.ShowHighPoint.Should().BeTrue();
        reloadedSecond.ShowMarkers.Should().BeFalse();
    }
}
