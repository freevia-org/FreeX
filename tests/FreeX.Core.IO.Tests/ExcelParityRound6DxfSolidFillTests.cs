using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Excel-parity round 6: real Excel paints a SOLID differential (conditional-format) fill with the
/// dxf's bgColor. The advanced-rule writer (Top 10, Above Average, Duplicate/Unique, text, blank/error
/// rules) used to emit only fgColor with bgColor indexed=64, which opened in Excel as a white fill
/// (measured through Range.DisplayFormat.Interior.Color of the re-opened workbook).
/// </summary>
public sealed class ExcelParityRound6DxfSolidFillTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Theory]
    [InlineData(CfRuleType.Top10)]
    [InlineData(CfRuleType.AboveAverage)]
    [InlineData(CfRuleType.DuplicateValues)]
    [InlineData(CfRuleType.UniqueValues)]
    [InlineData(CfRuleType.ContainsText)]
    [InlineData(CfRuleType.Blanks)]
    [InlineData(CfRuleType.Errors)]
    public void AdvancedRuleDxf_WritesTheSolidFillColorToBgColorAsWellAsFgColor(CfRuleType type)
    {
        var workbook = new Workbook("DxfSolidFill");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = type,
            TextRuleText = "a",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 199, 206) },
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var styles = zip.GetEntry("xl/styles.xml")!.Open();
        var patternFill = XDocument.Load(styles).Root!
            .Element(Ns + "dxfs")!.Elements(Ns + "dxf").Single()
            .Element(Ns + "fill")!.Element(Ns + "patternFill")!;

        ((string?)patternFill.Element(Ns + "bgColor")!.Attribute("rgb")).Should().Be("FFFFC7CE");
        ((string?)patternFill.Element(Ns + "fgColor")!.Attribute("rgb")).Should().Be("FFFFC7CE");
    }
}
