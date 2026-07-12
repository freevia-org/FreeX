using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 35 backlog regression test (R35-deferred-x14-unknown-cfrule-1):
/// <para>
/// The x14:conditionalFormattings reader used to understand only iconSet and dataBar cfRule types.
/// Every other x14-only rule -- most notably an "expression" rule whose formula references another
/// worksheet, which Excel can ONLY store as an x14:cfRule because the classic ST cfRule formula
/// grammar cannot carry a cross-sheet reference -- had no classic &lt;conditionalFormatting&gt;
/// fallback to read from at all, so it was silently dropped on load and never re-emitted on save.
/// </para>
/// <para>
/// The fix captures the raw &lt;x14:cfRule&gt; XML verbatim (via
/// <c>XlsxFileAdapter.ReadX14UnhandledConditionalFormatRules</c>) on a synthetic, inert
/// <see cref="ConditionalFormat"/> and re-emits it byte-for-byte
/// (via <see cref="XlsxAdvancedConditionalFormatWriter"/>'s raw-passthrough routing) instead of
/// modeling/evaluating it, so the rule survives a load → save round-trip.
/// </para>
/// </summary>
public sealed class R35_X14UnhandledCfRulePassthroughTests
{
    private const string Sheet1Path = "xl/worksheets/sheet1.xml";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";
    private const string RuleId = "{DAD2AE98-8CE1-4A70-8880-6060EED1EF48}";

    /// <summary>
    /// Builds a package whose Sheet1 has an x14-only "expression" cfRule (a cross-sheet-formula rule,
    /// e.g. <c>=Sheet2!A1&gt;10</c> applied to A1:A10) with NO classic cfRule fallback at all -- exactly
    /// how real Excel stores this shape of rule.
    /// </summary>
    private static MemoryStream BuildPackageWithX14OnlyExpressionRule()
    {
        var wb = new Workbook("X14OnlyExpressionBook");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        for (uint row = 1; row <= 10; row++)
            sheet1.SetCell(new CellAddress(sheet1.Id, row, 1), new NumberValue(row));
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
                            new XElement(
                                X14Ns + "cfRule",
                                new XAttribute("type", "expression"),
                                new XAttribute("id", RuleId),
                                new XElement(XmNs + "f", "Sheet2!A1>10")),
                            new XElement(XmNs + "sqref", "A1:A10"))))));

            entry.Delete();
            var replacement = archive.CreateEntry(Sheet1Path);
            using var writer = new StreamWriter(replacement.Open());
            doc.Save(writer);
        }

        stream.Position = 0;
        return stream;
    }

    private static XDocument LoadWorksheetXml(Stream xlsxStream, string worksheetPath)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(worksheetPath)!;
        using var xmlStream = entry.Open();
        return XDocument.Load(xmlStream);
    }

    [Fact]
    public void Load_X14OnlyExpressionRule_IsCapturedNotDropped()
    {
        using var stream = BuildPackageWithX14OnlyExpressionRule();

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet1 = workbook.Sheets.Single(s => s.Name == "Sheet1");

        // Before the fix this rule was silently dropped on load: ConditionalFormats would be empty.
        sheet1.ConditionalFormats.Should().ContainSingle(
            "the x14-only expression rule has no classic fallback, so it must be captured by the new " +
            "passthrough path instead of being silently dropped on load");

        var captured = sheet1.ConditionalFormats.Single();
        captured.AppliesTo.Should().Be(new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 10, 1)));
    }

    [Fact]
    public void RoundTrip_X14OnlyExpressionRule_SurvivesLoadSaveByteForByte()
    {
        using var firstStream = BuildPackageWithX14OnlyExpressionRule();

        var workbook = new XlsxFileAdapter().Load(firstStream);

        using var secondStream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, secondStream);

        var doc = LoadWorksheetXml(secondStream, Sheet1Path);
        var root = doc.Root!;

        // The rule must NOT have been fabricated into a classic <conditionalFormatting><cfRule> block --
        // there was no classic fallback for it in the source file, and none must be invented on save.
        root.Elements(WorksheetNs + "conditionalFormatting").Should().BeEmpty(
            "an x14-only rule with no classic counterpart in the source file must not gain a fabricated " +
            "classic cfRule on save");

        // The x14 ext block itself must survive with the rule's real id, type, formula, and range intact.
        var x14CfRules = root
            .Elements(WorksheetNs + "extLst")
            .Elements(WorksheetNs + "ext")
            .Where(e => (string?)e.Attribute("uri") == X14CfUri)
            .Elements(X14Ns + "conditionalFormattings")
            .Elements(X14Ns + "conditionalFormatting")
            .ToList();

        x14CfRules.Should().ContainSingle("the x14-only expression rule must survive the round-trip");

        var x14Cf = x14CfRules.Single();
        var cfRule = x14Cf.Element(X14Ns + "cfRule");
        cfRule.Should().NotBeNull();
        ((string?)cfRule!.Attribute("type")).Should().Be("expression");
        ((string?)cfRule.Attribute("id")).Should().Be(RuleId);
        cfRule.Element(XmNs + "f")?.Value.Should().Be("Sheet2!A1>10",
            "the cross-sheet formula must be preserved verbatim, not reinterpreted");

        ((string?)x14Cf.Element(XmNs + "sqref")).Should().Be("A1:A10");
    }

    /// <summary>
    /// Sibling no-regression case: a normal, fully-modeled advanced rule (dataBar) that has NO x14
    /// companion at all must keep round-tripping exactly as before -- the new raw-passthrough routing
    /// in <see cref="XlsxAdvancedConditionalFormatWriter"/> must not misclassify it and must not
    /// fabricate an x14 ext block for it.
    /// </summary>
    [Fact]
    public void RoundTrip_PlainDataBarWithNoX14Companion_StillRoundTripsNormally()
    {
        var wb = new Workbook("PlainDataBarBook");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(0x63, 0xBE, 0x7B),
            // Defaults (gradient=true, no border/axis/negative colors) require no x14 companion at all.
        });

        using var firstStream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, firstStream);
        firstStream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(firstStream);
        var reloadedSheet = reloaded.Sheets.Single(s => s.Name == "Sheet1");
        reloadedSheet.ConditionalFormats.Should().ContainSingle();
        reloadedSheet.ConditionalFormats.Single().RuleType.Should().Be(CfRuleType.DataBar);

        using var secondStream = new MemoryStream();
        new XlsxFileAdapter().Save(reloaded, secondStream);

        var doc = LoadWorksheetXml(secondStream, Sheet1Path);
        var root = doc.Root!;

        root.Elements(WorksheetNs + "conditionalFormatting").Should().ContainSingle(
            "the plain data-bar rule must still be written as a classic cfRule");

        var hasX14CfExt = root
            .Elements(WorksheetNs + "extLst")
            .Elements(WorksheetNs + "ext")
            .Any(e => (string?)e.Attribute("uri") == X14CfUri);
        hasX14CfExt.Should().BeFalse(
            "a plain data-bar with no extended properties and no prior x14 id must not gain a spurious x14 ext block");
    }
}
