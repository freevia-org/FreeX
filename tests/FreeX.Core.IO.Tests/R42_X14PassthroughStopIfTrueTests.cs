using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 42 regression test (R42-io-cf-dxf-priority-3-2): an x14-only passthrough conditional-format
/// rule (e.g. a cross-sheet "expression" rule, which Excel can ONLY store in the x14 extension because
/// the classic ST cfRule formula grammar cannot carry a cross-sheet reference — see
/// <see cref="R35_X14UnhandledCfRulePassthroughTests"/>) never read its <c>stopIfTrue</c> attribute in
/// <c>XlsxFileAdapter.ReadX14UnhandledConditionalFormatRules</c>. The model's <see
/// cref="ConditionalFormat.StopIfTrue"/> always came back <see langword="false"/> for these rules
/// regardless of what the file said, so <see cref="ViewportConditionalFormatEvaluator"/> (which already
/// generically honors <c>StopIfTrue</c> for every rule kind) could never suppress a lower-priority rule
/// underneath an x14 passthrough rule, even though real Excel does.
/// </summary>
public sealed class R42_X14PassthroughStopIfTrueTests
{
    private const string Sheet1Path = "xl/worksheets/sheet1.xml";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";
    private const string RuleId = "{7F6D8E1A-90C2-4B3D-9A9D-6A6C6E7B8F2C}";

    /// <summary>
    /// Builds a package whose Sheet1 has an x14-only "expression" cfRule (a cross-sheet-formula rule
    /// with no classic cfRule fallback, exactly how real Excel stores this shape of rule) applied to
    /// A1:A5, carrying the given <c>stopIfTrue</c> attribute value (or none, when null). A1 and A2 both
    /// hold the value 1 (a duplicate pair); A3:A5 hold distinct values, so the passthrough rule's
    /// generic (DuplicateValues-modeled) condition is true only for A1/A2.
    /// </summary>
    private static MemoryStream BuildPackageWithX14OnlyRule(string? stopIfTrue)
    {
        var wb = new Workbook("X14StopIfTrueBook");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var values = new[] { 1d, 1d, 2d, 3d, 4d };
        for (uint row = 1; row <= 5; row++)
            sheet1.SetCell(new CellAddress(sheet1.Id, row, 1), new NumberValue(values[row - 1]));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(5));

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(Sheet1Path)!;
            XDocument doc;
            using (var xmlStream = entry.Open())
                doc = XDocument.Load(xmlStream);

            var cfRule = new XElement(
                X14Ns + "cfRule",
                new XAttribute("type", "expression"),
                new XAttribute("id", RuleId),
                new XAttribute("priority", "1"),
                new XElement(XmNs + "f", "Sheet2!A1>0"));
            if (stopIfTrue is not null)
                cfRule.Add(new XAttribute("stopIfTrue", stopIfTrue));

            doc.Root!.Add(new XElement(
                WorksheetNs + "extLst",
                new XElement(
                    WorksheetNs + "ext",
                    new XAttribute(XNamespace.Xmlns + "x14", X14Ns.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "xm", XmNs.NamespaceName),
                    new XAttribute("uri", X14CfUri),
                    new XElement(
                        X14Ns + "conditionalFormattings",
                        new XElement(
                            X14Ns + "conditionalFormatting",
                            cfRule,
                            new XElement(XmNs + "sqref", "A1:A5"))))));

