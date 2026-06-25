using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for G8: XlsxConditionalFormatClosedXmlMapper.Save must apply CellValue/Formula
/// basic CF rules to ALL ranges (AppliesTo + AdditionalRanges), not only the first range.
/// The old code iterated only cf.AppliesTo; AdditionalRanges were lost on save.
/// </summary>
public sealed class XlsxBasicCfMultiRangeTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    [Fact]
    public void Save_BasicCellValueCfWithAdditionalRanges_EmitsBothRangesInWorksheet()
    {
        // Arrange: build a workbook with a CellValue CF that covers two discontiguous ranges.
        var wb = new Workbook("G8Test");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;

        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheetId, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheetId, row, 3), new NumberValue(row * 2));
        }

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 1)),
            AdditionalRanges =
            [
                new GridRange(
                    new CellAddress(sheetId, 1, 3),
                    new CellAddress(sheetId, 5, 3))
            ],
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "2",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(cf);

        // Act: save to XLSX.
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        // Assert: the worksheet must contain conditional-formatting elements for BOTH ranges.
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = entry.Open())
            doc = XDocument.Load(xmlStream);

        var sqrefs = doc.Root!
            .Elements(Ns + "conditionalFormatting")
            .Select(e => e.Attribute("sqref")?.Value)
            .Where(v => v is not null)
            .ToList();

        // ClosedXML may coalesce identical rules into one <conditionalFormatting> with a multi-range
        // sqref ("A1:A5 C1:C5") or emit two elements; either is valid as long as BOTH ranges appear.
        var sqrefTokens = sqrefs
            .SelectMany(s => s!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        sqrefTokens.Should().Contain("A1:A5", "the primary range must be emitted");
        sqrefTokens.Should().Contain("C1:C5", "the additional range C1:C5 must be emitted (previously lost)");
    }
}
