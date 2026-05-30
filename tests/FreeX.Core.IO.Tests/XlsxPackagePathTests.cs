using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

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
    [InlineData("xl/drawings/drawing1.xml", "../media/image%201.png", "xl/media/image 1.png")]
    [InlineData("xl/drawings/drawing1.xml", "../media/image%2F1.png", "xl/media/image%2F1.png")]
    [InlineData("xl/drawings/drawing1.xml", "../media/image%5C1.png", "xl/media/image%5C1.png")]
    [InlineData("xl/drawings/drawing1.xml", "%2E/media/image.png", "xl/drawings/%2E/media/image.png")]
    [InlineData("xl/drawings/drawing1.xml", "%2E%2E/media/image.png", "xl/drawings/%2E%2E/media/image.png")]
    [InlineData("xl/drawings/drawing1.xml", "../media/image%E0%A4%A.png", "xl/media/image%E0%A4%A.png")]
    [InlineData("xl/workbook.xml", "/xl/externalLinks/externalLink1.xml", "xl/externalLinks/externalLink1.xml")]
    [InlineData("xl/workbook.xml", "xl/sharedStrings.xml", "xl/sharedStrings.xml")]
    public void ResolveRelationshipTarget_NormalizesRelativeAndAbsoluteTargets(string sourcePath, string target, string expected)
    {
        XlsxPackagePath.ResolveRelationshipTarget(sourcePath, target).Should().Be(expected);
    }

    [Theory]
    [InlineData("xl/worksheets/sheet1.xml", "xl/drawings/drawing1.xml", "../drawings/drawing1.xml")]
    [InlineData("xl/worksheets/sheet1.xml", "xl/media/image 1.png", "../media/image%201.png")]
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

    [Fact]
    public void RelationshipPathEscaping_FastPathsSafeTargetsBeforeSplittingSegments()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxPackagePath.cs"));
        var escapingHelpers = source[
            source.IndexOf("private static string UnescapePathSegments", StringComparison.Ordinal)..
            source.IndexOf("private static string UnescapePathSegment(string segment)", StringComparison.Ordinal)];

        escapingHelpers.Should().Contain("if (!path.Contains('%', StringComparison.Ordinal))");
        escapingHelpers.Should().Contain("if (!PathNeedsEscaping(path))");
        escapingHelpers.Should().Contain("private static bool PathNeedsEscaping(string path)");
        escapingHelpers.IndexOf("if (!path.Contains('%', StringComparison.Ordinal))", StringComparison.Ordinal)
            .Should()
            .BeLessThan(escapingHelpers.IndexOf("path.Split('/').Select(UnescapePathSegment)", StringComparison.Ordinal));
        escapingHelpers.IndexOf("if (!PathNeedsEscaping(path))", StringComparison.Ordinal)
            .Should()
            .BeLessThan(escapingHelpers.IndexOf("path.Split('/').Select(EscapePathSegment)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("xl/media/image1.jpeg", "image/jpeg")]
    [InlineData("xl/media/image1.JPEG", "image/jpeg")]
    [InlineData("xl/media/image1.bmp", "image/bmp")]
    [InlineData("xl/media/image1.BMP", "image/bmp")]
    [InlineData("xl/media/image1.gif", "image/gif")]
    [InlineData("xl/media/image1.GIF", "image/gif")]
    [InlineData("xl/media/image1.png", "image/png")]
    [InlineData("xl/media/image1.unknown", "image/png")]
    public void GetImageContentType_MapsSupportedImageExtensions(string path, string expected)
    {
        XlsxPackagePath.GetImageContentType(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("IMAGE/JPEG", ".jpg")]
    [InlineData("image/bmp", ".bmp")]
    [InlineData("IMAGE/BMP", ".bmp")]
    [InlineData("image/gif", ".gif")]
    [InlineData("IMAGE/GIF", ".gif")]
    [InlineData("image/png", ".png")]
    [InlineData("application/octet-stream", ".png")]
    public void GetImageExtension_MapsSupportedImageContentTypes(string contentType, string expected)
    {
        XlsxPackagePath.GetImageExtension(contentType).Should().Be(expected);
    }

    [Fact]
    public void ImageMediaMapping_AvoidsLowercaseStringAllocations()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxPackagePath.cs"));
        var mediaMapping = source[
            source.IndexOf("public static string GetImageContentType", StringComparison.Ordinal)..
            source.IndexOf("public static string GetWorksheetBackgroundMediaFileName", StringComparison.Ordinal)];

        mediaMapping.Should().Contain("Path.GetExtension(path.AsSpan())");
        mediaMapping.Should().Contain("StringComparison.OrdinalIgnoreCase");
        mediaMapping.Should().NotContain(
            "ToLowerInvariant()",
            "picture and background media save/load paths should not allocate lowercase copies for MIME or extension checks");
    }

    [Theory]
    [InlineData("background", 2, ".png", "background.png")]
    [InlineData("background.jpg", 2, ".png", "background.jpg")]
    [InlineData(null, 2, ".png", "freexBackground2.png")]
    public void GetWorksheetBackgroundMediaFileName_UsesSafeNamesOrFallback(string? fileName, int index, string extension, string expected)
    {
        XlsxPackagePath.GetWorksheetBackgroundMediaFileName(fileName, index, extension).Should().Be(expected);
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var pathParts = new string[parts.Length + 1];
            pathParts[0] = directory.FullName;
            Array.Copy(parts, 0, pathParts, 1, parts.Length);

            var candidate = Path.Combine(pathParts);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
