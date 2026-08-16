using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class WpfPresentationFileCommandPortsSourceTests
{
    [Fact]
    public void WpfHost_OwnsPortableSessionDirectlyAndKeepsOnlyNativePorts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "WpfPresentationFileCommandPorts.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));
        var session = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationFileCommandSession.cs"));

        source.Should().Contain("internal static class WpfPresentationFileCommandSessionFactory");
        source.Should().Contain("public static PresentationFileCommandSession Create(");
        source.Should().Contain("new PresentationFileCommandSession(");
        source.Should().Contain("new PresentationFileLifecycleAdapter(workflow.Workflow)");
        source.Should().Contain("WpfPresentationFilePickerPort");
        source.Should().Contain("WpfPresentationFileRenderPort");
        source.Should().Contain("WpfPresentationPrintPort");
        source.Should().Contain("WpfPresentationVideoPort");
        source.Should().Contain("WpfPresentationFileFeedbackPort");
        source.Should().Contain("WpfPresentationSlideImageRenderer.RenderSlideToPng");
        source.Should().Contain("WpfRasterPdfWriter.WriteToBytes");
        source.Should().Contain("WpfPresentationPrintService.ShowPrintDialogAndPrint(");
        source.Should().Contain("WindowsNativePrintOutput.CreateVideoAdapter(resolvedVideoCapability)");
        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        source.Should().Contain("new OpenFolderDialog");
        source.Should().Contain("SisterWpfFileCommandWorkflow");
        source.Should().Contain("IUserMessageService? messageService = null");
        source.Should().NotContain("class FileCommands");
        source.Should().NotContain("private readonly PresentationFileCommandSession");
        mainWindow.Should().Contain("private PresentationFileCommandSession _fileSession")
            .And.Contain("WpfPresentationFileCommandSessionFactory.Create(")
            .And.Contain("_fileSession.ConfirmCloseAllowedAsync().GetAwaiter().GetResult()")
            .And.NotContain("private FileCommands");
        source.Should().NotContain("PresentationFilePersistenceWorkflow.");
        source.Should().NotContain("PresentationFileDialogPlanner.");
        source.Should().NotContain("PresentationExportPlanner.Build");
        source.Should().NotContain("PresentationRasterPdfExporter.");
        source.Should().NotContain("PresentationImageExportExecutor.");
        source.Should().NotContain("PresentationPrintOutputPackageExecutor.BuildPackage(");
        source.Should().NotContain("PresentationVideoFramePackageExecutor.BuildPackage(");

        session.Should().Contain("PresentationFilePersistenceWorkflow.Open(path)");
        session.Should().Contain("PresentationFilePersistenceWorkflow.Save(path, _getPresentation(), expectedLastWriteTimeUtc)");
        session.Should().Contain("PresentationFileDialogPlanner.BuildOpenDialogPlan()");
        session.Should().Contain("PresentationExportPlanner.BuildPdfExportDialogPlan(");
        session.Should().Contain("PresentationFilePdfExportExecutor.ExportRaster(");
        session.Should().Contain("PresentationFilePdfExportExecutor.ExportNotesPages(");
        session.Should().Contain("PresentationImageExportExecutor.ExportWithDiagnostics(");
        session.Should().Contain("PresentationPrintOutputPackageExecutor.BuildPackageWithDiagnostics(");
        session.Should().Contain("PresentationVideoFramePackageExecutor.BuildPackageWithDiagnostics(");
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
        source.Should().NotContain("WpfPresentationFileLifecyclePort");
    }

    [Fact]
    public void MainWindow_ExecutesVideoExportInsteadOfOnlyRefreshingTheFramePackage()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("ExportVideo: () => _ = _fileSession.ExportVideoAsync(),");
    }

    [Fact]
    public void WpfComposition_SelectsRecordingOwnedVideoBackendsDirectly()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "WpfPresentationFileCommandPorts.cs"));

        source.Should().Contain("WindowsNativePrintOutput.CreateVideoAdapter(resolvedVideoCapability)")
            .And.Contain("new LinuxNativeOutputCapabilityDetector(")
            .And.NotContain("WpfVideoExportAdapter")
            .And.NotContain("WpfVideoEncoderCapabilityDetector");
        File.Exists(Path.Combine(root, "freep", "FreeP.App.Host", "WpfVideoExportAdapter.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public void MainWindow_UsesSharedTransitionSoundAudioFilter()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));
        var assetImports = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.AssetImports.cs"));
        var catalog = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationAssetPickerProfileCatalog.cs"));

        assetImports.Should().Contain("pickerProfile.Wpf.BuildWpfFilter()");
        catalog.Should().Contain("PresentationMediaFileTypeCatalog.AudioFilePatterns");
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
        source.Should().Contain("New: () => FileNew(),");
        source.Should().Contain("Open: () => FileOpen(),");
        source.Should().Contain("Save: () => FileSave(),");
        source.Should().Contain("SaveAs: () => FileSaveAs(),");
        source.Should().Contain("RunFileCommand(_fileSession.NewAsync())");
        source.Should().Contain("RunFileCommand(_fileSession.SaveAsync())");
        source.Should().Contain(
            "_workareaSession = new PresentationWorkareaSession(CreateWorkareaEndpoint());");
        source.Should().Contain("_workareaSession.ExecuteCommand(FreePKeyboardCommand.Undo)");
        source.Should().Contain("_workareaSession.ExecuteCommand(FreePKeyboardCommand.Redo)");
        source.Should().Contain("(_, _) => _workareaSession.ExecuteCommand(command)");
        endpoint.Should().Contain("NewPresentation = () => FileNew()");
        endpoint.Should().Contain("SavePresentation = () => FileSave()");
        source.Should().NotContain("private void ExecuteKeyboardCommand(");
        source.Should().NotContain("case FreePKeyboardCommand.");
        source.Should().Contain("foreach (var shortcut in FreePKeyboardShortcutCatalog.All)");
        source.Should().NotContain("ApplicationCommands.New");
        source.Should().NotContain("ApplicationCommands.Open");
    }

}
