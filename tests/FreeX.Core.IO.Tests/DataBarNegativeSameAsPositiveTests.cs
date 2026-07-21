using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for R61-io-cf-databar-x14-6-4: the x14 dataBar's
/// <c>negativeBarColorSameAsPositive</c>/<c>negativeBarBorderColorSameAsPositive</c> toggle
/// attributes must round-trip. Before the fix, neither attribute was ever read (no model field
/// existed to hold them) nor written, so a data bar explicitly configured with "Negative Value and
/// Axis" -&gt; Same as Positive Value silently lost that setting on load and on save.
/// </summary>
public sealed class DataBarNegativeSameAsPositiveTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void Load_X14DataBarWithSameAsPositiveToggles_MapsToModelFlags()
    {
        using var source = CreateXlsxWithSameAsPositiveDataBar();

        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarNegativeFillSameAsPositive.Should().BeTrue(
            "the source file explicitly set negativeBarColorSameAsPositive=\"1\"");
        rule.DataBarNegativeBorderSameAsPositive.Should().BeTrue(
            "the source file explicitly set negativeBarBorderColorSameAsPositive=\"1\"");
        // Excel omits the now-redundant color children in this case -- there is nothing to resolve.
        rule.DataBarNegativeFillColor.Should().BeNull();
        rule.DataBarNegativeBorderColor.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_X14DataBarWithSameAsPositiveToggles_WritesAttributesAndOmitsColorChildren()
    {
        using var source = CreateXlsxWithSameAsPositiveDataBar();
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(saved).Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("negativeBarColorSameAsPositive")?.Value.Should().Be("1",
            "the toggle must be re-emitted on save, not silently dropped");
        x14DataBar.Attribute("negativeBarBorderColorSameAsPositive")?.Value.Should().Be("1",
            "the toggle must be re-emitted on save, not silently dropped");
        x14DataBar.Element(X14Ns + "negativeFillColor").Should().BeNull(
            "the color child is redundant once same-as-positive is set");
        x14DataBar.Element(X14Ns + "negativeBorderColor").Should().BeNull(
            "the color child is redundant once same-as-positive is set");
    }

    [Fact]
    public void RoundTrip_X14DataBarWithExplicitNegativeColors_StillWritesColorsAndNoSameAsPositiveAttributes()
    {
        // Sibling no-regression test: a data bar with EXPLICIT negative colors (the pre-existing,
        // already-covered case) must keep writing those colors and must not gain the new
        // same-as-positive attributes now that the writer knows about them.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarGradient = false,
            DataBarNegativeFillColor = new RgbColor(4, 5, 6),
            DataBarNegativeBorderColor = new RgbColor(7, 8, 9),
        });
        using var stream = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, stream);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(stream).Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Element(X14Ns + "negativeFillColor")?.Attribute("rgb")?.Value.Should().Be("FF040506");
        x14DataBar.Element(X14Ns + "negativeBorderColor")?.Attribute("rgb")?.Value.Should().Be("FF070809");
        x14DataBar.Attribute("negativeBarColorSameAsPositive").Should().BeNull();
        x14DataBar.Attribute("negativeBarBorderColorSameAsPositive").Should().BeNull();
    }

    private static MemoryStream CreateXlsxWithSameAsPositiveDataBar()
    {
        var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchWorksheetXml(package, xml =>
        {
            var root = xml.Root!;
            root.Add(
                new XElement(MainNs + "conditionalFormatting",
                    new XAttribute("sqref", "A1:A5"),
                    new XElement(MainNs + "cfRule",
                        new XAttribute("type", "dataBar"),
                        new XAttribute("priority", "1"),
                        new XElement(MainNs + "dataBar",
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF0A141E"))),
                        new XElement(MainNs + "extLst",
                            new XElement(MainNs + "ext",
                                new XAttribute("uri", "{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"),
                                new XElement(X14Ns + "id", "{22222222-3333-4444-5555-666666666666}"))))));
            root.Add(
                new XElement(MainNs + "extLst",
                    new XElement(MainNs + "ext",
                        new XAttribute(XNamespace.Xmlns + "x14", X14Ns.NamespaceName),
                        new XAttribute("uri", "{78C0D931-6437-407d-A8EE-F0AAD7539E65}"),
                        new XElement(X14Ns + "conditionalFormattings",
                            new XElement(X14Ns + "conditionalFormatting",
                                new XAttribute("sqref", "A1:A5"),
                                new XElement(X14Ns + "cfRule",
                                    new XAttribute("type", "dataBar"),
                                    new XAttribute("id", "{22222222-3333-4444-5555-666666666666}"),
                                    new XElement(X14Ns + "dataBar",
                                        new XAttribute("gradient", "1"),
                                        new XAttribute("negativeBarColorSameAsPositive", "1"),
                                        new XAttribute("negativeBarBorderColorSameAsPositive", "1"),
                                        new XElement(X14Ns + "cfvo", new XAttribute("type", "autoMin")),
                                        new XElement(X14Ns + "cfvo", new XAttribute("type", "autoMax")))))))));
        });

        return package;
    }
}
