using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for finding R36-io-sparkline-2-1: editing one member of a loaded
/// multi-member sparkline group must not silently discard the edit, nor force it onto the
/// untouched siblings, on save. XlsxSparklineMapper.Save used to pick an arbitrary
/// <c>group.First()</c> as the "representative" whose group-level attributes (type, markers,
/// colors, axis scaling, etc.) were written for the WHOLE &lt;x14:sparklineGroup&gt;, so whichever
/// member happened to be first in list order silently won regardless of which member the user
/// actually edited.
/// </summary>
public sealed class XlsxSparklineGroupEditTests
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

    private static (Workbook workbook, XDocument worksheetXml) SaveAndReadXml(Workbook workbook)
    {
        using var saved = SaveXlsx(workbook);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument wsXml;
        using (var s = entry.Open())
            wsXml = XDocument.Load(s);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        return (reloaded, wsXml);
    }

    private static IEnumerable<XElement> SparklineGroups(XDocument wsXml) =>
        wsXml.Descendants().Where(e =>
            string.Equals(e.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds the row/col=6 3-member Line group used by the tests below, all sharing GroupId=5.</summary>
    private static (Sheet sheet, SparklineModel first, SparklineModel second, SparklineModel third) BuildThreeMemberGroup(Workbook workbook)
    {
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= 3; row++)
            for (uint col = 1; col <= 5; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * 10 + col));

        var first = new SparklineModel
        {
            DataRange = Range(sheet, 1, 1, 1, 5),
            Location  = new CellAddress(sheet.Id, 1, 6),
            Kind      = SparklineKind.Line,
            GroupId   = 5,
        };
        var second = new SparklineModel
        {
            DataRange = Range(sheet, 2, 1, 2, 5),
            Location  = new CellAddress(sheet.Id, 2, 6),
            Kind      = SparklineKind.Line,
            GroupId   = 5,
        };
        var third = new SparklineModel
        {
            DataRange = Range(sheet, 3, 1, 3, 5),
            Location  = new CellAddress(sheet.Id, 3, 6),
            Kind      = SparklineKind.Line,
            GroupId   = 5,
        };
        sheet.Sparklines.Add(first);
        sheet.Sparklines.Add(second);
        sheet.Sparklines.Add(third);
        return (sheet, first, second, third);
    }

    // ── The finding's exact scenario ────────────────────────────────────────────

    [Fact]
    public void EditingOneMemberOfThreeMemberGroup_PreservesEditAndLeavesSiblingsUnchanged()
    {
        var workbook = new Workbook("SparklineGroupEdit");
        var (_, first, second, third) = BuildThreeMemberGroup(workbook);

        // Simulate ConfigureSparklineCommand.Apply editing ONLY the second member: turn on
        // markers and switch its type to Column, leaving #1 and #3 untouched (still Line, no
        // markers) — exactly as the finding describes.
        second.Kind = SparklineKind.Column;
        second.ShowMarkers = true;

        var (reloaded, wsXml) = SaveAndReadXml(workbook);

        // The edited member must NOT be silently dropped: some group in the saved XML must be
        // type=column with markers=1.
        var groups = SparklineGroups(wsXml).ToList();
        var hasColumnWithMarkers = groups.Any(g =>
            g.Attribute("type")!.Value == "column" &&
            (g.Attribute("markers")?.Value == "1" || g.Attribute("markers")?.Value == "true"));
        hasColumnWithMarkers.Should().BeTrue(
            "the user's edit to the second sparkline (Column + markers) must survive the save");

        // The untouched siblings must NOT have been force-converted to Column/markers either:
        // at least one saved group must still be type=line with no markers attribute.
        var hasUntouchedLineGroup = groups.Any(g =>
            g.Attribute("type")!.Value == "line" &&
            g.Attribute("markers") is null);
        hasUntouchedLineGroup.Should().BeTrue(
            "sparklines #1 and #3 were never edited and must keep their original Line/no-markers settings");

        // Reload and verify all three sparklines survive with the correct per-member settings.
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(3);

        var reloadedFirst  = reloadedSheet.Sparklines.Single(s => s.Location.Row == 1);
        var reloadedSecond = reloadedSheet.Sparklines.Single(s => s.Location.Row == 2);
        var reloadedThird  = reloadedSheet.Sparklines.Single(s => s.Location.Row == 3);

        reloadedSecond.Kind.Should().Be(SparklineKind.Column, "the edited member's new type must round-trip");
        reloadedSecond.ShowMarkers.Should().BeTrue("the edited member's new markers setting must round-trip");

        reloadedFirst.Kind.Should().Be(SparklineKind.Line, "sibling #1 must be unaffected by the edit to #2");
        reloadedFirst.ShowMarkers.Should().BeFalse("sibling #1 must be unaffected by the edit to #2");
        reloadedThird.Kind.Should().Be(SparklineKind.Line, "sibling #3 must be unaffected by the edit to #2");
        reloadedThird.ShowMarkers.Should().BeFalse("sibling #3 must be unaffected by the edit to #2");
    }

    [Fact]
    public void RemovingOneMemberOfThreeMemberGroup_LeavesRemainingTwoUnchanged()
    {
        var workbook = new Workbook("SparklineGroupRemove");
        var (sheet, first, _, third) = BuildThreeMemberGroup(workbook);

        // Simulate deleting the second sparkline (e.g. via Clear Sparklines on that one cell):
        // it's simply removed from the sheet's collection, leaving #1 and #3 still agreeing.
        sheet.Sparklines.RemoveAt(1);

        var (reloaded, wsXml) = SaveAndReadXml(workbook);

        // The two untouched, still-agreeing members must still be written as ONE shared group.
        var groups = SparklineGroups(wsXml).ToList();
        groups.Should().HaveCount(1,
            because: "the remaining two members never diverged from each other and must stay merged into one group");
        groups[0].Attribute("type")!.Value.Should().Be("line");

        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(2);
        reloadedSheet.Sparklines.Should().AllSatisfy(s =>
        {
            s.Kind.Should().Be(SparklineKind.Line);
            s.ShowMarkers.Should().BeFalse();
        });

        var rowsPresent = reloadedSheet.Sparklines.Select(s => s.Location.Row).OrderBy(r => r).ToList();
        rowsPresent.Should().Equal(new uint[] { 1, 3 });
        first.Kind.Should().Be(SparklineKind.Line);
        third.Kind.Should().Be(SparklineKind.Line);
    }

    // ── No-regression: a uniform group-level edit applied to every member still merges ──

    [Fact]
    public void GroupLevelStyleEditAppliedToAllMembers_StillRoundTripsAsOneSharedGroup()
    {
        var workbook = new Workbook("SparklineGroupUniformEdit");
        var (_, first, second, third) = BuildThreeMemberGroup(workbook);

        // A genuine "group-level" style edit (e.g. via the ribbon's Sparkline Style gallery)
        // is applied uniformly to every member of the group — they must still agree afterwards
        // and therefore still round-trip as ONE <x14:sparklineGroup>, not be split apart.
        foreach (var member in new[] { first, second, third })
        {
            member.SeriesColor = new CellColor(0x11, 0x22, 0x33);
            member.ShowMarkers = true;
        }

        var (reloaded, wsXml) = SaveAndReadXml(workbook);

        var groups = SparklineGroups(wsXml).ToList();
        groups.Should().HaveCount(1,
            because: "all three members still agree on every group-level attribute after the uniform edit");
        groups[0].Attribute("markers")!.Value.Should().BeOneOf("1", "true");

        var sparklineRefs = groups[0].Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "sparkline", StringComparison.OrdinalIgnoreCase))
            .ToList();
        sparklineRefs.Should().HaveCount(3, "all three members belong in the single shared group");

        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(3);
        reloadedSheet.Sparklines.Should().AllSatisfy(s =>
        {
            s.ShowMarkers.Should().BeTrue();
            s.SeriesColor.Should().Be(new CellColor(0x11, 0x22, 0x33));
        });
    }
}
