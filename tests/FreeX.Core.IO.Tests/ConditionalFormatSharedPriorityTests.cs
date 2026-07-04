using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for G15: classic (CellIs/Expression) conditional-format rules must be
/// numbered from the SAME priority sequence as advanced (ColorScale/DataBar/IconSet/long-tail)
/// rules, preserving each rule's true relative order from the source file. Previously the classic
/// mapper (<see cref="XlsxConditionalFormatClosedXmlMapper"/>) renumbered its rules from its own
/// private 1..N counter, independent of and colliding with the real priorities the advanced rules
/// carry — corrupting the evaluation/stacking order between the two rule families.
/// </summary>
public sealed class ConditionalFormatSharedPriorityTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_MixedColorScaleAndCellIsRules_PreservesTrueFilePriorityOrder()
    {
        // Real file priority order: ColorScaleA(1), ColorScaleB(2), CellIs(3).
        // The old classic-mapper counter would (mis)assign the single CellIs rule Priority=1
        // (its own counter only sees one CellIs rule), colliding with ColorScaleA and sorting
        // CellIs between the two color scales instead of after both.
        using var source = XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "colorScale"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "colorScale",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF00AA00")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FFAA0000"))))),
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "B1:B5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "colorScale"),
                        new XAttribute("priority", "2"),
                        new XElement(MainNs + "colorScale",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF0000AA")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FFAAAA00"))))),
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "C1:C5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "cellIs"),
                        new XAttribute("priority", "3"),
                        new XAttribute("operator", "greaterThan"),
                        new XElement(MainNs + "formula", "10"))));
        });

        var workbook = new XlsxFileAdapter().Load(source);
        var rules = workbook.GetSheetAt(0).ConditionalFormats
            .OrderBy(r => r.Priority)
            .ToArray();

        rules.Should().HaveCount(3);
        rules[0].RuleType.Should().Be(CfRuleType.ColorScale);
        rules[0].AppliesTo.Start.Col.Should().Be(1, because: "A1:A5 is the first, highest-priority rule");
        rules[1].RuleType.Should().Be(CfRuleType.ColorScale);
        rules[1].AppliesTo.Start.Col.Should().Be(2, because: "B1:B5 is the second color scale, priority 2");
        rules[2].RuleType.Should().Be(CfRuleType.CellValue);
        rules[2].AppliesTo.Start.Col.Should().Be(3, because: "C1:C5's CellIs rule is truly last (priority 3), not first");

        // The three priorities must be distinct — no collision between the classic rule and
        // either advanced rule.
        rules.Select(r => r.Priority).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void Load_CellIsBeforeColorScale_PreservesTrueFilePriorityOrder()
    {
        // Real file priority order reversed: CellIs(1), ColorScale(2). Also verifies the shared
        // sequence isn't merely "advanced always first" by coincidence.
        using var source = XlsxPackageTestHelper.CreatePackageWithPatchedWorksheet(root =>
        {
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "cellIs"),
                        new XAttribute("priority", "1"),
                        new XAttribute("operator", "lessThan"),
                        new XElement(MainNs + "formula", "5"))),
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "B1:B5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "colorScale"),
                        new XAttribute("priority", "2"),
                        new XElement(MainNs + "colorScale",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF00AA00")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FFAA0000"))))));
        });

        var workbook = new XlsxFileAdapter().Load(source);
        var rules = workbook.GetSheetAt(0).ConditionalFormats
            .OrderBy(r => r.Priority)
            .ToArray();

        rules.Should().HaveCount(2);
        rules[0].RuleType.Should().Be(CfRuleType.CellValue, because: "CellIs has the true lower (higher-precedence) priority 1");
        rules[1].RuleType.Should().Be(CfRuleType.ColorScale);
        rules[0].Priority.Should().BeLessThan(rules[1].Priority);
    }
}
