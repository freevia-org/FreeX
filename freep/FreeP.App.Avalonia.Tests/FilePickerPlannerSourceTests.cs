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
        var project = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "FreeP.App.Avalonia.csproj"));

        source.Should().Contain("PresentationFileDialogPlanner.BuildOpenPickerPlan()");
        source.Should().Contain("PresentationFileDialogPlanner.BuildSavePickerPlan(");
        source.Should().Contain("PresentationExportPlanner.PdfExportCommandId");
        source.Should().Contain("PresentationExportPlanner.NotesPagePdfExportCommandId");
        source.Should().Contain("PresentationExportPlanner.ImageExportCommandId");
        source.Should().Contain("PresentationExportPlanner.VideoExportCommandId");
        source.Should().Contain("new ActionRibbonCommand(() => _ = FileExportVideoAsync())");
        source.Should().Contain("PresentationExportPlanner.PrintCommandId");
        source.Should().Contain("PresentationExportPlanner.BuildPdfExportPickerPlan(");
        source.Should().Contain("PresentationExportPlanner.BuildNotesPagePdfExportPlan(");
        source.Should().Contain("PresentationExportPlanner.BuildNotesPagePdfExportPickerPlan(");
        // IA1 regression guard: notes-page PDF export must cover the whole deck (AllSlides), the
        // same as the WPF host (FreeP.App.Host/FileCommands.cs ExportNotesPagePdf, range: null ->
        // AllSlides) and this shell's own slides-PDF export. It must not be narrowed back down to
        // only the current slide.
        source.Should().Contain("var range = new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides);");
        source.Should().Contain("PresentationExportPlanner.BuildHandoutLayoutPlan(");
        source.Should().Contain("PresentationNotesPagePdfExporter.BuildRenderPlan(");
        source.Should().Contain("PresentationNotesPagePdfExporter.ExportToBytes(");
        source.Should().Contain("SkiaPdfWriter.WriteToBytesWithPortableFallback");
        source.Should().Contain("PresentationExportPlanner.BuildVideoExportPlan(");
        source.Should().Contain("PresentationVideoFramePackageExecutor.BuildPackage(");
        source.Should().Contain("PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(");
        source.Should().Contain("PresentationPrintOutputPackageExecutor.BuildPackage(");
        source.Should().Contain("PresentationPrintOutputPackageExecutor.BuildExecutionDescriptor(");
        source.Should().Contain("PresentationPrintBackstagePlanner.Build(");
        source.Should().Contain("ShowPrintBackstage");
        source.Should().Contain("RenderPrintOptionsPane(plan)");
        source.Should().Contain("plan.OutputOptionChoices");
        source.Should().Contain("plan.LayoutChoices");
        source.Should().Contain("plan.RangeChoices");
        source.Should().Contain("PresentationRasterPdfExporter.ExportToBytes(");
        source.Should().Contain("SlideRenderer.RenderToBytes");
        source.Should().Contain("SkiaRasterPdfWriter.WriteToBytes");
        source.Should().Contain("PresentationImageExportExecutor.Export(");
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
        source.Should().Contain("LastVideoExecutionDescriptor.HandoffPlan");
        source.Should().Contain("LastVideoExportHandoffPlan");
        source.Should().Contain("PresentationExportPlanner.PrintCommandId");
        source.Should().Contain("PresentationExportPlanner.BuildPdfExportPickerPlan(");
        source.Should().Contain("PresentationExportPlanner.BuildHandoutLayoutPlan(");
        source.Should().Contain("PresentationImageExportExecutor.Export(");
        source.Should().Contain("internal PresentationHandoutLayoutPlan RefreshHandoutLayoutPlan(");
        source.Should().Contain("LastHandoutLayoutPlan");
        source.Should().Contain("PresentationExportPlanner.ImageExportPickerTitle");
        source.Should().Contain("StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions");
        source.Should().Contain("PresentationExportPlanner.BuildCurrentSlideRangeRequest(Editor.CurrentSlideIndex)");
        source.Should().Contain("ExportAtomicWriter.WriteAllBytes(path,");
        source.Should().Contain("PresentationFilePersistenceWorkflow.Open(path)");
        source.Should().Contain("PresentationFilePersistenceWorkflow.Save(path, _presentation)");
        source.Should().Contain("PresentationFilePersistenceWorkflow.IsSupportedPresentationPath(path)");
        source.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(");
        source.Should().Contain("AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(");
        source.Should().Contain("AvaloniaFilePickerOpenRequest.FromDescriptors(FileText.OpenPickerTitle, plan.FileTypes)");
        source.Should().MatchRegex(
            @"AvaloniaFilePickerSaveRequest\.FromSavePlan\(\s*FileText\.SavePickerTitle,\s*plan,\s*showOverwritePrompt:\s*true\s*\)");
        source.Should().Contain("AvaloniaFilePickerSaveRequest.FromSavePlan(PresentationExportPlanner.PdfExportPickerTitle, plan)");
        source.Should().Contain("AvaloniaFilePickerSaveRequest.FromSavePlan(PresentationExportPlanner.NotesPagePdfExportPickerTitle, plan)");
        source.Should().Contain("SisterAppFileTextPlanner.Presentation");
        source.Should().Contain("PresentationFileTextResources.PictureFileTypeName");
        source.Should().Contain("AvaloniaFilePickerTypeAdapter.CreateFileType(");
        source.Should().Contain("FileText.OpenPickerTitle");
        source.Should().Contain("FileText.SavePickerTitle");
        source.Should().Contain("SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(");
        source.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(");
        source.Should().NotContain("OpenFilePickerAsync(");
        source.Should().NotContain("SaveFilePickerAsync(");
        source.Should().NotContain("new FilePickerFileType(descriptor.DisplayName)");
        source.Should().NotContain("Patterns = descriptor.Patterns.ToArray()");
        source.Should().Contain("IsSupportedPresentationPath(a)");
        source.Should().NotContain("PptxFileType");
        source.Should().NotContain("new FilePickerFileType(\"Images\")");
        source.Should().NotContain("Title         = \"Open Presentation\"");
        source.Should().NotContain("Title             = \"Save Presentation\"");
        source.Should().NotContain("_statusText.Text = $\"Open failed:");
        source.Should().NotContain("_statusText.Text = $\"Save failed:");
        source.Should().NotContain("new FilePickerFileType(\"PowerPoint Presentation\")");
        source.Should().NotContain("DefaultExtension  = \"pptx\"");
        source.Should().NotContain("SuggestedFileName = suggested");
        source.Should().NotContain("PresentationPdfExporter.ExportToBytes(_presentation)");
        source.Should().NotContain("\"Print comments and ink markup\"");
        source.Should().NotContain("\"Frame slides\"");
        source.Should().NotContain("\"Pure Black and White\"");
        source.Should().NotContain("FxpFormat.");
        source.Should().NotContain("PptxPackageReader.");
        source.Should().NotContain("PptxPackageWriter.");
        source.Should().NotContain("File.Create(");
        source.Should().NotContain("new PresentationHandoutSlideSlot(");
        project.Should().Contain(@"..\..\shared\Free.Shared.IO\Free.Shared.IO.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Pdf.Skia\Free.Shared.Pdf.Skia.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
    }

}
