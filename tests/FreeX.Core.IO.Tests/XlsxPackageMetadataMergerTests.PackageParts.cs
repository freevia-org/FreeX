using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxPackageMetadataMergerTests
{
    [Theory]
    [InlineData("/xl/media/rooted.bin")]
    [InlineData("xl\\media\\backslash.bin")]
    [InlineData("xl/../escape.bin")]
    [InlineData("../escape.bin")]
    [InlineData("xl/./dot.bin")]
    [InlineData("xl/media//empty-segment.bin")]
    [InlineData("C:/absolute.bin")]
    [InlineData(" xl/media/padded.bin")]
    public void CopyUnknownPackageParts_SkipsHostileEntryNames(string hostileEntryName)
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/media/safe.bin", "safe"),
            (hostileEntryName, "evil"));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage();
        using var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.CopyUnknownPackageParts(source, target);

        target.Entries.Select(entry => entry.FullName).Should().Equal("xl/media/safe.bin");
    }

    // OPC part names are compared case-insensitively. ClosedXML writes legacy-comment VML as
    // xl/drawings/vmldrawing<N>.vml (lowercase) while Excel authored xl/drawings/vmlDrawing<N>.vml
    // (camelCase). Copying the source part verbatim alongside the generated one produced two
    // case-colliding parts, which makes the package unreadable ("Format error in package") and
    // caused Excel to drop PivotTables/formulas on repair. The merger must skip a source part that
    // collides case-insensitively with one already in the generated package, while still copying
    // genuinely new parts.
    [Fact]
    public void CopyUnknownPackageParts_SkipsCaseCollidingPart_ButCopiesGenuinelyNewParts()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/drawings/vmlDrawing1.vml", "<xml>excel camelCase</xml>"),
            ("xl/customXml/item1.xml", "<root/>"));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(
            ("xl/drawings/vmldrawing1.vml", "<xml>closedxml lowercase</xml>"));
        using (var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxPackageMetadataMerger.CopyUnknownPackageParts(source, target);
        }

        targetPackage.Position = 0;
        using var result = new ZipArchive(targetPackage, ZipArchiveMode.Read);
        var names = result.Entries.Select(entry => entry.FullName).ToList();

        names.Where(name => string.Equals(name, "xl/drawings/vmldrawing1.vml", StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle("a source part colliding only by case must not be duplicated");
        names.Should().Contain("xl/customXml/item1.xml", "genuinely new source parts are still copied");
    }

    [Fact]
    public void CopyUnknownPackageParts_SkipsGeneratedPartWithCaseEquivalentName()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("xl/drawings/vmlDrawing2.vml", "source"));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(("xl/drawings/vmldrawing2.vml", "generated"));
        using var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(source, target);

        generatedEntriesBeforeMerge.Should().Contain("xl/drawings/vmldrawing2.vml");
        target.Entries.Select(entry => entry.FullName).Should().Equal("xl/drawings/vmldrawing2.vml");
    }

    [Fact]
    public void CopyUnknownPackageParts_SkipsInvalidCustomXmlPropertiesPart()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(
            ("customXml/item1.xml", "<root xmlns=\"urn:freex:customXml\"/>"),
            ("customXml/itemProps1.xml", "<notDatastoreItem/>"));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage();
        using var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.CopyUnknownPackageParts(source, target);

        target.GetEntry("customXml/item1.xml").Should().NotBeNull();
        target.GetEntry("customXml/itemProps1.xml").Should().BeNull();
    }

    [Fact]
    public void CopyUnknownPackageParts_SkipsMalformedCustomXmlItemPart()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(("customXml/item1.xml", "<root>"));
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage();
        using var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.CopyUnknownPackageParts(source, target);

        target.GetEntry("customXml/item1.xml").Should().BeNull();
    }
}
