using Free.Shared.AppServices;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class SisterDialogTextResourceTests
{
    [Fact]
    public void InsertDialogTextResources_ExposeAvaloniaInsertDialogLabels()
    {
        InsertDialogTextResources.Hyperlink.Title.Should().Be("Insert Hyperlink");
        InsertDialogTextResources.Hyperlink.AddressPlaceholder
            .Should().Be("https://\u2026  or  #BookmarkName for an internal link");
        InsertDialogTextResources.Bookmark.GoToButton.Should().Be("Go To");
        InsertDialogTextResources.QuickPart.SnippetPlaceholder
            .Should().Be("Snippet text (one paragraph per line)");
        InsertDialogTextResources.TextFromFilePickerTitle.Should().Be("Insert Text from File");
    }

    [Fact]
    public void FreeWFileTextResources_FormatPdfExportStatus()
    {
        var documentText = FreeWFileTextResources.Document;

        documentText.OpenPickerTitle.Should().Be("Open document");
        documentText.SavePickerTitle.Should().Be("Save document");
        documentText.FallbackDisplayName.Should().Be("Document");
        documentText.NewAction.Should().Be("replace the current document");
        documentText.OpenAction.Should().Be("opening another document");
        documentText.OpenCommand.Should().Be("Open");
        documentText.SaveCommand.Should().Be("Save");
        documentText.InsertPictureCommand.Should().Be("Insert picture");
        documentText.InsertPicturePickerTitle.Should().Be("Insert Picture");
        FreeWFileTextResources.PdfFileTypeName.Should().Be("PDF document");
        FreeWFileTextResources.PictureFileTypeName.Should().Be("Pictures");
        FreeWFileTextResources.TextFromFileTypeName.Should().Be("Documents");
        FreeWFileTextResources.ExportPdfPickerTitle.Should().Be("Export to PDF");
        FreeWFileTextResources.PdfExportCommand.Should().Be("PDF export");
        FreeWFileTextResources.InsertTextCommand.Should().Be("Insert text");
        FreeWFileTextResources.NewWindowCommand.Should().Be("New window");
        SisterAppFileTextPlanner.FormatUnsupportedFileType(documentText, documentText.OpenCommand, ".zip")
            .Should().Be("Open failed: unsupported file type \".zip\".");
        SisterAppFileTextPlanner.FormatCommandFailed(documentText, FreeWFileTextResources.InsertTextCommand, "No adapter")
            .Should().Be("Insert text failed: No adapter");
        FreeWFileTextResources.FormatPdfExported(1, "Skia", "Draft.pdf")
            .Should().Be("Exported PDF (1 page, Skia): Draft.pdf");
        FreeWFileTextResources.FormatPdfExported(3, "Portable", "Draft.pdf")
            .Should().Be("Exported PDF (3 pages, Portable): Draft.pdf");
    }

    [Fact]
    public void BackstageViewTextResources_ExposePaneText()
    {
        BackstageViewTextResources.WindowTitle.Should().Be("FreeW \u2014 File");
        BackstageViewTextResources.Home.Description
            .Should().Be("Start with a new document or reopen a recent file.");
        BackstageViewTextResources.EvidenceSection.Should().Be("Evidence");
        BackstageViewTextResources.PrintPreviewEvidenceLabel.Should().Be("Print preview fidelity");
        BackstageViewTextResources.PdfExportEvidenceLabel.Should().Be("PDF export fidelity");
        BackstageViewTextResources.FixtureReadyEvidenceStatus.Should().Be("Fixture ready");
        BackstageViewTextResources.HostBackedEvidenceStatus.Should().Be("Host backed");
        BackstageViewTextResources.DirectPrintDeferredNote
            .Should().Contain("Create PDF");
    }

    [Fact]
    public void ApplicationFrameTextCatalog_formats_shared_help_messages()
    {
        FreeWApplicationFrameTextCatalog.FormatExternalLinkFailure("Help Online", "https://example.test")
            .Should().Be("FreeW could not open Help Online. The link is:\n\nhttps://example.test");
        FreeWApplicationFrameTextCatalog.FormatClipboardFailure("busy")
            .Should().Be("FreeW could not access the clipboard: busy");
        FreeWApplicationFrameTextCatalog.DiagnosticsCopiedMessage
            .Should().Be("FreeW diagnostics were copied to the clipboard.");
    }
}
