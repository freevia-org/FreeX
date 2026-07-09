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

        mainSource.Should().Contain("ShowExportOptionsDialogAsync(ExportContentScope.ActiveSheet, ExportFormat.Pdf)");
        mainSource.Should().Contain("ShowExportOptionsDialogAsync(ToExportContentScope(scope), ExportFormat.Pdf)");
        mainSource.Should().Contain("CreatePortablePdfPrintPlan(exportOptions, WorkbookExportPrintOutputKind.Pdf)");
        mainSource.Should().Contain("CreatePortablePdfPrintPlan(exportOptions, outputKind)");
        mainSource.Should().Contain("TryPreparePortablePdfExportPlan(exportPlan, exportOptions, out var effectiveExportPlan, out var optionsError)");
        mainSource.Should().Contain("Pdf.AvaloniaPdfDocumentExporter.Save(_session.Workbook, effectiveExportPlan, pdfBuffer, options: null, workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter())");
        mainSource.Should().Contain("await TryOpenExportedPdfAsync(path)");

        optionsSource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(format)");
        optionsSource.Should().Contain("ExportOptionsDialogSurfacePlanner.CreateResult(");
        optionsSource.Should().Contain("ExportPlanner.TryCreatePageRange(");
        optionsSource.Should().Contain("ExportPlanner.TryNormalizePdfLanguage(");
        optionsSource.Should().Contain("ExportPlanner.TryValidatePublishOptions(");
        optionsSource.Should().Contain("ExportPlanner.TryValidatePageRange(");
        optionsSource.Should().Contain("ApplyPageRangeToPortablePdfExportPlan(");
        optionsSource.Should().Contain("launcher.LaunchUriAsync(new Uri(Path.GetFullPath(path)))");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
