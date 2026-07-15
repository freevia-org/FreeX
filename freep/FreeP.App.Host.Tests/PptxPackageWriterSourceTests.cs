using System.IO;
using System.Text.RegularExpressions;

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
