using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ExportDocumentPropertiesPlannerTests
{
    private static readonly ExportOptions Included = new(
        ExportContentScope.ActiveSheet,
        IncludeDocumentProperties: true,
        OpenAfterPublish: false);

    [Fact]
    public void FromWorkbook_RequiresTheExportOption()
    {
        var workbook = new Workbook("Budget Model");

        ExportDocumentPropertiesPlanner.FromWorkbook(workbook, ExportOptions.ExcelLikeDefault)
            .Should()
            .BeNull();
    }

    [Fact]
    public void FromWorkbook_NormalizesValuesAndUsesWorkbookUserName()
    {
        var workbook = new Workbook("  Budget Model  ")
        {
            FileSharing = new WorkbookFileSharingModel
            {
                UserName = "  Analyst  ",
            },
        };

        ExportDocumentPropertiesPlanner.FromWorkbook(workbook, Included)
            .Should()
            .Be(new ExportDocumentProperties(
                "Budget Model",
                "Analyst",
                ExportDocumentPropertiesPlanner.DefaultSubject,
                ExportDocumentPropertiesPlanner.DefaultKeywords));
    }

    [Fact]
    public void FromWorkbook_FallsBackToApplicationCreatorForBlankUserName()
    {
        var workbook = new Workbook("Budget Model")
        {
            FileSharing = new WorkbookFileSharingModel
            {
                UserName = "  ",
            },
        };

        ExportDocumentPropertiesPlanner.FromWorkbook(workbook, Included)!.Creator
            .Should()
            .Be(ExportDocumentPropertiesPlanner.DefaultCreator);
        ExportDocumentPropertiesPlanner.Normalize(" \t ").Should().BeNull();
        ExportDocumentPropertiesPlanner.Normalize("  Quarterly Review  ").Should().Be("Quarterly Review");
    }

    [Fact]
    public void NativeExportAdapters_DelegateMetadataOwnershipToPlanner()
    {
        var pdfExporter = Read("src", "FreeX.App.Host", "PdfDocumentExporter.cs");
        var xps = Read("src", "FreeX.App.Host", "XpsPackagePropertiesAdapter.cs");

        pdfExporter.Should().Contain("ExportDocumentPropertiesPlanner.FromWorkbook(workbook, options)");
        pdfExporter.Should().Contain("SharedPdf.PdfDocumentProperties?");
        xps.Should().Contain("ExportDocumentProperties? properties");
        xps.Should().Contain("ExportDocumentPropertiesPlanner.Normalize(properties.Title)");
        pdfExporter.Should().Contain("ExportDocumentPropertiesPlanner.Normalize(properties?.Title)");

        foreach (var adapter in new[] { pdfExporter, xps })
        {
            adapter.Should().NotContain(ExportDocumentPropertiesPlanner.DefaultSubject);
            adapter.Should().NotContain(ExportDocumentPropertiesPlanner.DefaultKeywords);
            adapter.Should().NotContain("ResolveWorkbookUserName(");
            adapter.Should().NotContain("private static string? Normalize(");
        }

        pdfExporter.Should().NotContain("private static string? NormalizeProperty(");
        var hostDirectory = Path.GetDirectoryName(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "PdfDocumentExporter.cs"))!;
        File.Exists(Path.Combine(hostDirectory, "PdfDocumentProperties.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(hostDirectory, "XpsDocumentProperties.cs"))
            .Should().BeFalse();
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