            entry.Delete();
            var replacement = archive.CreateEntry(Sheet1Path);
            using var writer = new StreamWriter(replacement.Open());
            doc.Save(writer);
        }

        stream.Position = 0;
        return stream;
    }

    private static ViewportModel GetViewport(Workbook wb, Sheet sheet) =>
        new ViewportService().GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));

    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void Load_X14OnlyRuleWithStopIfTrue_PreservesStopIfTrueFlag()
    {
        using var stream = BuildPackageWithX14OnlyRule("1");

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet1 = workbook.Sheets.Single(s => s.Name == "Sheet1");

        var cf = sheet1.ConditionalFormats.Should().ContainSingle(
            "the x14-only rule has no classic fallback, so it must be captured by the raw " +
            "passthrough path instead of being silently dropped on load").Subject;

        cf.StopIfTrue.Should().BeTrue(
            "the x14:cfRule had stopIfTrue=\"1\" in the file, and the passthrough reader must not " +
            "silently drop that attribute the way it used to");
    }

    [Fact]
    public void Load_X14OnlyRuleWithoutStopIfTrue_DefaultsToFalse()
    {
        using var stream = BuildPackageWithX14OnlyRule(stopIfTrue: null);

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet1 = workbook.Sheets.Single(s => s.Name == "Sheet1");

        var cf = sheet1.ConditionalFormats.Should().ContainSingle().Subject;
        cf.StopIfTrue.Should().BeFalse(
            "no stopIfTrue attribute was present in the file, so the passthrough rule must not " +
            "fabricate one");
    }

    [Fact]
    public void X14OnlyRuleWithStopIfTrue_SuppressesLowerPriorityFillRuleWhereItsConditionMatches()
    {
        using var stream = BuildPackageWithX14OnlyRule("1");
        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet1 = workbook.Sheets.Single(s => s.Name == "Sheet1");

        // Sanity: the x14 passthrough rule (priority 1) really did come back with StopIfTrue set.
        var passthrough = sheet1.ConditionalFormats.Single();
        passthrough.StopIfTrue.Should().BeTrue();
        passthrough.Priority.Should().Be(1);

        // Lower-priority (priority 2) classic-style rule that would otherwise fill every cell red.
        // Modeled directly in-memory here, mirroring what XlsxConditionalFormatClosedXmlMapper would
        // have produced for a real classic <cfRule> on the same range.
        sheet1.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = passthrough.AppliesTo,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var vp = GetViewport(workbook, sheet1);
        var red = new CellColor(255, 0, 0);

        // A1/A2 share the duplicate value 1, so the passthrough rule's (DuplicateValues-modeled)
        // condition is met there -- with StopIfTrue set, Excel suppresses the lower-priority fill rule.
        GetCell(vp, 1, 1).Style?.FillColor.Should().NotBe(red,
            "row 1: the higher-priority x14 passthrough rule's StopIfTrue must suppress the fill rule");
        GetCell(vp, 2, 1).Style?.FillColor.Should().NotBe(red,
            "row 2: the higher-priority x14 passthrough rule's StopIfTrue must suppress the fill rule");

        // A3:A5 hold distinct values, so the passthrough rule's condition is NOT met there -- the
        // lower-priority fill rule must still apply normally.
        for (uint row = 3; row <= 5; row++)
        {
            GetCell(vp, row, 1).Style?.FillColor.Should().Be(red,
                $"row {row}: the passthrough rule's condition does not match a unique value, so it " +
                "must not suppress the lower-priority fill rule");
        }
    }

    /// <summary>
    /// Sibling no-regression case: when the x14-only rule has NO stopIfTrue attribute, its (matching)
    /// condition must NOT suppress the lower-priority fill rule anywhere -- StopIfTrue must stay
    /// opt-in, not become accidentally sticky once the attribute is read at all.
    /// </summary>
    [Fact]
    public void X14OnlyRuleWithoutStopIfTrue_DoesNotSuppressLowerPriorityFillRule()
    {
        using var stream = BuildPackageWithX14OnlyRule(stopIfTrue: null);
        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet1 = workbook.Sheets.Single(s => s.Name == "Sheet1");

        var passthrough = sheet1.ConditionalFormats.Single();
        passthrough.StopIfTrue.Should().BeFalse();

        sheet1.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = passthrough.AppliesTo,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var vp = GetViewport(workbook, sheet1);
        var red = new CellColor(255, 0, 0);

        for (uint row = 1; row <= 5; row++)
        {
            GetCell(vp, row, 1).Style?.FillColor.Should().Be(red,
                $"row {row}: without stopIfTrue on the passthrough rule, the lower-priority fill rule " +
                "must still apply everywhere its own condition matches");
        }
    }
}
