using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class FilePickerPlannerSourceTests
{
    [Fact]
    public void MainWindow_RoutesPresentationPickerPolicyThroughSharedPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var ports = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.FileCommandPorts.cs"));
        var assetImports = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.AssetImports.cs"));
        var assetPickerProfiles = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationAssetPickerProfileCatalog.cs"));
        var session = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationFileCommandSession.cs"));
        var ribbonProfile = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "Ribbon",
            "FreePRibbonHostProfile.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "FreeP.App.Avalonia.csproj"));

        // Constructed through PresentationFileCommandSessionFactory now; the guard is that the
        // shell still routes file commands through that shared session, not that it news it up.
        source.Should().Contain("PresentationFileCommandSessionFactory.Create(");
        source.Should().Contain("new PresentationFileCommandSessionComposition(");
        source.Should().Contain("_fileSession.OpenAsync()");
        source.Should().Contain("_fileSession.SaveAsync()");
        source.Should().Contain("_fileSession.ExportPdfAsync()");
        source.Should().Contain("_fileSession.ExportImagesAsync()");
        source.Should().Contain("new FreePRibbonFileCommandEndpoints")
            .And.Contain("ExportVideo = () => _ = FileExportVideoAsync()")
            .And.Contain("Print = () =>");
        ribbonProfile.Should().Contain("PresentationExportPlanner.PdfExportCommandId")
            .And.Contain("PresentationExportPlanner.NotesPagePdfExportCommandId")
            .And.Contain("PresentationExportPlanner.ImageExportCommandId")
            .And.Contain("PresentationExportPlanner.VideoExportCommandId")
            .And.Contain("PresentationExportPlanner.PrintCommandId");
        session.Should().Contain("PresentationFileDialogPlanner.BuildOpenPickerPlan()");
        session.Should().Contain("PresentationFileDialogPlanner.BuildSavePickerPlan(");
        session.Should().Contain("PresentationExportPlanner.BuildPdfExportPickerPlan(");
        session.Should().Contain("PresentationExportPlanner.BuildNotesPagePdfExportPlan(");
        session.Should().Contain("PresentationExportPlanner.BuildNotesPagePdfExportPickerPlan(");
        // IA1 regression guard: notes-page PDF export must cover the whole deck (AllSlides), the
        // same as the WPF host (PresentationFileCommandSession.ExportNotesPagePdfAsync, range: null ->
        // AllSlides) and this shell's own slides-PDF export. It must not be narrowed back down to
        // only the current slide.
        source.Should().Contain("var range = new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides);");
        session.Should().Contain("PresentationExportPlanner.BuildHandoutLayoutPlan(");
        session.Should().Contain("PresentationNotesPagePdfExporter.BuildRenderPlan(");
        session.Should().Contain("PresentationFilePdfExportExecutor.ExportNotesPages(");
        ports.Should().Contain("SkiaPdfWriter.WriteToBytesWithPortableFallback");
        session.Should().Contain("PresentationExportPlanner.BuildVideoExportPlan(");
        session.Should().Contain("PresentationVideoFramePackageExecutor.BuildPackageWithDiagnostics(");
        session.Should().Contain("PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(");
        session.Should().Contain("PresentationPrintOutputPackageExecutor.BuildPackageWithDiagnostics(");
        session.Should().Contain("PresentationPrintOutputPackageExecutor.BuildExecutionDescriptor(");
        session.Should().Contain("PresentationPrintBackstagePlanner.Build(");
        source.Should().Contain("ShowPrintBackstage");
        source.Should().Contain("RenderPrintOptionsPane(plan)");
        source.Should().Contain("PresentationBackstagePrintSurfacePlanner.Build(plan)");
        source.Should().Contain("surface.ChoiceGroups");
        session.Should().Contain("PresentationFilePdfExportExecutor.ExportRaster(");
        ports.Should().Contain("SlideRenderer.RenderToBytes");
        ports.Should().Contain("SkiaRasterPdfWriter.WriteToBytes");
        session.Should().Contain("PresentationImageExportExecutor.ExportWithDiagnostics(");
        source.Should().Contain("internal PresentationHandoutLayoutPlan RefreshHandoutLayoutPlan(");
        source.Should().Contain("internal PresentationNotesPagePdfRenderPlan RefreshNotesPagePdfRenderPlan(");
        source.Should().Contain("internal PresentationPrintOutputPackage RefreshPrintOutputPackage(");
        source.Should().Contain("internal PresentationPrintBackstagePlan RefreshPrintBackstagePlan(");
        source.Should().Contain("internal PresentationVideoExportPlan RefreshVideoExportPlan(");
        source.Should().Contain("internal PresentationVideoFramePackage RefreshVideoFramePackage(");
        source.Should().Contain("LastHandoutLayoutPlan");
        source.Should().Contain("LastNotesPagePdfRenderPlan");
        source.Should().Contain("LastPrintOutputPackage");
        source.Should().Contain("LastPrintBackstagePlan");
        source.Should().Contain("LastPrintExecutionDescriptor");
        source.Should().Contain("LastVideoExportPlan");
        source.Should().Contain("LastVideoFramePackage");
        session.Should().Contain("LastVideoExecutionDescriptor = PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(");
        source.Should().Contain("_fileSession.LastVideoExecutionDescriptor");
        source.Should().Contain("LastVideoExportHandoffPlan");
        ribbonProfile.Should().Contain("PresentationExportPlanner.PrintCommandId");
        session.Should().Contain("PresentationExportPlanner.BuildPdfExportPickerPlan(");
        session.Should().Contain("PresentationExportPlanner.BuildHandoutLayoutPlan(");
        session.Should().Contain("PresentationImageExportExecutor.ExportWithDiagnostics(");
        source.Should().Contain("internal PresentationHandoutLayoutPlan RefreshHandoutLayoutPlan(");
        source.Should().Contain("LastHandoutLayoutPlan");
        session.Should().Contain("PresentationExportPlanner.ImageExportPickerTitle");
        ports.Should().Contain("StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions");
        source.Should().Contain("PresentationExportPlanner.BuildCurrentSlideRangeRequest(Editor.CurrentSlideIndex)");
        session.Should().Contain("_atomicExportExecutor.ExecuteAsync<PresentationPdfExportArtifact>(")
            .And.Contain("await output.WriteAsync(artifact.Bytes, token)");
        session.Should().NotContain("AtomicFileWriter.WriteAllBytes(selection.Path!, artifact.Bytes)");
        session.Should().Contain("PresentationFilePersistenceWorkflow.Open(path)");
        session.Should().Contain("PresentationFilePersistenceWorkflow.Save(path, _getPresentation(), expectedLastWriteTimeUtc)");
        ports.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(");
        ports.Should().Contain("AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(");
        ports.Should().Contain("AvaloniaFilePickerOpenRequest.FromDescriptors(request.Title, request.PickerPlan.FileTypes)");
        ports.Should().Contain("AvaloniaFilePickerSaveRequest.FromSavePlan(");
        source.Should().Contain("PresentationFileTextResources.Presentation");
        assetPickerProfiles.Should().Contain("PresentationFileTextResources.PictureFileTypeName");
        assetImports.Should().Contain("AvaloniaFilePickerTypeAdapter.CreateFileType(");
        ports.Should().Contain("SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(FileText,");
        source.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(FileText,");
        (source + ports + session).Should().NotContain("SisterAppFileTextPlanner.Presentation");
        (source + ports + session).Should().NotContain("SisterAppFileTextPlanner.OpenCommand");
        (source + ports + session).Should().NotContain("SisterAppFileTextPlanner.SaveCommand");
        (source + ports + session).Should().NotContain("SisterAppFileTextPlanner.InsertPictureCommand");
        (source + ports + session).Should().NotContain("SisterAppFileTextPlanner.InsertPicturePickerTitle");
        ports.Should().NotContain("OpenFilePickerAsync(");
        ports.Should().NotContain("SaveFilePickerAsync(");
        ports.Should().NotContain("new FilePickerFileType(descriptor.DisplayName)");
        ports.Should().NotContain("Patterns = descriptor.Patterns.ToArray()");
        source.Should().Contain("startupOpenSession.Plan(startupArguments)");
        ports.Should().NotContain("PptxFileType");
        ports.Should().NotContain("new FilePickerFileType(\"Images\")");
        ports.Should().NotContain("Title         = \"Open Presentation\"");
        ports.Should().NotContain("Title             = \"Save Presentation\"");
        source.Should().NotContain("_statusText.Text = $\"Open failed:");
        source.Should().NotContain("_statusText.Text = $\"Save failed:");
        source.Should().NotContain("new FilePickerFileType(\"PowerPoint Presentation\")");
        ports.Should().NotContain("DefaultExtension  = \"pptx\"");
        ports.Should().NotContain("SuggestedFileName = suggested");
        source.Should().NotContain("\"Print comments and ink markup\"");
        source.Should().NotContain("\"Frame slides\"");
        source.Should().NotContain("\"Pure Black and White\"");
        (source + ports).Should().NotContain("FxpFormat.");
        (source + ports).Should().NotContain("PptxPackageReader.");
        (source + ports).Should().NotContain("PptxPackageWriter.");
        (source + ports).Should().NotContain("File.Create(");
        source.Should().NotContain("new PresentationHandoutSlideSlot(");
        project.Should().Contain(@"..\..\shared\Free.Shared.IO\Free.Shared.IO.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Pdf.Skia\Free.Shared.Pdf.Skia.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
    }

}
