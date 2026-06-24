using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for the multi-range sqref data-loss bug fix.
/// A CF rule whose sqref covers two non-contiguous ranges (e.g. "A1:A5 C1:C5")
/// must preserve ALL ranges through load → save → reload.
/// </summary>
public sealed class ConditionalFormatMultiRangeSqrefTests
{
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ─── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal XLSX package that has a colorScale CF rule with a two-range sqref
    /// "A1:A5 C1:C5" injected directly into the worksheet XML.
    /// </summary>
    private static MemoryStream BuildPackageWithMultiRangeSqref()
    {
        // Start from a FreeX-generated single-cell workbook so all the required
        // part files (workbook.xml, styles.xml, relationships, …) already exist.
        var wb = new Workbook("MultiRangeBook");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 2));
        }

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        // Inject a colorScale conditionalFormatting element whose sqref spans two ranges.
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(WorksheetPath)!;
            XDocument doc;
            using (var xmlStream = entry.Open())
                doc = XDocument.Load(xmlStream);

            doc.Root!.Add(new XElement(
                Ns + "conditionalFormatting",
                new XAttribute("sqref", "A1:A5 C1:C5"),
                new XElement(
                    Ns + "cfRule",
                    new XAttribute("type", "colorScale"),
                    new XAttribute("priority", "1"),
                    new XElement(
                        Ns + "colorScale",
                        new XElement(Ns + "cfvo", new XAttribute("type", "min")),
                        new XElement(Ns + "cfvo", new XAttribute("type", "max")),
                        new XElement(Ns + "color", new XAttribute("rgb", "FF63BE7B")),
                        new XElement(Ns + "color", new XAttribute("rgb", "FFF8696B"))))));

            entry.Delete();
            var replacement = archive.CreateEntry(WorksheetPath);
            using var writer = new System.IO.StreamWriter(replacement.Open());
            doc.Save(writer);
        }

        stream.Position = 0;
        return stream;
    }

    // ─── round-trip: XLSX ────────────────────────────────────────────────────

    [Fact]
    public void XlsxLoad_MultiRangeSqref_PreservesAdditionalRangesInModel()
    {
        // Arrange
        var stream = BuildPackageWithMultiRangeSqref();

        // Act
        var loaded = new XlsxFileAdapter().Load(stream);
        var cf = loaded.GetSheetAt(0).ConditionalFormats.Single(r => r.RuleType == CfRuleType.ColorScale);

        // Assert – primary range
        cf.AppliesTo.Start.Row.Should().Be(1);
        cf.AppliesTo.Start.Col.Should().Be(1); // column A
        cf.AppliesTo.End.Row.Should().Be(5);
        cf.AppliesTo.End.Col.Should().Be(1);

        // Assert – additional range preserved
        cf.AdditionalRanges.Should().NotBeNull("sqref had two ranges");
        cf.AdditionalRanges!.Should().HaveCount(1);
        var second = cf.AdditionalRanges![0];
        second.Start.Col.Should().Be(3); // column C
        second.End.Col.Should().Be(3);
        second.Start.Row.Should().Be(1);
        second.End.Row.Should().Be(5);
    }

    [Fact]
    public void XlsxSave_MultiRangeSqref_EmitsBothRangesInSqrefAttribute()
    {
        // Arrange – load a workbook with a multi-range CF rule
        var stream = BuildPackageWithMultiRangeSqref();
        var loaded = new XlsxFileAdapter().Load(stream);

        // Act – save back to XLSX
        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, saved);
        saved.Position = 0;

        // Inspect the saved worksheet XML for the sqref attribute
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = entry.Open())
            doc = XDocument.Load(xmlStream);

        var sqrefs = doc.Root!
            .Elements(Ns + "conditionalFormatting")
            .Select(e => e.Attribute("sqref")?.Value)
            .Where(v => v is not null)
            .ToList();

        // Assert – the saved sqref must contain both ranges
        sqrefs.Should().Contain(s => s!.Contains("A1:A5") && s.Contains("C1:C5"),
            "the saved sqref must preserve all non-contiguous ranges");
    }

    [Fact]
    public void XlsxRoundTrip_MultiRangeSqref_BothRangesPreservedAfterLoadAndSaveAndReload()
    {
        // Arrange
        var stream = BuildPackageWithMultiRangeSqref();

        // Act – load → save → reload
        var firstLoad = new XlsxFileAdapter().Load(stream);
        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(firstLoad, saved);
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        // Assert – both ranges survive a second load
        var cf = reloaded.GetSheetAt(0).ConditionalFormats.Single(r => r.RuleType == CfRuleType.ColorScale);
        cf.AdditionalRanges.Should().NotBeNull("multi-range sqref must survive a full round-trip");
        cf.AdditionalRanges!.Should().HaveCount(1);
        cf.AllRanges.Should().HaveCount(2);

        var all = cf.AllRanges.ToList();
        all.Should().Contain(r => r.Start.Col == 1 && r.End.Col == 1, "range A1:A5 must survive");
        all.Should().Contain(r => r.Start.Col == 3 && r.End.Col == 3, "range C1:C5 must survive");
    }

    // ─── round-trip: NativeJson ──────────────────────────────────────────────

    [Fact]
    public void NativeJsonRoundTrip_MultiRangeSqref_BothRangesPreserved()
    {
        // Arrange – build a workbook in-memory with multi-range CF
        var wb = new Workbook("NativeJsonMultiRange");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 1)),
            AdditionalRanges = [new GridRange(
                new CellAddress(sheetId, 1, 3),
                new CellAddress(sheetId, 5, 3))],
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "3",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(wb, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);

        // Assert
        var roundTripped = loaded.GetSheetAt(0).ConditionalFormats.Single();
        roundTripped.AdditionalRanges.Should().NotBeNull();
        roundTripped.AdditionalRanges!.Should().HaveCount(1);
        roundTripped.AdditionalRanges![0].Start.Col.Should().Be(3);
        roundTripped.AdditionalRanges![0].End.Col.Should().Be(3);
    }
}
