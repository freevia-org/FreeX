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
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PresentationCommandUnavailableStatus)
                .Should().Be("The presentation command is unavailable.");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PrintCustomRangeApplyHelp)
                .Should().Be("Apply the custom slide range to the print preview and output.");
            PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PrinterSelectedStatus("Office Printer"))
                .Should().Be("Printer selected: Office Printer");
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
        var avaloniaPorts = Read("freep", "FreeP.App.Avalonia", "MainWindow.FileCommandPorts.cs");
        var wpfBackstage = Read("freep", "FreeP.App.Host", "Backstage", "BackstageView.cs");
        var wpfFileCommands = Read("freep", "FreeP.App.Host", "FileCommands.cs");
        var nativePrint = Read(
            "freep", "FreeP.App.Presentation", "PresentationPrintOutputPackageExecutor.cs");

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
            .And.Contain("surface.PrinterPickerAutomationId")
            .And.Contain("surface.NativeDialogAutomationId");
        avaloniaPorts.Should().NotContain("\"The presentation command is unavailable.\"");
        wpfBackstage.Should().NotContain(
            "\"Apply the custom slide range to the print preview and output.\"");
        wpfFileCommands.Should().NotContain("\"Could not complete the presentation command\"");
        nativePrint.Should().Contain("PrinterPickerAutomationId: \"FreePWindowsPrinterPicker\"")
            .And.Contain("NativeDialogAutomationId: \"FreePWindowsPrinterDialog\"");
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
