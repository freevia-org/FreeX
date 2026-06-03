using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void ExportOptionsDialog_ExposesOnlyHonoredPdfXpsChoices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("Content = UiText.Get(\"ExportOptions_Workbook\")");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_ActiveSheetS\")");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_SelectedRange\")");
        source.Should().Contain("UiText.Get(\"ExportOptions_PdfXpsOptions\")");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_IncludeDocumentProperties\")");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_OpenAfterPublishing\")");
        source.Should().Contain("Content = UiText.Get(\"ExportOptions_IgnorePrintAreas\")");
        source.Should().NotContain("CSV options");
        source.Should().NotContain("Content = \"CSV _delimiter:\"");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesExcelLikePreviewToolbarAffordances()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = UiText.Get(\"PrintPreview_PreviousPageButton\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_NextPageButton\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_FirstPageButton\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_LastPageButton\")");
        source.Should().Contain("NavigationCommands.FirstPage");
        source.Should().Contain("NavigationCommands.LastPage");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_ZoomLabel\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_MarginsButton\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_PageSetupButton\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_PrintButton\")");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_CloseButton\")");
        source.Should().Contain("IsCancel = true");
        source.Should().Contain("closeButton.Click += (_, _) => Close();");
    }
}
