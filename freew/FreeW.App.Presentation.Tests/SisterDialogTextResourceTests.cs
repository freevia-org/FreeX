using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;

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
        FreeWFileTextResources.PdfFileTypeName.Should().Be("PDF document");
        FreeWFileTextResources.FormatPdfExported(1, "Skia", "Draft.pdf")
            .Should().Be("Exported PDF (1 page, Skia): Draft.pdf");
        FreeWFileTextResources.FormatPdfExported(3, "Portable", "Draft.pdf")
            .Should().Be("Exported PDF (3 pages, Portable): Draft.pdf");
    }

    [Fact]
    public void BackstageViewTextResources_ExposeRailAndPaneText()
    {
        BackstageViewTextResources.WindowTitle.Should().Be("FreeW \u2014 File");
        BackstageViewTextResources.BackButton.Should().Be("\u2190 Back");
        BackstageViewTextResources.RailEntries.Select(entry => entry.Label)
            .Should().Equal("Home", "Open", "Save As", "Print", "Share", "Export", "Info", "Account");
        BackstageViewTextResources.Home.Description
            .Should().Be("Start with a new document or reopen a recent file.");
        BackstageViewTextResources.DirectPrintDeferredNote
            .Should().Contain("Direct printer output is planned");
    }
}
