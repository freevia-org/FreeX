using System.IO;
using System.Text.RegularExpressions;
using Free.Shared.Opc;

namespace FreeP.App.Host.Tests;

public sealed class PptxPackageWriterSourceTests
{
    [Fact]
    public void PreservedRelationshipMerge_UsesSharedOpcRelationshipParser()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageWriter.cs"));

        ExtractMethod(source, "private static void MergePreservedRelationships(")
            .Should()
            .Contain("OpcRelationships.Load(sourceRels)")
            .And.NotContain(".Elements(PkgRels + \"Relationship\")")
            .And.NotContain("Attribute(\"TargetMode\")");
    }

    [Fact]
    public void PreservedContentTypeMerge_UsesSharedOpcContentTypeMerger()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageWriter.cs"));

        ExtractMethod(source, "private static void MergePreservedContentTypes(")
            .Should()
            .Contain("OpcXml.TryLoadXml(bytes)")
            .And.Contain("OpcMediaTypes.MergePreservedContentTypes")
            .And.NotContain(".Elements(contentTypes.Root.Name.Namespace + \"Default\")")
            .And.NotContain(".Elements(sourceTypes.Root.Name.Namespace + \"Override\")");
    }

    [Fact]
    public void PackageRetentionClassification_DelegatesToSharedOpcClassifier()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageWriter.cs"));

        source.Should().Contain("OpcPackageRetentionClassifier WriterOwnedPackageClassifier");
        ExtractMethod(source, "private static bool IsWriterOwnedRelationship(")
            .Should()
            .Contain("WriterOwnedPackageClassifier.IsRegeneratedRelationship")
            .And.NotContain("RegeneratedRelationshipTypes.Contains")
            .And.NotContain("ResolvePackagePath");
        ExtractMethod(source, "private static bool IsWriterOwnedPath(")
            .Should()
            .Contain("WriterOwnedPackageClassifier.IsRegeneratedPart")
            .And.NotContain("StartsWith(\"ppt/slides/\"");
    }

    [Fact]
    public void DocumentProperties_UseSharedOpcPropertyHelpersAndConstants()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageWriter.cs"));

        source.Should()
            .Contain("OpcPackageProperties.CorePropertiesZipEntry")
            .And.Contain("OpcPackageProperties.CorePropertiesPartName")
            .And.Contain("OpcPackageProperties.CorePropertiesContentType")
            .And.Contain("OpcDocumentProperties.BuildCorePropertiesDocument(")
            .And.NotContain("\"docProps/core.xml\"")
            .And.NotContain("\"/docProps/core.xml\"")
            .And.NotContain("\"docProps/app.xml\"")
            .And.NotContain("\"docProps/custom.xml\"");
    }

    [Fact]
    public void DrawingMlSrgbFormatting_UsesSharedRgbHelper()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageWriter.cs"));

        ExtractMethod(source, "private static string FmtColor(")
            .Should()
            .Contain("DrawingMlRgbColor")
            .And.Contain(".ToHexRgb()")
            .And.NotContain("$\"{c.R:X2}");
    }

    [Fact]
    public void PackageRelationshipPaths_UseSharedOpcPathHelpers()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.Core.IO",
            "PptxPackageWriter.cs"));

        source.Should()
            .Contain("OpcPathHelper.GetRelativeZipPath(GetDirectoryName(layoutPath), mediaTarget)")
            .And.Contain("OpcPathHelper.GetRelativeZipPath(GetDirectoryName(masterPath), mediaTarget)")
            .And.Contain("OpcPathHelper.GetRelativeZipPath(GetDirectoryName(slidePath), mediaFileTarget)")
            .And.Contain("OpcPathHelper.GetRelativeZipPath(GetDirectoryName(slidePath), targetPath)")
            .And.Contain("OpcPathHelper.GetRelativeZipPath(\"ppt/slides\", captionPath)")
            .And.Contain("OpcPathHelper.GetRelationshipPartPath(freshPath)")
            .And.NotContain("private static string MakeRelativePath(")
            .And.NotContain("private static string MakePartRelsPath(");
    }

    [Theory]
    [InlineData("ppt/slides", "ppt/media/video1.mp4", "../media/video1.mp4")]
    [InlineData("ppt/slideLayouts", "ppt/media/layout1_video1.mp4", "../media/layout1_video1.mp4")]
    [InlineData("ppt/slideMasters", "ppt/media/master1_video1.mp4", "../media/master1_video1.mp4")]
    [InlineData("ppt/slides", "ppt/ink/ink1.xml", "../ink/ink1.xml")]
    public void PackageRelationshipPaths_PreserveStandardPptxTargets(
        string ownerDirectory,
        string targetPath,
        string expected)
    {
        OpcPathHelper.GetRelativeZipPath(ownerDirectory, targetPath).Should().Be(expected);
    }

    [Theory]
    [InlineData("ppt/ink/ink1.xml", "ppt/ink/_rels/ink1.xml.rels")]
    [InlineData("ppt/models/model1.glb", "ppt/models/_rels/model1.glb.rels")]
    public void PreservedPartRelationshipPaths_PreserveStandardPptxTargets(
        string partPath,
        string expected)
    {
        OpcPathHelper.GetRelationshipPartPath(partPath).Should().Be(expected);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method '{signature}' should exist");

        var nextMethod = Regex.Match(
            source[(start + signature.Length)..],
            @"\r?\n    (private|internal|public) static ");

        return nextMethod.Success
            ? source[start..(start + signature.Length + nextMethod.Index)]
            : source[start..];
    }

}
