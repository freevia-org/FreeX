using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class FileCommandsSourceTests
{
    [Fact]
    public void FileCommands_UsesSharedPerFormatDialogPlans()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "FileCommands.cs"));

        source.Should().Contain("PresentationFileDialogPlanner.BuildOpenDialogPlan()");
        source.Should().Contain("PresentationFileDialogPlanner.BuildSaveAsDialogPlan(");
        source.Should().Contain("PresentationExportPlanner.BuildPdfExportDialogPlan(");
        source.Should().Contain("PresentationRasterPdfExporter.ExportToBytes(");
        source.Should().Contain("WpfPresentationSlideImageRenderer.RenderSlideToPng");
        source.Should().Contain("WpfRasterPdfWriter.WriteToBytes");
        source.Should().Contain("PresentationExportPlanner.BuildNotesPagePdfExportPlan(");
        source.Should().Contain("PresentationExportPlanner.BuildNotesPagePdfExportDialogPlan(");
        source.Should().Contain("PresentationExportPlanner.BuildHandoutLayoutPlan(");
        source.Should().Contain("PresentationNotesPagePdfExporter.BuildRenderPlan(");
        source.Should().Contain("PresentationNotesPagePdfExporter.ExportToBytes(");
        source.Should().Contain("PresentationPrintOutputPackageExecutor.BuildPackage(");
        source.Should().Contain("PresentationPrintOutputPackageExecutor.BuildExecutionDescriptor(");
        source.Should().Contain("PresentationPrintBackstagePlanner.Build(");
        source.Should().Contain("public PresentationPrintOutputPackage BuildPrintOutputPackage(");
        source.Should().Contain("public PresentationPrintBackstagePlan BuildPrintBackstagePlan(");
        source.Should().Contain("LastPrintOutputPackage");
        source.Should().Contain("LastPrintBackstagePlan");
        source.Should().Contain("LastPrintExecutionDescriptor");
        source.Should().Contain("PresentationVideoFramePackageExecutor.BuildPackage(");
        source.Should().Contain("PresentationVideoFramePackageExecutor.BuildHandoffPlan(");
        source.Should().Contain("public PresentationVideoFramePackage BuildVideoFramePackage(");
        source.Should().Contain("public PresentationVideoExportHandoffPlan BuildVideoExportHandoffPlan(");
        source.Should().Contain("public async Task<bool> ExportVideoAsync(");
        source.Should().Contain("WpfVideoExportAdapter");
        source.Should().Contain("LastVideoFramePackage");
        source.Should().Contain("LastVideoExportHandoffPlan");
        source.Should().Contain("PresentationExportPlanner.BuildHandoutLayoutPlan(");
        source.Should().Contain("PresentationExportPlanner.ImageExportPickerTitle");
        source.Should().Contain("PresentationImageExportExecutor.Export(");
        source.Should().Contain("public bool ExportNotesPagePdf(");
        source.Should().Contain("public bool ExportImages()");
        source.Should().Contain("public PresentationHandoutLayoutPlan BuildHandoutLayoutPlan(");
        source.Should().Contain("public PresentationNotesPagePdfRenderPlan BuildNotesPagePdfRenderPlan(");
        source.Should().Contain("_getImageExportRange()");
        source.Should().Contain("new OpenFolderDialog");
        source.Should().Contain("PresentationFilePersistenceWorkflow.Open(path)");
        source.Should().Contain("PresentationFilePersistenceWorkflow.Save(path, _getModel())");
        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        source.Should().Contain("IUserMessageService? messageService = null");
        source.Should().Contain("SisterWpfFileCommandWorkflow");
        source.Should().Contain("messageService);");
        source.Should().Contain("_workflow.ShowError(summary, ex");
        source.Should().NotContain("new FileDialogFormatDescriptor");
        source.Should().NotContain("FileDialogRequestPlanner.");
        source.Should().NotContain("FileDialogFilterBuilder.BuildPerFormatFilter(Formats)");
        source.Should().NotContain("FileDialogFilterBuilder.GetDefaultExtension(Formats)");
        source.Should().NotContain("PresentationPdfExporter.ExportToBytes(_getModel())");
        source.Should().NotContain("FxpFormat.");
        source.Should().NotContain("PptxPackageReader.");
        source.Should().NotContain("PptxPackageWriter.");
        source.Should().NotContain("SerializePresentation(");
        source.Should().NotContain("FileCommandMessageBox.PromptSaveChanges(");
        source.Should().NotContain("FileCommandMessageBox.ShowError(");
        source.Should().NotContain("PromptSaveChanges(DisplayName, action");
        source.Should().NotContain("ShowFileCommandError(summary, ex");
        source.Should().NotContain("new PresentationHandoutSlideSlot(");
        source.Should().NotContain("UserMessageButtons.YesNoCancel");
        source.Should().NotContain("UserMessageButtons.Ok");
        source.Should().NotContain("new OpenFileDialog");
        source.Should().NotContain("new SaveFileDialog");
    }

    [Fact]
    public void MainWindow_ExecutesVideoExportInsteadOfOnlyRefreshingTheFramePackage()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("ExportVideo: () => _ = _file.ExportVideoAsync(),");
    }

    [Fact]
    public void MainWindow_UsesSharedTransitionSoundAudioFilter()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("PresentationMediaFileTypeCatalog.BuildWpfAudioFilter()");
        source.Should().NotContain("*.mp3;*.m4a;*.wav;*.wma;*.aac;*.ogg;*.flac");
    }

    [Fact]
    public void WpfImageExportAdapter_OnlySuppliesSlideCanvasRenderCallback()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "WpfPresentationSlideImageRenderer.cs"));

        source.Should().Contain("new SlideCanvas");
        source.Should().Contain("RenderTargetBitmap");
        source.Should().Contain("PngBitmapEncoder");
        source.Should().NotContain("BuildSlideRangePlan(");
        source.Should().NotContain("PresentationExportPlanner.");
        source.Should().NotContain("File.WriteAllBytes(");
    }

    [Fact]
    public void WpfMainWindow_RoutesPlatformOnlyCommandResidualsThroughShellCommands()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("InstallSharedKeyboardShortcuts();");
        source.Should().Contain("New: () => _file.New(),");
        source.Should().Contain("Open: () => _file.Open(),");
        source.Should().Contain("Save: () => _file.Save(),");
        source.Should().Contain("SaveAs: () => _file.SaveAs(),");
        source.Should().Contain("New: () => _file.New()");
        source.Should().Contain("Open: () => _file.Open()");
        source.Should().Contain("Save: () => _file.Save()");
        source.Should().Contain("SaveAs: () => _file.SaveAs()");
        source.Should().Contain("new PresentationApplicationCommandCallbacks(");
        source.Should().Contain("Undo: () => Editor.Undo(),");
        source.Should().Contain("Redo: () => Editor.Redo(),");
        source.Should().Contain("(_, _) => _applicationFrameSession!.ExecuteCommand(command)");
        source.Should().NotContain("private void ExecuteKeyboardCommand(");
        source.Should().NotContain("case FreePKeyboardCommand.");
        source.Should().Contain("foreach (var shortcut in FreePKeyboardShortcutCatalog.All)");
        source.Should().NotContain("ApplicationCommands.New");
        source.Should().NotContain("ApplicationCommands.Open");
    }

}
