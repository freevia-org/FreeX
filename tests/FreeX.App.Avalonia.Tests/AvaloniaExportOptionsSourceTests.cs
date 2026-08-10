using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaExportOptionsSourceTests
{
    [Fact]
    public void LivePdfExport_UsesSharedExportOptionsDialogPolicy()
    {
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var optionsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ExportOptions.cs"));

        mainSource.Should().Contain("WorkbookExportInteractionPlanner.CreateSelectionPlan(");
        mainSource.Should().Contain("WorkbookExportInteractionPlanner.CreateRequestPlan(");
        mainSource.Should().Contain("WorkbookExportInteractionPlanner.CreateResultPlan(");
        mainSource.Should().Contain("PortablePdfExportPlanner.TryApplyOptions(");
        mainSource.Should().Contain("Pdf.AvaloniaPdfDocumentExporter.Save(_session.Workbook, effectiveExportPlan, pdfBuffer, options: null, workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter())");
        mainSource.Should().Contain("await TryOpenExportedPdfAsync(resultPlan.DestinationPath)");
        mainSource.Should().NotContain("private static ExportContentScope ToExportContentScope(");
        mainSource.Should().NotContain("private static WorkbookExportPrintScope ToWorkbookExportPrintScope(");

        optionsSource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(format)");
        optionsSource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateResult(");
        optionsSource.Should().Contain("ExportPlanner.TryCreatePageRange(");
        optionsSource.Should().Contain("ExportPlanner.TryNormalizePdfLanguage(");
        optionsSource.Should().NotContain("TryPreparePortablePdfExportPlan(");
        optionsSource.Should().NotContain("ApplyPageRangeToPortablePdfExportPlan(");
        optionsSource.Should().Contain("DesktopPathLauncher.OpenFileAsync(");
        optionsSource.Should().Contain("target => launcher.LaunchUriAsync(target.LaunchUri)");
        optionsSource.Should().NotContain("LaunchUriAsync(new Uri(Path.GetFullPath(path)))");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
