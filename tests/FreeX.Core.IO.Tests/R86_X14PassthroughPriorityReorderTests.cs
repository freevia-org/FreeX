using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 86 regression test (R86-io-conditional-format-roundtrip-5-2): reordering CF rules in the
/// Manage Conditional Formatting dialog reassigns fresh <see cref="ConditionalFormat.Priority"/> values
/// onto the model (see <c>ReplaceAllConditionalFormatsCommand</c>) without touching an x14-only
/// passthrough rule's captured <see cref="ConditionalFormat.NativeChildXmls"/>. On save,
/// <c>XlsxAdvancedConditionalFormatWriter.AppendX14ConditionalFormattingsExt</c> used to re-emit that
/// rule's raw &lt;x14:cfRule&gt; element completely verbatim -- including its original @priority
/// attribute -- so a reorder never actually took effect for x14-only rules in the saved file, even
/// though the classic-rule family (and every other advanced-rule family) always writes the live
/// <see cref="ConditionalFormat.Priority"/> fresh.
/// </summary>
public sealed class R86_X14PassthroughPriorityReorderTests
{
    private const string Sheet1Path = "xl/worksheets/sheet1.xml";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";
    private const string X14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";
    private const string RuleId = "{7F6D8E1A-90C2-4B3D-9A9D-6A6C6E7B8F2C}";

    /// <summary>
    /// Builds a package whose Sheet1 has an x14-only cross-sheet "expression" cfRule (Rule A, initial
    /// priority 1) applied to A1:A10, plus a classic cellIs rule (Rule B, initial priority 2) on the
    /// same range -- mirroring the finding's scenario. Both rules are spliced directly into the raw
    /// worksheet XML (rather than routed through <c>ConditionalFormats.Add</c> before an initial adapter
    /// Save) so the test package's on-disk priorities are exactly as specified, independent of
    /// <c>SplitAndRealignClassicRules</c>'s own lone-classic-rule short-circuit.
    /// </summary>
    private static MemoryStream BuildPackageWithX14AndClassicRule()
    {
        var wb = new Workbook("X14PriorityReorderBook");
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

            // Rule B: classic cellIs rule, priority 2, spliced directly as the worksheet's own
            // <conditionalFormatting> element.
            doc.Root!.Add(new XElement(
                WorksheetNs + "conditionalFormatting",
                new XAttribute("sqref", "A1:A10"),
                new XElement(
                    WorksheetNs + "cfRule",
                    new XAttribute("type", "cellIs"),
                    new XAttribute("priority", "2"),
                    new XAttribute("operator", "greaterThan"),
                    new XElement(WorksheetNs + "formula", "0"))));

            // Rule A: x14-only cross-sheet expression cfRule, priority 1, the same way real Excel
            // stores a rule the classic grammar cannot express.
            var cfRule = new XElement(
                X14Ns + "cfRule",
                new XAttribute("type", "expression"),
                new XAttribute("id", RuleId),
                new XAttribute("priority", "1"),
                new XElement(XmNs + "f", "Sheet2!A1>0"));

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
                            new XElement(XmNs + "sqref", "A1:A10"))))));

            entry.Delete();
            var replacement = archive.CreateEntry(Sheet1Path);
            using var writer = new StreamWriter(replacement.Open());
            doc.Save(writer);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Reads the saved Sheet1 worksheet XML back out of the given package and returns the @priority
    /// attribute of the (single) x14 passthrough cfRule embedded in its extLst.
    /// </summary>
    private static string? ReadX14CfRulePriority(MemoryStream savedStream)
    {
        savedStream.Position = 0;
        using var archive = new ZipArchive(savedStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(Sheet1Path)!;
        using var xmlStream = entry.Open();
        var doc = XDocument.Load(xmlStream);

        var cfRule = doc.Descendants(X14Ns + "cfRule").SingleOrDefault();
        return cfRule?.Attribute("priority")?.Value;
    }

    [Fact]
    public void ReorderingRulesAndSaving_WritesX14PassthroughPriorityFromLiveModel()
    {
        using var stream = BuildPackageWithX14AndClassicRule();
        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet1 = workbook.Sheets.Single(s => s.Name == "Sheet1");

        var ruleA = sheet1.ConditionalFormats.Single(cf => cf.RuleType != CfRuleType.CellValue);
        var ruleB = sheet1.ConditionalFormats.Single(cf => cf.RuleType == CfRuleType.CellValue);
        ruleA.Priority.Should().Be(1, "Rule A (x14-only) started as the highest-precedence rule");
        ruleB.Priority.Should().Be(2, "Rule B (classic) started as the lower-precedence rule");

        // Simulate the Manage Conditional Formatting dialog's reorder: the user moves Rule B above
        // Rule A. ReplaceAllConditionalFormatsCommand reassigns fresh Priority values onto the model
        // objects without touching NativeChildXmls -- exactly reproduced here.
        ruleB.Priority = 1;
        ruleA.Priority = 2;

        using var savedStream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, savedStream);

        var writtenPriority = ReadX14CfRulePriority(savedStream);
        writtenPriority.Should().Be("2",
            "the x14 passthrough rule's saved priority must reflect the live model value " +
            "(Rule A was reassigned to priority 2 by the reorder), not the stale value captured at " +
            "read time");

        // Reload and confirm precedence round-trips: the classic rule (now priority 1) must come
        // back ahead of the x14 passthrough rule (now priority 2).
        savedStream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(savedStream);
        var reloadedSheet1 = reloaded.Sheets.Single(s => s.Name == "Sheet1");
        var reloadedRuleA = reloadedSheet1.ConditionalFormats.Single(cf => cf.RuleType != CfRuleType.CellValue);
        var reloadedRuleB = reloadedSheet1.ConditionalFormats.Single(cf => cf.RuleType == CfRuleType.CellValue);

        reloadedRuleB.Priority.Should().Be(1, "the reordered classic rule must round-trip as priority 1");
        reloadedRuleA.Priority.Should().Be(2, "the reordered x14 passthrough rule must round-trip as priority 2");
    }

    /// <summary>
    /// Sibling no-regression case: when the rules are saved WITHOUT any reorder, the x14 passthrough
    /// rule's priority must still round-trip unchanged (this must not regress into always forcing some
    /// other value, or into losing the attribute).
    /// </summary>
    [Fact]
    public void SavingWithoutReorder_PreservesX14PassthroughPriorityUnchanged()
    {
        using var stream = BuildPackageWithX14AndClassicRule();
        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet1 = workbook.Sheets.Single(s => s.Name == "Sheet1");

        var ruleA = sheet1.ConditionalFormats.Single(cf => cf.RuleType != CfRuleType.CellValue);
        ruleA.Priority.Should().Be(1);

        using var savedStream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, savedStream);

        var writtenPriority = ReadX14CfRulePriority(savedStream);
        writtenPriority.Should().Be("1",
            "with no reorder, the x14 passthrough rule's priority must still be written from the " +
            "(unchanged) live model value");
    }
}
