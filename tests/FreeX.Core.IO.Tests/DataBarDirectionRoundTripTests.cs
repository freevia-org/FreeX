using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Covers the x14 <c>dataBar/@direction</c> attribute (R31-io-conditionalformat-eval-deep-2):
/// it must be read into <see cref="ConditionalFormat.DataBarDirection"/> and written back on save
/// instead of being silently dropped.
/// </summary>
public sealed class DataBarDirectionRoundTripTests
{
    [Fact]
    public void Load_X14DataBarWithRightToLeftDirection_MapsDataBarDirection()
    {
        using var source = CreateXlsxWithX14DataBar(direction: "rightToLeft");

        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarDirection.Should().Be("rightToLeft");
    }

    [Fact]
    public void RoundTrip_X14DataBarWithRightToLeftDirection_PreservesDirectionAttribute()
    {
        using var source = CreateXlsxWithX14DataBar(direction: "rightToLeft");
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("direction")!.Value.Should().Be("rightToLeft");
    }

    [Fact]
    public void RoundTrip_X14DataBarWithContextDirection_PreservesDirectionAttribute()
    {
        // Also cover "context" (mirrors sheet RTL state) so we aren't only special-casing rightToLeft.
        using var source = CreateXlsxWithX14DataBar(direction: "context");
        var workbook = new XlsxFileAdapter().Load(source);
        using var saved = new MemoryStream();

        new XlsxFileAdapter().Save(workbook, saved);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("direction")!.Value.Should().Be("context");
    }

    [Fact]
    public void RoundTrip_X14DataBarWithoutDirectionAttribute_DoesNotEmitDirection()
    {
        // Sibling already-working case: a data bar with no direction attribute at all (Excel's
        // default left-to-right growth) must keep working exactly as before this fix.
        using var source = CreateXlsxWithX14DataBar(direction: null);
        var workbook = new XlsxFileAdapter().Load(source);

        var rule = workbook.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.DataBarDirection.Should().BeNull();

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        var x14DataBar = XlsxPackageTestHelper.ReadWorksheetXml(saved)
            .Descendants(X14Ns + "dataBar").Should().ContainSingle().Subject;
        x14DataBar.Attribute("direction").Should().BeNull();
        // Other existing x14 attributes on this same sibling rule still round-trip correctly.
        x14DataBar.Attribute("border")!.Value.Should().Be("1");
        x14DataBar.Attribute("axisPosition")!.Value.Should().Be("middle");
    }

    [Fact]
    public void Clone_DataBarDirection_IsCopied()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarDirection = "rightToLeft"
        };

        var clone = source.Clone(Guid.NewGuid());

        clone.DataBarDirection.Should().Be("rightToLeft");
    }

    private static MemoryStream CreateXlsxWithX14DataBar(string? direction)
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
                            new XAttribute("showValue", "1"),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "min")),
                            new XElement(MainNs + "cfvo", new XAttribute("type", "max")),
                            new XElement(MainNs + "color", new XAttribute("rgb", "FF0A141E"))),
                        new XElement(MainNs + "extLst",
                            new XElement(MainNs + "ext",
                                new XAttribute("uri", "{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"),
                                new XElement(X14Ns + "id", "{11111111-2222-3333-4444-555555555555}"))))));
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
                                    new XAttribute("id", "{11111111-2222-3333-4444-555555555555}"),
                                    new XElement(X14Ns + "dataBar",
                                        new XAttribute("minLength", "0"),
                                        new XAttribute("maxLength", "100"),
                                        new XAttribute("gradient", "1"),
                                        new XAttribute("border", "1"),
                                        new XAttribute("axisPosition", "middle"),
                                        direction is null ? null : new XAttribute("direction", direction),
                                        new XElement(X14Ns + "cfvo", new XAttribute("type", "autoMin")),
                                        new XElement(X14Ns + "cfvo", new XAttribute("type", "autoMax")))))))));
        });

        return package;
    }

    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
}
