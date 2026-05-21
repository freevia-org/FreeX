using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxPackagePathTests
{
    [Theory]
    [InlineData("worksheets/sheet1.xml", "xl/worksheets/sheet1.xml")]
    [InlineData("/xl/workbook.xml", "xl/workbook.xml")]
    [InlineData("xl/styles.xml", "xl/styles.xml")]
    [InlineData("xl\\theme\\theme1.xml", "xl/theme/theme1.xml")]
    public void NormalizeWorkbookTarget_RootsWorkbookRelationshipsUnderXl(string target, string expected)
    {
        XlsxPackagePath.NormalizeWorkbookTarget(target).Should().Be(expected);
    }

    [Theory]
    [InlineData("xl/worksheets/sheet1.xml", "xl/worksheets/_rels/sheet1.xml.rels")]
    [InlineData("xl/drawings/drawing1.xml", "xl/drawings/_rels/drawing1.xml.rels")]
    [InlineData("workbook.xml", "_rels/workbook.xml.rels")]
    public void GetRelationshipPartPath_ReturnsSiblingRelsPart(string sourcePath, string expected)
    {
        XlsxPackagePath.GetRelationshipPartPath(sourcePath).Should().Be(expected);
    }

    [Theory]
    [InlineData("xl/worksheets/sheet1.xml", "../drawings/drawing1.xml", "xl/drawings/drawing1.xml")]
    [InlineData("xl/drawings/drawing1.xml", "../media/image1.png", "xl/media/image1.png")]
    [InlineData("xl/workbook.xml", "/xl/externalLinks/externalLink1.xml", "xl/externalLinks/externalLink1.xml")]
    [InlineData("xl/workbook.xml", "xl/sharedStrings.xml", "xl/sharedStrings.xml")]
    public void ResolveRelationshipTarget_NormalizesRelativeAndAbsoluteTargets(string sourcePath, string target, string expected)
    {
        XlsxPackagePath.ResolveRelationshipTarget(sourcePath, target).Should().Be(expected);
    }

    [Theory]
    [InlineData("xl/worksheets/sheet1.xml", "xl/drawings/drawing1.xml", "../drawings/drawing1.xml")]
    [InlineData("xl/drawings/drawing1.xml", "xl/charts/chart1.xml", "../charts/chart1.xml")]
    [InlineData("xl/workbook.xml", "xl/sharedStrings.xml", "sharedStrings.xml")]
    public void GetRelationshipTarget_ReturnsExcelStyleRelativeTargets(string sourcePath, string targetPath, string expected)
    {
        XlsxPackagePath.GetRelationshipTarget(sourcePath, targetPath).Should().Be(expected);
    }

    [Theory]
    [InlineData("xl/worksheets/../drawings/./drawing1.xml", "xl/drawings/drawing1.xml")]
    [InlineData("/xl//media/../media/image1.png", "xl/media/image1.png")]
    public void NormalizeZipPath_CollapsesDotSegments(string path, string expected)
    {
        XlsxPackagePath.NormalizeZipPath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("xl/media/image1.jpeg", "image/jpeg")]
    [InlineData("xl/media/image1.bmp", "image/bmp")]
    [InlineData("xl/media/image1.gif", "image/gif")]
    [InlineData("xl/media/image1.png", "image/png")]
    [InlineData("xl/media/image1.unknown", "image/png")]
    public void GetImageContentType_MapsSupportedImageExtensions(string path, string expected)
    {
        XlsxPackagePath.GetImageContentType(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/bmp", ".bmp")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/png", ".png")]
    [InlineData("application/octet-stream", ".png")]
    public void GetImageExtension_MapsSupportedImageContentTypes(string contentType, string expected)
    {
        XlsxPackagePath.GetImageExtension(contentType).Should().Be(expected);
    }

    [Theory]
    [InlineData("background", 2, ".png", "background.png")]
    [InlineData("background.jpg", 2, ".png", "background.jpg")]
    [InlineData(null, 2, ".png", "freexcelBackground2.png")]
    public void GetWorksheetBackgroundMediaFileName_UsesSafeNamesOrFallback(string? fileName, int index, string extension, string expected)
    {
        XlsxPackagePath.GetWorksheetBackgroundMediaFileName(fileName, index, extension).Should().Be(expected);
    }
}
