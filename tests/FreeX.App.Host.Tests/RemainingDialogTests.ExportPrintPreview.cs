using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void ExportOptionsDialog_ExposesOnlyHonoredPdfXpsChoices()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ExportOptionsDialog.cs");

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

        source.Should().Contain("\"PrintPreview_PreviousPageButton\"");
        source.Should().Contain("\"PrintPreview_NextPageButton\"");
        source.Should().Contain("\"PrintPreview_FirstPageButton\"");
        source.Should().Contain("\"PrintPreview_LastPageButton\"");
        source.Should().Contain("PrintPreviewToolbarCommand.PreviousPage");
        source.Should().Contain("PrintPreviewToolbarCommand.NextPage");
        source.Should().Contain("PrintPreviewToolbarCommand.FirstPage");
        source.Should().Contain("PrintPreviewToolbarCommand.LastPage");
        source.Should().Contain("NavigationCommands.FirstPage");
        source.Should().Contain("NavigationCommands.LastPage");
        source.Should().Contain("Content = UiText.Get(\"PrintPreview_ZoomLabel\")");
        source.Should().Contain("\"PrintPreview_MarginsButton\"");
        source.Should().Contain("\"PrintPreview_PageSetupButton\"");
        source.Should().Contain("\"PrintPreview_PrintButton\"");
        source.Should().Contain("\"PrintPreview_CloseButton\"");
        source.Should().Contain("isCancel: true");
        source.Should().Contain("closeButton.Click += (_, _) => Close();");
        source.Should().Contain("PrintPreviewSurfacePlanner.SettingsRailWidth");
        source.Should().Contain("PrintPreviewSurfacePlanner.PrinterComboWidth");
        source.Should().Contain("PrintPreviewSurfacePlanner.ToolbarCopiesBoxWidth");
        source.Should().Contain("PrintPreviewSurfacePlanner.ToolbarSidesComboWidth");
    }
}
