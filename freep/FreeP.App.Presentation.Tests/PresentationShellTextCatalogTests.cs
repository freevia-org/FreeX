using System.Globalization;
using FreeP.Core.Model;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationShellTextCatalogTests
{
    [Fact]
    public void Catalog_preserves_existing_neutral_status_and_dialog_copy()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.SlideSizeDialogStatus)
                .Should().Be("Slide Size");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.HeaderFooterDialogStatus)
                .Should().Be("Header and Footer");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.SlideShowSettingsDialogStatus)
                .Should().Be("Set Up Slide Show");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PictureBulletAppliedStatus)
                .Should().Be("Picture bullet applied.");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PictureBulletCommandName)
                .Should().Be("Picture Bullet");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCopyCommand)
                .Should().Be("Copy");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCutCommand)
                .Should().Be("Cut");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditPasteCommand)
                .Should().Be("Paste");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditSelectAllCommand)
                .Should().Be("Select All");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.LayoutPickerStatus(18))
                .Should().Be("Layout picker: 18 choices");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.TablePickerStatus(80))
                .Should().Be("Table picker: 80 choices");
            PresentationShellTextCatalog.Resolve(
                    PresentationShellTextCatalog.SmartArtPictureFailureStatus("read failed"))
                .Should().Be("Could not replace SmartArt picture: read failed");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PresentationCommandUnavailableStatus)
                .Should().Be("The presentation command is unavailable.");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PrintCustomRangeApplyHelp)
                .Should().Be("Apply the custom slide range to the print preview and output.");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PrinterSelectedStatus("Office Printer"))
                .Should().Be("Printer selected: Office Printer");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.ExportCompletedStatus)
                .Should().Be("Export completed");
            PresentationShellTextCatalog.Resolve(
                    PresentationShellTextCatalog.VideoExportCompletedWithTracksStatus(2, 1, 3))
                .Should().Be(
                    "Video export completed with 2 narration track(s), 1 camera track(s), and 3 caption track(s)");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.WpfWindowsVideoExportHostName)
                .Should().Be("WPF Windows video export host");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.AvaloniaLinuxVideoExportHostName)
                .Should().Be("Avalonia Linux video export host");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void File_error_summaries_and_edit_commands_follow_the_ui_culture()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            PresentationFileTextResources.ErrorSummary(PresentationFileCommand.Open)
                .Should().Be("Impossible d'ouvrir la présentation");
            PresentationFileTextResources.ErrorSummary(PresentationFileCommand.SaveAs)
                .Should().Be("Impossible d'enregistrer la présentation");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCopyCommand)
                .Should().Be("Copier");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditSelectAllCommand)
                .Should().Be("Tout sélectionner");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void Print_and_video_plans_carry_typed_status_and_native_surface_metadata()
    {
        var presentation = PresentationModel.CreateEmpty();
        var handout = PresentationExportPlanner.BuildHandoutLayoutPlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts),
            presentation);
        var notes = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);
        var video = PresentationExportPlanner.BuildVideoExportPlan(
            null,
            presentation,
            PresentationVideoExportHandoffHostCapabilities.Deferred("test", "deferred"));
        var print = PresentationPrintBackstagePlanner.Build(
            null,
            presentation,
            hostCapabilities: PresentationNativePrintHandoffHostCapabilities.Available("test"));

        handout.StatusText.Should().Be(PresentationShellTextCatalog.PrintHandoutLayoutPlannedStatus);
        notes.StatusText.Should().Be(PresentationShellTextCatalog.NotesPagePdfPlannedStatus);
        video.PlannedStatusText.Should().Be(PresentationShellTextCatalog.VideoExportPlannedStatus);
        print.NativePrintHandoff.Surface.Should().Be(PresentationPrintOutputPackageExecutor.NativePrintSurface);
        print.NativePrintHandoff.Surface.PrinterPickerAutomationId.Should().Be("FreePWindowsPrinterPicker");
        print.NativePrintHandoff.Surface.NativeDialogAutomationId.Should().Be("FreePWindowsPrinterDialog");
    }

    [Fact]
    public void Renderer_sources_do_not_own_the_extracted_copy_or_native_print_ids()
    {
        var avalonia = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var avaloniaOptions = Read("freep", "FreeP.App.Avalonia", "OptionsDialog.cs");
        var avaloniaPorts = Read("freep", "FreeP.App.Avalonia", "MainWindow.FileCommandPorts.cs");
        var wpf = Read("freep", "FreeP.App.Host", "MainWindow.cs");
        var wpfOptions = Read("freep", "FreeP.App.Host", "OptionsDialog.cs");
        var wpfBackstage = Read("freep", "FreeP.App.Host", "Backstage", "BackstageView.cs");
        var wpfFileCommands = Read(
            "freep", "FreeP.App.Host", "WpfPresentationFileCommandPorts.cs");
        var nativePrint = Read(
            "freep", "FreeP.App.Presentation", "PresentationPrintOutputPackageExecutor.cs");
        var fileSession = Read(
            "freep", "FreeP.App.Presentation", "PresentationFileCommandSession.cs");
        var avaloniaRichEditor = Read(
            "freep", "FreeP.App.Rendering.Avalonia", "AvaloniaRichTextEditor.cs");
        var reviewPaneCoordinator = Read(
            "freep", "FreeP.App.Presentation", "PresentationMainWindowReviewPaneCoordinator.cs");

        avalonia.Should().NotContain("\"Slide Size\"")
            .And.NotContain("\"Header and Footer\"")
            .And.NotContain("\"Set Up Slide Show\"")
            .And.NotContain("\"Picture bullet applied.\"")
            .And.NotContain("\"Picture Bullet\"")
            .And.NotContain("\"Print handout layout planned\"")
            .And.NotContain("\"Notes page PDF planned\"")
            .And.NotContain("\"Video export planned\"")
            .And.NotContain("\"Windows printer dialog\"")
            .And.NotContain("\"FreePWindowsPrinterPicker\"")
            .And.NotContain("\"FreePWindowsPrinterDialog\"")
            .And.NotContain("Layout picker: ")
            .And.NotContain("Table picker: ")
            .And.NotContain("Content = \"Save\"")
            .And.NotContain("Content = \"Select\"")
            .And.Contain("Content  = plan.CloseAction.Label")
            .And.Contain("Content = editAction.Label")
            .And.Contain("PresentationMainWindowReviewPaneCoordinator.BuildProofingRowActions(row)")
            .And.Contain("Content = action.Label")
            .And.Contain("PresentationShellTextCatalog.LayoutPickerStatus(")
            .And.Contain("PresentationShellTextCatalog.TablePickerStatus(")
            .And.Contain("surface.PrinterPickerAutomationId")
            .And.Contain("surface.NativeDialogAutomationId");
        wpf.Should().NotContain("Content = \"Save\"")
            .And.NotContain("Content = \"Select\"")
            .And.NotContain("Could not replace SmartArt picture:")
            .And.Contain("Content = plan.CloseAction.Label")
            .And.Contain("Content = editAction.Label")
            .And.Contain("PresentationMainWindowReviewPaneCoordinator.BuildProofingRowActions(row)")
            .And.Contain("Content = action.Label");
        avaloniaOptions.Should().NotContain("Content = \"OK\"")
            .And.NotContain("Content = \"Cancel\"")
            .And.Contain("_surface.AcceptLabel")
            .And.Contain("_surface.CancelLabel");
        wpfOptions.Should().Contain("acceptContent: _surface.AcceptLabel")
            .And.Contain("cancelContent: _surface.CancelLabel");
        avaloniaPorts.Should().NotContain("\"The presentation command is unavailable.\"");
        wpfBackstage.Should().NotContain(
            "\"Apply the custom slide range to the print preview and output.\"");
        wpfFileCommands.Should().NotContain("\"Could not complete the presentation command\"");
        wpfFileCommands.Should().NotContain("\"Export completed\"")
            .And.NotContain("BuildWpfStatusText")
            .And.NotContain("WPF Windows video export host")
            .And.Contain("BuildVideoExportHostCapabilities(")
            .And.Contain("BuildVideoExportCommandResult(");
        avaloniaPorts.Should().NotContain("\"Export completed\"")
            .And.Contain("PresentationNativeCommandOutcomePlanner.ExportCompletedStatus")
            .And.Contain("BuildVideoExportCommandResult(");
        fileSession.Should().Contain("PresentationFileTextResources.ErrorSummary(")
            .And.NotContain("\"Could not open the presentation\"")
            .And.NotContain("\"Could not save the presentation\"")
            .And.NotContain("\"Could not export the presentation to PDF\"")
            .And.NotContain("\"Could not export the presentation video\"");
        avaloniaRichEditor.Should().Contain("PresentationShellTextCatalog.EditCopyCommand")
            .And.Contain("PresentationShellTextCatalog.EditCutCommand")
            .And.Contain("PresentationShellTextCatalog.EditPasteCommand")
            .And.Contain("PresentationShellTextCatalog.EditSelectAllCommand")
            .And.NotContain("Header = \"Copy\"")
            .And.NotContain("Header = \"Cut\"")
            .And.NotContain("Header = \"Paste\"")
            .And.NotContain("Header = \"Select All\"");
        nativePrint.Should().Contain("PrinterPickerAutomationId: \"FreePWindowsPrinterPicker\"")
            .And.Contain("NativeDialogAutomationId: \"FreePWindowsPrinterDialog\"");
        reviewPaneCoordinator.Should().Contain("row.SelectionAction")
            .And.NotContain("Content = row.SelectionAction.Label");
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
