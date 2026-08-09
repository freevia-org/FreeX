using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class FileCommandsSourceTests
{
    [Fact]
    public void FileCommands_UsesSharedPerFormatDialogPlans()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "FileCommands.cs"));
        var session = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationFileCommandSession.cs"));

        source.Should().Contain("new PresentationFileCommandSession(");
        source.Should().Contain("WpfPresentationFileLifecyclePort");
        source.Should().Contain("WpfPresentationFilePickerPort");
        source.Should().Contain("WpfPresentationFileRenderPort");
        source.Should().Contain("WpfPresentationPrintPort");
        source.Should().Contain("WpfPresentationVideoPort");
        source.Should().Contain("WpfPresentationFileFeedbackPort");
        source.Should().Contain("WpfPresentationSlideImageRenderer.RenderSlideToPng");
        source.Should().Contain("WpfRasterPdfWriter.WriteToBytes");
        source.Should().Contain("WpfPresentationPrintService.ShowPrintDialogAndPrint(");
        source.Should().Contain("WpfVideoExportAdapter");
        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        source.Should().Contain("new OpenFolderDialog");
        source.Should().Contain("SisterWpfFileCommandWorkflow");
        source.Should().Contain("public PresentationPrintOutputPackage BuildPrintOutputPackage(");
        source.Should().Contain("public PresentationPrintBackstagePlan BuildPrintBackstagePlan(");
        source.Should().Contain("public PresentationVideoFramePackage BuildVideoFramePackage(");
        source.Should().Contain("public PresentationVideoExportHandoffPlan BuildVideoExportHandoffPlan(");
        source.Should().Contain("public async Task<bool> ExportVideoAsync(");
        source.Should().Contain("public bool ExportNotesPagePdf(");
        source.Should().Contain("public bool ExportImages()");
        source.Should().Contain("IUserMessageService? messageService = null");
        source.Should().NotContain("PresentationFilePersistenceWorkflow.");
        source.Should().NotContain("PresentationFileDialogPlanner.");
        source.Should().NotContain("PresentationExportPlanner.Build");
        source.Should().NotContain("PresentationRasterPdfExporter.");
        source.Should().NotContain("PresentationImageExportExecutor.");
        source.Should().NotContain("PresentationPrintOutputPackageExecutor.BuildPackage(");
        source.Should().NotContain("PresentationVideoFramePackageExecutor.BuildPackage(");

        session.Should().Contain("PresentationFilePersistenceWorkflow.Open(path)");
        session.Should().Contain("PresentationFilePersistenceWorkflow.Save(path, _getPresentation())");
        session.Should().Contain("PresentationFileDialogPlanner.BuildOpenDialogPlan()");
        session.Should().Contain("PresentationExportPlanner.BuildPdfExportDialogPlan(");
        session.Should().Contain("PresentationRasterPdfExporter.ExportToBytes(");
        session.Should().Contain("PresentationImageExportExecutor.Export(");
        session.Should().Contain("PresentationPrintOutputPackageExecutor.BuildPackage(");
        session.Should().Contain("PresentationVideoFramePackageExecutor.BuildPackage(");
        session.Should().Contain("PresentationFileTextResources.Presentation");
        source.Should().NotContain("new FileDialogFormatDescriptor");
        source.Should().NotContain("FileDialogRequestPlanner.");
        source.Should().NotContain("FileDialogFilterBuilder.BuildPerFormatFilter(Formats)");
        source.Should().NotContain("FileDialogFilterBuilder.GetDefaultExtension(Formats)");
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
        var assetImports = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.AssetImports.cs"));

        assetImports.Should().Contain("PresentationMediaFileTypeCatalog.BuildWpfAudioFilter()");
        (source + assetImports).Should().NotContain("*.mp3;*.m4a;*.wav;*.wma;*.aac;*.ogg;*.flac");
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
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));
        var endpoint = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.WorkareaEndpoint.cs"));

        source.Should().Contain("InstallSharedKeyboardShortcuts();");
        source.Should().Contain("New: () => _file.New(),");
        source.Should().Contain("Open: () => _file.Open(),");
        source.Should().Contain("Save: () => _file.Save(),");
        source.Should().Contain("SaveAs: () => _file.SaveAs(),");
        source.Should().Contain("New: () => _file.New()");
        source.Should().Contain("Open: () => _file.Open()");
        source.Should().Contain("Save: () => _file.Save()");
        source.Should().Contain("SaveAs: () => _file.SaveAs()");
        source.Should().Contain(
            "_workareaSession = new PresentationWorkareaSession(CreateWorkareaEndpoint());");
        source.Should().Contain("_workareaSession.ExecuteCommand(FreePKeyboardCommand.Undo)");
        source.Should().Contain("_workareaSession.ExecuteCommand(FreePKeyboardCommand.Redo)");
        source.Should().Contain("(_, _) => _workareaSession.ExecuteCommand(command)");
        endpoint.Should().Contain("NewPresentation = () => _file.New()");
        endpoint.Should().Contain("SavePresentation = () => _file.Save()");
        source.Should().NotContain("private void ExecuteKeyboardCommand(");
        source.Should().NotContain("case FreePKeyboardCommand.");
        source.Should().Contain("foreach (var shortcut in FreePKeyboardShortcutCatalog.All)");
        source.Should().NotContain("ApplicationCommands.New");
        source.Should().NotContain("ApplicationCommands.Open");
    }

}
