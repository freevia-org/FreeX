using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 36 regression test (R36-io-conditional-format-formula-2-2):
/// <para>
/// A CellIs ("cell value") conditional-format rule's threshold can be a cell reference or formula
/// instead of a plain number -- e.g. real Excel's "Cell Value is BETWEEN $B$1 AND $C$1". Before the
/// fix, <see cref="XlsxConditionalFormatClosedXmlMapper"/>.Save passed the raw reference text
/// straight into ClosedXML's string-typed <c>WhenBetween</c>/<c>WhenGreaterThan</c>/etc overloads,
/// which quote any operand that isn't already flagged as a formula (no leading '=') or a bare
/// number, producing a dead string literal like <c>"$B$1"</c> in the saved worksheet XML instead of
/// a live reference. Reloading that file then yields <c>Value1 == "\"$B$1\""</c> (quotes baked in
/// permanently), and the rule silently stops ever matching because it now compares against the
/// six-character text "$B$1" instead of the live contents of B1.
/// </para>
/// <para>
/// The fix marks a non-numeric, non-already-quoted threshold as a ClosedXML formula (via a leading
/// '=') before handing it to the Whenxxx API, so it round-trips as a live reference/formula operand
/// instead of a quoted literal.
/// </para>
/// </summary>
public sealed class R36_CfCellIsFormulaThresholdTests
{
    private const string Sheet1Path = "xl/worksheets/sheet1.xml";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XDocument LoadWorksheetXml(Stream xlsxStream, string worksheetPath)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(worksheetPath)!;
        using var xmlStream = entry.Open();
        return XDocument.Load(xmlStream);
    }

    [Fact]
    public void RoundTrip_CellIsBetween_WithCellReferenceThresholds_PreservesLiveReferences()
    {
        var workbook = new Workbook("CellIsRefBetween");
        var sheet = workbook.AddSheet("Sheet1");

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(addr, addr),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Between,
            Value1 = "$B$1",
            Value2 = "$C$1",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        // The saved worksheet XML must carry the references as live (unquoted) formula operands,
        // not as quoted string literals.
        var doc = LoadWorksheetXml(stream, Sheet1Path);
        var cfRule = doc.Root!
            .Elements(WorksheetNs + "conditionalFormatting")
            .Elements(WorksheetNs + "cfRule")
            .Single(r => (string?)r.Attribute("type") == "cellIs");
        var formulas = cfRule.Elements(WorksheetNs + "formula").Select(f => f.Value).ToList();
        formulas.Should().BeEquivalentTo(["$B$1", "$C$1"],
            "cell-reference thresholds must be written as live formula operands, not quoted text literals");
        formulas.Should().NotContain(f => f.Contains('"'),
            "a reference threshold must never be baked into a quoted string literal");

        // Reloading must preserve the references verbatim (not the quoted-string-corrupted form).
        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var rule = reloadedSheet!.ConditionalFormats.Should().ContainSingle().Subject;
        rule.Value1.Should().Be("$B$1", "the reference threshold must survive save/reload unquoted");
        rule.Value2.Should().Be("$C$1", "the reference threshold must survive save/reload unquoted");
    }

    [Fact]
    public void RoundTrip_CellIsGreaterOrEqual_WithCellReferenceThreshold_PreservesLiveReference()
    {
        var workbook = new Workbook("CellIsRefGte");
        var sheet = workbook.AddSheet("Sheet1");

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(5)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(addr, addr),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThanOrEqual,
            Value1 = "$B$1",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(0, 255, 0) },
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        var doc = LoadWorksheetXml(stream, Sheet1Path);
        var cfRule = doc.Root!
            .Elements(WorksheetNs + "conditionalFormatting")
            .Elements(WorksheetNs + "cfRule")
            .Single(r => (string?)r.Attribute("type") == "cellIs");
        var formula = cfRule.Element(WorksheetNs + "formula")!.Value;
        formula.Should().Be("$B$1", "a >= reference threshold must round-trip as a live reference, not \"$B$1\"");

        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var rule = reloadedSheet!.ConditionalFormats.Should().ContainSingle().Subject;
        rule.Value1.Should().Be("$B$1");
    }

    /// <summary>
    /// Sibling no-regression case: plain numeric-literal thresholds (the common case, already
    /// covered elsewhere) must keep round-tripping exactly as before -- written bare (no quotes,
    /// no leading '=') and reloaded back to the same numeric text.
    /// </summary>
    [Fact]
    public void RoundTrip_CellIsBetween_WithNumericLiteralThresholds_StillRoundTripsUnquoted()
    {
        var workbook = new Workbook("CellIsNumericBetween");
        var sheet = workbook.AddSheet("Sheet1");

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(30)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(addr, addr),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Between,
            Value1 = "10",
            Value2 = "50",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(198, 239, 206) },
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        var doc = LoadWorksheetXml(stream, Sheet1Path);
        var cfRule = doc.Root!
            .Elements(WorksheetNs + "conditionalFormatting")
            .Elements(WorksheetNs + "cfRule")
            .Single(r => (string?)r.Attribute("type") == "cellIs");
        var formulas = cfRule.Elements(WorksheetNs + "formula").Select(f => f.Value).ToList();
        formulas.Should().BeEquivalentTo(["10", "50"],
            "plain numeric thresholds must remain bare, unquoted numbers");

        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var rule = reloadedSheet!.ConditionalFormats.Should().ContainSingle().Subject;
        rule.Value1.Should().Be("10");
        rule.Value2.Should().Be("50");
    }
}
