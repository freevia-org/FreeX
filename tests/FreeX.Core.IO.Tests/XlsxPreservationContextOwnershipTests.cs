using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPreservationContextOwnershipTests
{
    private static readonly string[] ContextOwnedPreservers =
    [
        "XlsxExternalLinkReferencePreserver.cs",
        "XlsxLegacyCommentPreserver.cs",
        "XlsxPivotXmlReferencePreserver.cs",
        "XlsxStructuredTableReferencePreserver.cs",
        "XlsxUnsupportedSheetReferencePreserver.cs",
        "XlsxWorkbookMetadataPreserver.cs",
        "XlsxWorksheetDrawingReferencePreserver.cs",
        "XlsxWorksheetFormControlPreserver.cs",
        "XlsxWorksheetMetadataPreserver.cs",
        "XlsxWorksheetPrinterSettingsReferencePreserver.cs",
        "XlsxWorksheetVmlReferencePreserver.cs"
    ];

    [Fact]
    public void Preservers_DoNotRebuildWorkbookPackageContext()
    {
        foreach (var file in ContextOwnedPreservers)
        {
            var source = TestWorkspaceFiles.ReadCoreIoSource(file);
            source.Should().NotContain("sourceArchive.GetEntry(\"xl/workbook.xml\")", file);
            source.Should().NotContain("targetArchive.GetEntry(\"xl/workbook.xml\")", file);
            source.Should().NotContain("sourceArchive.GetEntry(\"xl/_rels/workbook.xml.rels\")", file);
            source.Should().NotContain("targetArchive.GetEntry(\"xl/_rels/workbook.xml.rels\")", file);
            source.Should().NotContain("XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths", file);
        }
    }

    [Fact]
    public void SourcePackagePipeline_CreatesOneSharedContextAndPassesItToEveryApplicablePreserver()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.SourcePackage.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        CountOccurrences(source, "XlsxSourcePackagePreservationContext.TryCreate(").Should().Be(1);
        source.Should().Contain("XlsxWorkbookMetadataPreserver.Preserve(\n            context,");
        source.Should().Contain("XlsxPivotXmlReferencePreserver.Preserve(context)");
        source.Should().Contain("XlsxStructuredTableReferencePreserver.Preserve(context)");
        source.Should().Contain("XlsxExternalLinkReferencePreserver.Preserve(context)");
        source.Should().Contain("XlsxUnsupportedSheetReferencePreserver.Preserve(context, workbook)");
        source.Should().Contain("XlsxWorksheetDrawingReferencePreserver.Preserve(context, drawingPaths)");
        source.Should().Contain("XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(context)");
        source.Should().Contain("XlsxWorksheetVmlReferencePreserver.Preserve(context, workbook)");
        source.Should().Contain("XlsxWorksheetFormControlPreserver.Preserve(context, workbook)");
        source.Should().Contain("XlsxLegacyCommentPreserver.Preserve(workbook, context)");
    }

    [Fact]
    public void WorksheetRelationshipSetup_IsOwnedByPreservationContext()
    {
        var files = new[]
        {
            "XlsxPivotXmlReferencePreserver.cs",
            "XlsxStructuredTableReferencePreserver.cs",
            "XlsxWorksheetDrawingReferencePreserver.cs",
            "XlsxWorksheetFormControlPreserver.cs",
            "XlsxWorksheetPrinterSettingsReferencePreserver.cs",
            "XlsxWorksheetVmlReferencePreserver.cs"
        };

        foreach (var file in files)
        {
            var source = TestWorkspaceFiles.ReadCoreIoSource(file);
            source.Should().NotContain("XlsxRelationshipReader.LoadTargets", file);
            source.Should().NotContain(
                "new XDocument(new XElement(context.PackageRelNs + \"Relationships\"))",
                file);
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
