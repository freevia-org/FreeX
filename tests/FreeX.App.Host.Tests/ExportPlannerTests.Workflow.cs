using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    [Fact]
    public void ExportWorkflow_UsesOptionsDialogSelectionRangeAndOpenAfterPublish()
    {
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PrintExport.cs"));
        var optionsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FreeXOptions.cs"));

        optionsSource.Should().Contain("public string PdfExportLanguage { get; set; } = ExportPlanner.DefaultPdfLanguage;");
        printExport.Should().Contain("saveDlg.FilterIndex == 2");
        printExport.Should().Contain("new ExportOptionsDialog(SheetGrid.SelectedRange is not null, _options.PdfExportLanguage, selectedFormat)");
        printExport.Should().Contain("if (selectedFormat == ExportFormat.Pdf)");
        printExport.Should().Contain("_options.PdfExportLanguage = optionsDialog.Result.PdfLanguage;");
        printExport.Should().Contain("_options.Save();");
        printExport.Should().Contain("ExportPlanner.PlanExport(saveDlg.FileName, selectedFormat, optionsDialog.Result)");
        printExport.Should().Contain("RenderExportDocument(options)");
        printExport.Should().Contain("ExportPlanner.CreateEffectiveOptionsForFormat(options, ExportFormat.Pdf)");
        printExport.Should().Contain("ExportPlanner.CreateEffectiveOptionsForFormat(options, ExportFormat.Xps)");
        printExport.Should().Contain("RenderExportDocument(effectiveOptions)");
        printExport.Should().Contain("RenderExportPaginator(effectiveOptions)");
        printExport.Should().Contain("ApplyExportPageRange(options");
        printExport.Should().Contain("ExportAsPdf(request.Path, ExportPlanner.DescribeRequest(request), request.Options)");
        printExport.Should().Contain("ExportAsXps(request.Path, ExportPlanner.DescribeRequest(request), request.Options)");
        printExport.Should().Contain("ResolveExportRange(options)");
        printExport.Should().Contain("PdfDocumentProperties.FromWorkbook(_workbook, effectiveOptions)");
        printExport.Should().Contain("XpsDocumentProperties.ApplyToPackage(pkg, XpsDocumentProperties.FromWorkbook(_workbook, effectiveOptions))");
        printExport.Should().Contain("ExportPlanner.TryValidatePageRange(effectiveOptions.PageRange, document.Pages.Count");
        printExport.Should().Contain("ExportPlanner.TryValidatePageRange(options.PageRange, paginator.PageCount");
        printExport.Should().Contain("CreatePdfBookmarks(effectiveOptions)");
        printExport.Should().Contain("includeSelectableText: !effectiveOptions.BitmapTextWhenFontsMayNotBeEmbedded");
        printExport.Should().Contain("pdfLanguage: effectiveOptions.PdfLanguage");
        printExport.Should().Contain("options.EffectiveBookmarkMode");
        printExport.Should().Contain(": sheet.Name");
        printExport.Should().Contain("BuildPrintTitleBookmark(sheet)");
        printExport.Should().Contain("Page {pageIndex + 1 + offset}");
        printExport.Should().Contain("OpenExportedFile(request.ActualPath)");
    }
}
