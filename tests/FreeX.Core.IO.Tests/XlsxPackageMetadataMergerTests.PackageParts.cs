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
        using var sourcePackage = new MemoryStream();
        using (var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(sourceArchive, "xl/media/safe.bin", "safe");
            WritePackageEntry(sourceArchive, hostileEntryName, "evil");
        }

        sourcePackage.Position = 0;
        using var targetPackage = new MemoryStream();
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
        using var sourcePackage = new MemoryStream();
        using (var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(sourceArchive, "xl/drawings/vmlDrawing1.vml", "<xml>excel camelCase</xml>");
            WritePackageEntry(sourceArchive, "xl/customXml/item1.xml", "<root/>");
        }

        sourcePackage.Position = 0;
        using var targetPackage = new MemoryStream();
        using (var seed = new ZipArchive(targetPackage, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(seed, "xl/drawings/vmldrawing1.vml", "<xml>closedxml lowercase</xml>");
        }

        targetPackage.Position = 0;
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
        using var sourcePackage = new MemoryStream();
        using (var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(sourceArchive, "xl/drawings/vmlDrawing2.vml", "source");
        }

        sourcePackage.Position = 0;
        using var targetPackage = new MemoryStream();
        using (var generatedArchive = new ZipArchive(targetPackage, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(generatedArchive, "xl/drawings/vmldrawing2.vml", "generated");
        }

        targetPackage.Position = 0;
        using var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(source, target);

        generatedEntriesBeforeMerge.Should().Contain("xl/drawings/vmldrawing2.vml");
        target.Entries.Select(entry => entry.FullName).Should().Equal("xl/drawings/vmldrawing2.vml");
    }
}
