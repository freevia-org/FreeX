using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Recording;
using FreeP.Core.IO;

namespace FreeP.App.Host.Tests;

public sealed class FileLifecycleTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.FileLifecycleTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private (
        Window Window,
        FileCommands File,
        Func<Presentation> GetModel,
        Func<int> ChangeCount,
        RecordingUserMessageService Messages) CreateHarness(bool canEncodeVideo = false)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var model = Presentation.CreateEmpty();
        var changes = 0;
        var recentStorePath = Path.Combine(_tempDir, "recent.json");
        var messages = new RecordingUserMessageService();
        var file = new FileCommands(
            window,
            () => model,
            loaded => model = loaded,
            () => changes++,
            loadRecentFilesStore: () => RecentFilesStore.Load(recentStorePath),
            messageService: messages,
            videoEncoderCapability: canEncodeVideo
                ? new LinuxVideoEncoderCapability(true, "ffmpeg.exe", "libx264", false, "test encoder ready")
                : LinuxVideoEncoderCapability.Unavailable("Test encoder handoff deferred."),
            nativePrintCapability: WpfNativePrintCapability.Unavailable("Test printer handoff deferred."));
        return (window, file, () => model, () => changes, messages);
    }

    [StaFact]
    public void FreshPresentation_IsCleanWithUntitledName()
    {
        var (_, file, _, _, _) = CreateHarness();

        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().BeNull();
        file.DisplayName.Should().Be("Untitled");
    }

    [StaFact]
    public void MarkDirty_SetsDirtyAndNotifiesOnce()
    {
        var (_, file, _, changeCount, _) = CreateHarness();

        file.MarkDirty();
        file.IsDirty.Should().BeTrue();
        changeCount().Should().Be(1);

        file.MarkDirty();
        changeCount().Should().Be(1);
    }

    [StaFact]
    public void New_OnCleanPresentation_ProceedsWithoutPromptAndResetsState()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        var path = WritePptx("Deck.pptx", "Opened");
        file.OpenPath(path).Should().BeTrue();

        var proceeded = file.New();

        proceeded.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().BeNull();
        file.DisplayName.Should().Be("Untitled");
        getModel().Slides.Should().HaveCount(1);
    }

    [StaFact]
    public void New_OnDirtyPresentation_UsesInjectedMessageServiceForSavePrompt()
    {
        var (_, file, _, _, messages) = CreateHarness();
        messages.NextResult = UserMessageResult.No;

        file.MarkDirty();
        var proceeded = file.New();

        proceeded.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        messages.Messages.Should().ContainSingle();
        var prompt = messages.Messages[0];
        prompt.Message.Should().Be(
            "Do you want to save changes to Untitled before creating a new presentation?");
        prompt.Title.Should().Be("FreeP");
        prompt.Buttons.Should().Be(UserMessageButtons.YesNoCancel);
        prompt.Icon.Should().Be(UserMessageIcon.Warning);
    }

    [StaFact]
    public void DialogPlans_DefaultToPptxAndKeepLegacyFxpFilters()
    {
        var openPlan = PresentationFileDialogPlanner.BuildOpenDialogPlan();
        openPlan.Filter.Should().Be(
            "PowerPoint presentations (*.pptx)|*.pptx|PowerPoint macro-enabled presentations (*.pptm)|*.pptm|PowerPoint templates (*.potx)|*.potx|PowerPoint macro-enabled templates (*.potm)|*.potm|PowerPoint slide shows (*.ppsx)|*.ppsx|PowerPoint macro-enabled slide shows (*.ppsm)|*.ppsm|FreeP legacy presentations (*.fxp)|*.fxp|All files (*.*)|*.*");
        openPlan.DefaultExtensionWithDot.Should().Be(".pptx");

        var savePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan(null);
        savePlan.SuggestedFileName.Should().Be("Presentation.pptx");
        savePlan.DefaultExtensionWithDot.Should().Be(".pptx");
        savePlan.DefaultExtensionWithoutDot.Should().Be("pptx");
        savePlan.FilterIndex.Should().Be(1);
        savePlan.Filter.Should().Be(openPlan.Filter);

        var legacySourcePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan("Legacy.fxp");
        legacySourcePlan.SuggestedFileName.Should().Be("Legacy.pptx");
        legacySourcePlan.FilterIndex.Should().Be(1);
    }

    [StaFact]
    public void BuildHandoutLayoutPlan_UsesSharedPlannerForWpfAdapter()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        getModel().Slides.Add(new Slide { Title = "Two" });
        getModel().Slides.Add(new Slide { Title = "Three" });
        getModel().Slides.Add(new Slide { Title = "Four" });

        var plan = file.BuildHandoutLayoutPlan(
            slidesPerPage: 3,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CustomRange,
                StartSlideNumber: 2,
                EndSlideNumber: 4));

        plan.PrintPlan.CommandId.Should().Be(PresentationExportPlanner.PrintCommandId);
        plan.PrintPlan.Layout.SlidesPerPage.Should().Be(3);
        plan.Pages.Should().ContainSingle();
        plan.Pages[0].Slots.Select(slot => slot.SlideNumber).Should().Equal(2, 3, 4);
        plan.Pages[0].Slots.Should().OnlyContain(slot => slot.NotesOrLinesBounds != null);
        plan.Pages[0].Slots.Should().OnlyContain(slot => slot.BlankLineBounds.Count == 5);
    }

    [StaFact]
    public void BuildNotesPagePdfRenderPlan_UsesSharedExporterForWpfAdapter()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        getModel().Slides[0].Title = "Opening";
        getModel().Slides[0].Notes = MakeTextBody("Welcome speaker note.");
        getModel().Slides.Add(new Slide { Title = "Appendix" });

        var plan = file.BuildNotesPagePdfRenderPlan(new PresentationSlideRangeRequest(
            PresentationSlideRangeKind.CurrentSlide,
            CurrentSlideNumber: 1));

        plan.PrintPlan.CommandId.Should().Be(PresentationExportPlanner.PrintCommandId);
        plan.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1);
        plan.PreviewPlans.Should().ContainSingle(preview =>
            preview.SlideNumber == 1 &&
            preview.NoteLines.Count == 1 &&
            preview.NoteLines[0] == "Welcome speaker note.");
        plan.Pages.Should().ContainSingle();
        plan.Pages[0].Ops.OfType<Free.Shared.Pdf.PdfText>().Select(text => text.Text)
            .Should()
            .Contain(["Opening", "Welcome speaker note."])
            .And
            .NotContain("Appendix");
    }

    [StaFact]
    public void BuildPrintOutputPackage_UsesSharedExecutorForWpfAdapter()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        getModel().Slides[0].Title = "Opening";
        getModel().Slides[0].Notes = MakeTextBody("Welcome speaker note.");
        getModel().Slides.Add(new Slide { Title = "Appendix" });

        var package = file.BuildPrintOutputPackage(new PresentationPrintRequest(
            PresentationPrintLayoutKind.NotesPages,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CurrentSlide,
                CurrentSlideNumber: 1)));

        file.LastPrintOutputPackage.Should().BeSameAs(package);
        package.Plan.Route.Should().Be(PresentationPrintOutputPackageRoute.NotesPagePdf);
        package.Plan.PrintPlan.CommandId.Should().Be(PresentationExportPlanner.PrintCommandId);
        package.Plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1);
        package.Plan.PreviewPlan.Pages.Should().ContainSingle()
            .Which.Should().Match<PresentationPrintPreviewPage>(page =>
                page.PageIndex == 0 &&
                page.PageNumber == 1 &&
                page.Kind == PresentationPrintPreviewPageKind.NotesPage &&
                page.SlideNumbers.SequenceEqual(new[] { 1 }) &&
                page.ThumbnailLabel == "Slide 1 notes" &&
                page.Detail == "Notes page for slide 1");
        package.Plan.NativePrinterDialogDeferred.Should().BeFalse();
        file.LastNativePrintHandoffPlan.Should().NotBeNull();
        file.LastNativePrintHandoffPlan!.Status.Should().Be(PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost);
        file.LastNativePrintHandoffPlan.IsPackageReady.Should().BeTrue();
        file.LastNativePrintHandoffPlan.RequiresHostHandoff.Should().BeTrue();
        file.LastNativePrintHandoffPlan.CanOpenNativePrintDialog.Should().BeFalse();
        file.LastNativePrintHandoffPlan.Route.Should().Be(PresentationPrintOutputPackageRoute.NotesPagePdf);
        file.LastNativePrintHandoffPlan.SuggestedTempFileName.Should().Be("Presentation-print.pdf");
        file.LastNativePrintHandoffPlan.SuggestedDocumentName.Should().Be("Presentation");
        file.LastNativePrintHandoffPlan.SuggestedPrintJobName.Should().Be("Presentation - Notes Pages - Slide 1, 1 page");
        file.LastNativePrintHandoffPlan.Reason.Should().Contain("Test printer handoff deferred");
        file.LastPrintExecutionDescriptor.Should().NotBeNull();
        file.LastPrintExecutionDescriptor!.PackagePlan.Should().BeSameAs(package.Plan);
        file.LastPrintExecutionDescriptor.HandoffPlan.Should().BeSameAs(file.LastNativePrintHandoffPlan);
        file.LastPrintExecutionDescriptor.Validation.IsValid.Should().BeTrue();
        file.LastPrintExecutionDescriptor.IsHostReadyPdfPackage.Should().BeTrue();
        file.LastPrintExecutionDescriptor.CanMaterialize.Should().BeTrue();
        file.LastPrintExecutionDescriptor.SuggestedDocumentName.Should().Be("Presentation");
        file.LastPrintExecutionDescriptor.SuggestedPrintJobName.Should().Be(file.LastNativePrintHandoffPlan.SuggestedPrintJobName);
        package.Bytes.Length.Should().BeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(package.Bytes, 0, 5).Should().Be("%PDF-");
    }

    [StaFact]
    public void BuildPrintBackstagePlan_UsesSharedNotesRenderPageCountForWpfAdapter()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        getModel().Slides[0].Title = "Overflow notes";
        getModel().Slides[0].Notes = MakeTextBody(
            Enumerable.Range(1, 60)
                .Select(i => $"Speaker note line number {i} with enough words to be realistic.")
                .ToArray());

        var renderPlan = file.BuildNotesPagePdfRenderPlan();
        var backstagePlan = file.BuildPrintBackstagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages));

        renderPlan.Pages.Count.Should().BeGreaterThan(1);
        backstagePlan.PageCount.Should().Be(renderPlan.Pages.Count);
        backstagePlan.LayoutSummary.Should().Be($"Notes Pages - All slides, {renderPlan.Pages.Count} pages");
        backstagePlan.SelectedLayout.PackagePlan.PageCount.Should().Be(renderPlan.Pages.Count);
        backstagePlan.PreviewPlan.PageCount.Should().Be(renderPlan.Pages.Count);
        backstagePlan.PreviewPlan.Pages.Should().HaveCount(renderPlan.Pages.Count);
    }

    [StaFact]
    public void BuildVideoExportPlan_UsesSharedPlannerForWpfAdapter()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        getModel().Slides.Add(new Slide { Title = "Two" });
        getModel().Slides.Add(new Slide { Title = "Three" });

        var plan = file.BuildVideoExportPlan(new PresentationVideoExportRequest(
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CustomRange,
                StartSlideNumber: 2,
                EndSlideNumber: 3),
            PresentationVideoQualityKind.Hd,
            SecondsPerSlide: 12,
            UseRecordedTimings: false,
            IncludeNarration: false));

        plan.CommandId.Should().Be(PresentationExportPlanner.VideoExportCommandId);
        plan.DefaultExtensionWithDot.Should().Be(PresentationExportPlanner.VideoExportExtension);
        plan.SlideRange.SlideNumbers.Should().Equal(2, 3);
        plan.Quality.Quality.Should().Be(PresentationVideoQualityKind.Hd);
        plan.Quality.WidthPx.Should().Be(1280);
        plan.Quality.HeightPx.Should().Be(720);
        plan.SecondsPerSlide.Should().Be(12);
        plan.UseRecordedTimings.Should().BeFalse();
        plan.IncludeNarration.Should().BeFalse();
        plan.EstimatedDuration.Should().Be(TimeSpan.FromSeconds(24));
        plan.CanExecute.Should().BeFalse();
        plan.DisabledReason.Should().Be("Test encoder handoff deferred.");
    }

    [StaFact]
    public void BuildVideoExportPlan_EnablesWpfExecutionWhenEncoderIsAvailable()
    {
        var (_, file, getModel, _, _) = CreateHarness(canEncodeVideo: true);
        getModel().Slides.Add(new Slide { Title = "Two" });

        var plan = file.BuildVideoExportPlan();

        plan.IsImplemented.Should().BeTrue();
        plan.CanExecute.Should().BeTrue();
        plan.DisabledReason.Should().BeNull();
    }

    [StaFact]
    public void BuildVideoFramePackage_UsesSharedExecutorForWpfAdapter()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        getModel().Slides[0].Title = "Opening";
        getModel().Slides.Add(new Slide { Title = "Close" });

        var package = file.BuildVideoFramePackage(new PresentationVideoExportRequest(
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CurrentSlide,
                CurrentSlideNumber: 1),
            PresentationVideoQualityKind.Standard,
            SecondsPerSlide: 3,
            UseRecordedTimings: false,
            IncludeNarration: false));

        file.LastVideoFramePackage.Should().BeSameAs(package);
        file.LastVideoExportHandoffPlan.Should().NotBeNull();
        file.LastVideoExecutionDescriptor.Should().NotBeNull();
        file.LastVideoExecutionDescriptor!.PackagePlan.Should().BeSameAs(package.Plan);
        file.LastVideoExecutionDescriptor.HandoffPlan.Should().BeSameAs(file.LastVideoExportHandoffPlan);
        file.LastVideoExecutionDescriptor.Validation.IsValid.Should().BeTrue();
        file.LastVideoExecutionDescriptor.Validation.HasManifest.Should().BeTrue();
        file.LastVideoExecutionDescriptor.Validation.HasEncoderDeferredMarker.Should().BeTrue();
        file.LastVideoExecutionDescriptor.Validation.ExpectedFrameCount.Should().Be(1);
        file.LastVideoExecutionDescriptor.Validation.ManifestFrameCount.Should().Be(1);
        file.LastVideoExecutionDescriptor.Validation.ZipFrameEntryCount.Should().Be(1);
        file.LastVideoExecutionDescriptor.ContentType.Should().Be(PresentationVideoFramePackageExecutor.PackageContentType);
        file.LastVideoExecutionDescriptor.SuggestedPackageName.Should().Be("Presentation-video-encoder-input.zip");
        file.LastVideoExecutionDescriptor.ByteCount.Should().Be(package.Bytes.Length);
        file.LastVideoExecutionDescriptor.CanMaterialize.Should().BeTrue();
        file.LastVideoExportHandoffPlan!.PackagePlan.Should().BeSameAs(package.Plan);
        file.LastVideoExportHandoffPlan.Status.Should()
            .Be(PresentationVideoExportHandoffStatus.EncoderInputPackageReadyHostDeferred);
        file.LastVideoExportHandoffPlan.IsFramePackageReady.Should().BeTrue();
        file.LastVideoExportHandoffPlan.CanOpenHostEncoder.Should().BeFalse();
        file.LastVideoExportHandoffPlan.Mp4EncoderDeferredByHost.Should().BeTrue();
        file.LastVideoExportHandoffPlan.StatusText.Should()
            .Be("WPF video export host: MP4 encoder deferred; frame package ready");
        package.Plan.ExportPlan.CommandId.Should().Be(PresentationExportPlanner.VideoExportCommandId);
        package.Plan.ExportPlan.IsImplemented.Should().BeFalse();
        package.Plan.ExportPlan.CanExecute.Should().BeFalse();
        package.Plan.DeferredCapabilities.Should().Contain(PresentationVideoFramePackageExecutor.Mp4EncoderDeferred);
        package.Frames.Should().ContainSingle();
        package.Frames[0].FileName.Should().Be("frames/slide-01-frame-0001.png");
        package.Frames[0].WidthPx.Should().Be(852);
        package.Frames[0].HeightPx.Should().Be(480);
        package.Bytes.Length.Should().BeGreaterThan(100);
    }

    [StaFact]
    public void OpenPath_LoadsPptxFileAndMarksSavedWithPath()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        var path = WritePptx("Opened.pptx", "Quarterly Review");

        var opened = file.OpenPath(path);

        opened.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        file.DisplayName.Should().Be("Opened");
        getModel().Properties.Title.Should().Be("Quarterly Review");
    }

    [StaFact]
    public void OpenPath_StillLoadsLegacyFxpFileAndMarksSavedWithPath()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        var path = WriteFxp("Legacy.fxp", "Legacy Review");

        var opened = file.OpenPath(path);

        opened.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        file.DisplayName.Should().Be("Legacy");
        getModel().Properties.Title.Should().Be("Legacy Review");
    }

    [StaFact]
    public void Save_AfterEdit_WritesPptxToExistingPathAndClearsDirty()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        var path = WritePptx("Deck.pptx", "Initial");
        file.OpenPath(path).Should().BeTrue();

        getModel().Properties.Title = "Updated";
        file.MarkDirty();
        file.IsDirty.Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        PptxPackageReader.Read(path).Properties.Title.Should().Be("Updated");
    }

    [StaFact]
    public void Save_AfterEdit_StillWritesLegacyFxpToExistingPathAndClearsDirty()
    {
        var (_, file, getModel, _, _) = CreateHarness();
        var path = WriteFxp("Legacy.fxp", "Initial");
        file.OpenPath(path).Should().BeTrue();

        getModel().Properties.Title = "Updated Legacy";
        file.MarkDirty();
        file.IsDirty.Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        FxpFormat.Read(path).Properties.Title.Should().Be("Updated Legacy");
    }

    [StaFact]
    public void Save_OnCleanOpenedPresentation_StaysClean()
    {
        var (_, file, _, _, _) = CreateHarness();
        var path = WritePptx("Clean.pptx", "Clean");
        file.OpenPath(path).Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
    }

    [StaFact]
    public void OpenPath_CorruptPresentation_UsesInjectedMessageServiceForError()
    {
        var (_, file, _, _, messages) = CreateHarness();
        var path = Path.Combine(_tempDir, "corrupt.pptx");
        File.WriteAllText(path, "this is not a valid pptx");

        var opened = file.OpenPath(path);

        opened.Should().BeFalse();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().BeNull();
        messages.Messages.Should().ContainSingle();
        var error = messages.Messages[0];
        error.Message.Should().StartWith("Could not open the presentation:\n");
        error.Title.Should().Be("FreeP");
        error.Buttons.Should().Be(UserMessageButtons.Ok);
        error.Icon.Should().Be(UserMessageIcon.Error);
    }

    [StaFact]
    public void MainWindowClose_UsesInjectedMessageServiceForSavePrompt()
    {
        var messages = new RecordingUserMessageService { NextResult = UserMessageResult.No };
        var window = new MainWindow(new FreePOptions(), messageService: messages);

        GetFileCommands(window).MarkDirty();
        window.Close();

        messages.Messages.Should().ContainSingle();
        var prompt = messages.Messages[0];
        prompt.Message.Should().Be("Do you want to save changes to Untitled before closing?");
        prompt.Title.Should().Be("FreeP");
        prompt.Buttons.Should().Be(UserMessageButtons.YesNoCancel);
        prompt.Icon.Should().Be(UserMessageIcon.Warning);
    }

    // R133-wpf-startup-file-args: FreeP.App.Host.Program.Main never read command-line/file-association
    // arguments at all, so double-clicking a presentation, dragging one onto the icon, or passing a
    // path on the command line always opened the hardcoded empty presentation instead. These pin the
    // fix at the MainWindow constructor -- the same seam Program.cs's CreateWindow lambda calls in
    // production (`new MainWindow(options, optionsStore, startupFilePaths: startupFilePaths)`) -- so
    // they exercise the real production entry point, not just FileCommands.OpenPath in isolation.
    [StaFact]
    public void MainWindow_WithStartupFilePath_OpensItInsteadOfTheEmptyPresentation()
    {
        var path = WritePptx("Startup.pptx", "Opened from the command line");
        var messages = new RecordingUserMessageService();

        var window = new MainWindow(new FreePOptions(), messageService: messages, startupFilePaths: [path]);

        messages.Messages.Should().BeEmpty();
        GetFileCommands(window).CurrentPath.Should().Be(path);
        GetFileCommands(window).IsDirty.Should().BeFalse();
        window.Close();
    }

    // Sibling no-regression: constructing MainWindow without startupFilePaths (double-clicking
    // FreeP.exe itself, or any other in-process construction) must still show the empty presentation
    // unchanged -- proves the fix does not widen into replacing it when there is nothing to open.
    [StaFact]
    public void MainWindow_WithoutStartupFilePaths_StillShowsTheEmptyPresentation()
    {
        var window = new MainWindow(new FreePOptions());

        GetFileCommands(window).CurrentPath.Should().BeNull();
        GetFileCommands(window).IsDirty.Should().BeFalse();
        window.Close();
    }

    // A missing startup-file argument (a stale recent-file path, a typo, a since-deleted presentation)
    // must degrade to the empty presentation with an error message -- not crash the app before it is
    // usable, which would be strictly worse than the original silently-ignored-arguments bug.
    [StaFact]
    public void MainWindow_WithMissingStartupFilePath_ShowsErrorAndKeepsTheEmptyPresentation()
    {
        var missingPath = Path.Combine(_tempDir, "does-not-exist.pptx");
        var messages = new RecordingUserMessageService();

        var window = new MainWindow(new FreePOptions(), messageService: messages, startupFilePaths: [missingPath]);

        GetFileCommands(window).CurrentPath.Should().BeNull();
        messages.Messages.Should().ContainSingle();
        messages.Messages[0].Message.Should().StartWith("Could not open the presentation:\n");
        window.Close();
    }

    // An unparseable/unrecognized startup-file argument (wrong extension, corrupt container) must
    // likewise degrade to the empty presentation with an error instead of taking the app down.
    [StaFact]
    public void MainWindow_WithUnsupportedStartupFilePath_ShowsErrorAndKeepsTheEmptyPresentation()
    {
        var unsupportedPath = Path.Combine(_tempDir, "notes.unsupported");
        File.WriteAllText(unsupportedPath, "not a presentation FreeP can read");
        var messages = new RecordingUserMessageService();

        var window = new MainWindow(
            new FreePOptions(), messageService: messages, startupFilePaths: [unsupportedPath]);

        GetFileCommands(window).CurrentPath.Should().BeNull();
        messages.Messages.Should().ContainSingle();
        messages.Messages[0].Message.Should().StartWith("Could not open the presentation:\n");
        window.Close();
    }

    private static TextBody MakeTextBody(params string[] paragraphs)
    {
        var body = new TextBody();
        foreach (var text in paragraphs)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = text });
            body.Paragraphs.Add(paragraph);
        }

        return body;
    }

    private static FileCommands GetFileCommands(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_file",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (FileCommands)field!.GetValue(window)!;
    }

    private string WritePptx(string name, string title)
    {
        var path = Path.Combine(_tempDir, name);
        PptxPackageWriter.Write(CreatePresentation(title), path);
        return path;
    }

    private string WriteFxp(string name, string title)
    {
        var path = Path.Combine(_tempDir, name);
        FxpFormat.Write(CreatePresentation(title), path);
        return path;
    }

    private static Presentation CreatePresentation(string title)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = title;
        return presentation;
    }

}
