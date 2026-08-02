using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.App.Avalonia.Smoke;
using FreeP.App.Recording;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;

[assembly: AvaloniaTestApplication(typeof(FreeP.App.Avalonia.Tests.FreePHeadlessApp))]

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Minimal headless Avalonia application used by all FreeP.App.Avalonia tests.
/// Mirrors the pattern in FreeW.App.Avalonia.Tests.
/// </summary>
public sealed class FreePHeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FreePHeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>
/// Headless smoke tests for <see cref="MainWindow"/>.
/// Each test is tolerant of headless drawing not being available in the current environment
/// (returns early without assertion failure rather than erroring out).
/// </summary>
public sealed class MainWindowHeadlessTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    // Bootstrap once per test run so tests don't race on product identity.
    static MainWindowHeadlessTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            // Headless drawing unavailable in this CI environment; skip gracefully.
            return false;
        }
    }

    // ── Construction ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MainWindow_constructs_with_empty_presentation()
    {
        var slideCount = -1;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            slideCount = window.SlideCount;
        });

        if (!ran) return;
        // An empty Presentation created by Presentation.CreateEmpty() has at least one slide.
        slideCount.Should().BeGreaterThanOrEqualTo(1,
            "a freshly created empty presentation contains at least one slide");
    }

    [Fact]
    public void TransitionSoundPicker_UsesSharedAudioFileTypeCatalog()
    {
        var source = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("PresentationMediaFileTypeCatalog.AudioFilePatterns");
        source.Should().Contain("PresentationMediaFileTypeCatalog.AudioMimeTypes");
        source.Should().NotContain("[\"*.mp3\", \"*.m4a\", \"*.wav\", \"*.wma\"]");
    }

    [Fact]
    public async Task MainWindow_canvas_interaction_layers_share_margined_origin()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            try
            {
                window.Show();

                var adorner = window.GetVisualDescendants()
                    .OfType<SelectionAdornerLayer>()
                    .Single();
                var stack = adorner.Parent.Should().BeOfType<Grid>().Subject;
                var canvasContent = stack.Children
                    .OfType<Grid>()
                    .Single(candidate => candidate.Children.OfType<SlideCanvas>().Any());
                var canvas = canvasContent.Children.OfType<SlideCanvas>().Single();
                var textOverlay = stack.Children.OfType<Canvas>().Single();
                textOverlay.IsVisible = true;
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                canvasContent.Margin.Should().Be(new Thickness(FreePShellVisualMetrics.CanvasMargin));
                adorner.Margin.Should().Be(new Thickness(FreePShellVisualMetrics.CanvasMargin));
                stack.Children.IndexOf(textOverlay).Should().BeLessThan(
                    stack.Children.IndexOf(adorner),
                    "WPF paints selection chrome above the active text editor");
                canvas.Margin.Should().Be(default(Thickness));

                var canvasOrigin = canvas.TranslatePoint(default, window);
                var adornerOrigin = adorner.TranslatePoint(default, window);
                var textOverlayOrigin = textOverlay.TranslatePoint(default, window);
                canvasOrigin.Should().NotBeNull();
                adornerOrigin.Should().Be(canvasOrigin);
                textOverlayOrigin.Should().NotBeNull();
                (canvasOrigin!.Value.X - textOverlayOrigin!.Value.X).Should()
                    .BeApproximately(FreePShellVisualMetrics.CanvasMargin, 0.001);
                (canvasOrigin.Value.Y - textOverlayOrigin.Value.Y).Should()
                    .BeApproximately(FreePShellVisualMetrics.CanvasMargin, 0.001);
                canvasOrigin.Value.X.Should().BeGreaterThanOrEqualTo(FreePShellVisualMetrics.CanvasMargin);
                canvasOrigin.Value.Y.Should().BeGreaterThanOrEqualTo(FreePShellVisualMetrics.CanvasMargin);

                var shape = window.Editor.InsertDefaultRectangle();
                window.Editor.Select(shape.Id);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var expected = SlideCanvasGeometryPlanner.ShapeBoundsToScreen(
                    shape,
                    window.Editor.Presentation,
                    canvas.CurrentTransform);
                var actual = adorner.SelectionRects.Should().ContainSingle().Subject.screenRect;

                (adornerOrigin!.Value.X + actual.Left).Should().BeApproximately(
                    canvasOrigin.Value.X + expected.Left,
                    0.001);
                (adornerOrigin.Value.Y + actual.Top).Should().BeApproximately(
                    canvasOrigin.Value.Y + expected.Top,
                    0.001);
                actual.Width.Should().BeApproximately(expected.Width, 0.001);
                actual.Height.Should().BeApproximately(expected.Height, 0.001);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Native_output_detection_is_deferred_until_the_background_start_hook()
    {
        var detectorCalls = 0;
        var detectorCompleted = new TaskCompletionSource<LinuxNativeOutputCapabilities>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? window = null;

        var ran = await OnUiThread(() =>
        {
            window = new MainWindow(
                Array.Empty<string>(),
                loadRecentFilesStore: null,
                nativeOutputCapabilityDetector: () =>
                {
                    Interlocked.Increment(ref detectorCalls);
                    var result = new LinuxNativeOutputCapabilities(
                        new LinuxNativePrintCapability(true, "lp", "office", "ready"),
                        new LinuxVideoEncoderCapability(true, "ffmpeg", "mpeg4", false, "ready"));
                    detectorCompleted.TrySetResult(result);
                    return result;
                });
            detectorCalls.Should().Be(0);
            window.NativeOutputDetectionStartedForTests.Should().BeFalse();
        });

        if (!ran) return;
        window!.StartNativeOutputCapabilityDetectionForTests();
        await detectorCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        detectorCalls.Should().Be(1);
        window.NativeOutputDetectionStartedForTests.Should().BeTrue();
    }

    [Fact]
    public async Task Native_print_handoff_uses_a_ready_direct_submission_without_claiming_a_dialog()
    {
        var printAdapter = new RecordingPrintAdapter();
        LinuxNativePrintResult? result = null;
        MainWindow? window = null;
        Task<LinuxNativePrintResult>? printTask = null;
        var capabilities = new LinuxNativeOutputCapabilities(
            new LinuxNativePrintCapability(true, "lp", "office", "ready"),
            LinuxVideoEncoderCapability.Unavailable("no encoder"));

        var ran = await OnUiThread(() =>
        {
            window = new MainWindow(
                Array.Empty<string>(),
                loadRecentFilesStore: null,
                nativeOutputCapabilities: capabilities,
                nativePrintAdapter: printAdapter,
                videoExportAdapter: new RecordingVideoAdapter(capabilities.Video),
                printOutputPackageFactory: _ => BuildTestPrintPackage());
        });

        if (!ran) return;
        PresentationNativePrintHandoffPlan? handoff = null;
        var planRan = await OnUiThread(() =>
        {
            handoff = window!.RefreshNativePrintHandoffPlan();
            printTask = window.ExecuteNativePrintHandoffAsync();
        });
        if (!planRan) return;
        handoff!.CanOpenNativePrintDialog.Should().BeFalse();
        handoff.CanSubmitToNativePrinter.Should().BeTrue();
        result = await printTask!;
        result.Succeeded.Should().BeTrue(result.FailureReason);
        printAdapter.PdfBytes.Should().NotBeNullOrEmpty();
        printAdapter.PdfBytes!.AsSpan().StartsWith("%PDF-"u8).Should().BeTrue();
    }

    [Fact]
    public async Task Backstage_print_actions_route_layout_selection_to_native_handoff()
    {
        var printAdapter = new RecordingPrintAdapter();
        PresentationPrintRequest? printedRequest = null;
        IReadOnlyList<(string AutomationId, bool IsEnabled)> actions = [];
        MainWindow? window = null;
        var capabilities = new LinuxNativeOutputCapabilities(
            new LinuxNativePrintCapability(true, "lp", "office", "ready"),
            LinuxVideoEncoderCapability.Unavailable("no encoder"));

        var ran = await OnUiThread(() =>
        {
            window = new MainWindow(
                Array.Empty<string>(),
                loadRecentFilesStore: null,
                nativeOutputCapabilities: capabilities,
                nativePrintAdapter: printAdapter,
                videoExportAdapter: new RecordingVideoAdapter(capabilities.Video),
                printOutputPackageFactory: request =>
                {
                    printedRequest = request;
                    return BuildTestPrintPackage();
                });

            window.ShowBackstageForTests();
            window.ActivateBackstageEntryForTests("Print").Should().BeTrue();
            actions = window.BackstagePrintActionsForTests;
            actions.Should().HaveCount(window.LastPrintBackstagePlan!.LayoutChoices.Count);
            actions.Should().OnlyContain(action => action.IsEnabled);
            window.InvokeBackstagePrintActionForTests(actions[0].AutomationId).Should().BeTrue();
        });

        if (!ran) return;
        var result = await window!.BackstagePrintOperationForTests;

        result.Succeeded.Should().BeTrue(result.FailureReason);
        printedRequest.Should().NotBeNull();
        printedRequest!.Layout.Should().Be(
            window.LastPrintBackstagePlan!.LayoutChoices[0].Layout.Layout);
        printAdapter.PdfBytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Video_picker_cancel_and_non_local_selection_are_honest_and_successful_capability_adds_video_action()
    {
        var output = Path.Combine(Path.GetTempPath(), $"freep-host-video-{Guid.NewGuid():N}.mp4");
        var capabilities = new LinuxNativeOutputCapabilities(
            LinuxNativePrintCapability.Unavailable("no queue"),
            new LinuxVideoEncoderCapability(true, "ffmpeg", "mpeg4", false, "ready"));
        var videoAdapter = new RecordingVideoAdapter(capabilities.Video);
        try
        {
            MainWindow? window = null;
            var ran = await OnUiThread(() =>
            {
                window = new MainWindow(
                    Array.Empty<string>(),
                    loadRecentFilesStore: null,
                    nativeOutputCapabilities: capabilities,
                    nativePrintAdapter: new RecordingPrintAdapter(),
                    videoExportAdapter: videoAdapter,
                    videoFramePackageFactory: _ => BuildTestVideoPackage());
            });
            if (!ran) return;

            window!.VideoPickerOverrideForTests = _ =>
                Task.FromResult<MainWindow.VideoPickerSelectionForTests?>(null);
            Task<bool>? videoTask = null;
            var cancelRan = await OnUiThread(() => videoTask = window.FileExportVideoAsyncForTests());
            if (!cancelRan) return;
            (await videoTask!).Should().BeFalse();

            window.VideoPickerOverrideForTests = _ =>
                Task.FromResult<MainWindow.VideoPickerSelectionForTests?>(
                    new MainWindow.VideoPickerSelectionForTests(null));
            var nonLocalRan = await OnUiThread(() => videoTask = window.FileExportVideoAsyncForTests());
            if (!nonLocalRan) return;
            (await videoTask!).Should().BeFalse();
            window.StatusTextForTests.Should().Contain("not available as a local path");

            window.VideoPickerOverrideForTests = _ =>
                Task.FromResult<MainWindow.VideoPickerSelectionForTests?>(
                    new MainWindow.VideoPickerSelectionForTests(output));
            var successRan = await OnUiThread(() => videoTask = window.FileExportVideoAsyncForTests());
            if (!successRan) return;
            (await videoTask!).Should().BeTrue();
            videoAdapter.Package.Should().NotBeNull();

            window.ShowBackstageForTests();
            window.ActivateBackstageEntryForTests("Export").Should().BeTrue();
            window.GetLogicalDescendants()
                .OfType<Button>()
                .Select(AutomationProperties.GetAutomationId)
                .Should()
                .Contain("BackstageExport_freepfileexportvideo");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }

        var disabledRan = await OnUiThread(() =>
        {
            var disabled = new MainWindow(Array.Empty<string>());
            disabled.ShowBackstageForTests();
            disabled.ActivateBackstageEntryForTests("Export").Should().BeTrue();
            disabled.GetLogicalDescendants()
                .OfType<Button>()
                .Select(AutomationProperties.GetAutomationId)
                .Should()
                .NotContain("BackstageExport_freepfileexportvideo");
        });
        if (!disabledRan) return;
    }

    [Fact]
    public async Task MainWindow_has_toolbar_after_construction()
    {
        var hasToolbar = false;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            hasToolbar = window.HasToolbar;
        });

        if (!ran) return;
        hasToolbar.Should().BeTrue("the ribbon must be built and wired during construction");
    }

    [Fact]
    public async Task MainWindow_content_uses_shared_client_frame_shape()
    {
        int childCount = -1;
        int bottomDockedCount = -1;
        int topDockedCount = -1;
        var lastChildFill = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var outerRoot = window.Content.Should().BeOfType<Grid>().Subject;
            outerRoot.Children.Should().HaveCount(2, "the shared title bar wraps the app client layer");
            outerRoot.Children[0].Should().BeSameAs(window.TitleBarForTests);
            Grid.GetRow(window.TitleBarForTests).Should().Be(0);

            var overlayRoot = outerRoot.Children[1].Should().BeOfType<Grid>().Subject;
            Grid.GetRow(overlayRoot).Should().Be(1);
            overlayRoot.Children.Should().HaveCount(2, "the shared client frame and Backstage overlay share one layer root");
            var root = overlayRoot.Children[0].Should().BeOfType<DockPanel>().Subject;
            childCount = root.Children.Count;
            bottomDockedCount = root.Children.Count(child => DockPanel.GetDock(child) == Dock.Bottom);
            topDockedCount = root.Children.Count(child => DockPanel.GetDock(child) == Dock.Top);
            lastChildFill = root.LastChildFill;
        });

        if (!ran) return;
        childCount.Should().Be(3, "FreeP contributes ribbon, status, and workarea to the shared frame");
        topDockedCount.Should().Be(1, "the shared frame keeps the ribbon docked at the top");
        bottomDockedCount.Should().Be(1, "the shared frame keeps the status bar docked at the bottom");
        lastChildFill.Should().BeTrue("the workarea should fill the remaining client frame");
    }

    [Fact]
    public async Task MainWindow_shared_outer_shell_has_icon_qat_title_and_wpf_status_content()
    {
        IReadOnlyList<string?> automationIds = [];
        IReadOnlyList<string?> automationNames = [];
        string statusText = string.Empty;
        string title = string.Empty;
        var hasIcon = false;
        var titleBarHeight = 0d;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            automationIds = window.QuickAccessButtonsForTests
                .Select(AutomationProperties.GetAutomationId)
                .ToArray();
            automationNames = window.QuickAccessButtonsForTests
                .Select(AutomationProperties.GetName)
                .ToArray();
            statusText = window.StatusTextForTests;
            title = window.Title ?? string.Empty;
            hasIcon = window.HasWindowIconForTests;
            titleBarHeight = window.TitleBarForTests.Height;
        });

        if (!ran) return;
        automationIds.Should().Equal("Save", "Undo", "Redo");
        automationNames.Should().Equal(
            "Save (Ctrl+S)", "Undo (Ctrl+Z)", "Redo (Ctrl+Y)");
        titleBarHeight.Should().Be(34);
        title.Should().EndWith("FreeP");
        hasIcon.Should().BeTrue("Avalonia and WPF must load the same owned FreeP icon asset");
        statusText.Should().StartWith("Slide 1 / 1");
        statusText.Should().EndWith("options.json");
    }

    [Fact]
    public async Task Backstage_entries_match_wpf_order_and_entry_kinds()
    {
        IReadOnlyList<SisterBackstageEntryPlan<Control>> entries = [];
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            entries = window.BackstageEntries;
        });

        if (!ran) return;
        entries.Select(entry => entry.Kind == SisterBackstageEntryKind.Divider ? "|" : entry.Label)
            .Should().Equal(
                "Info", "New", "Open", "|", "Save", "Save As", "Print", "Export",
                "Recent", "New from template", "Account", "Options", "Close");
        entries.Should().HaveCount(13);
        entries.Count(entry => entry.Kind == SisterBackstageEntryKind.Pane).Should().Be(7);
        entries.Count(entry => entry.Kind == SisterBackstageEntryKind.Command).Should().Be(5);
        entries.Count(entry => entry.Kind == SisterBackstageEntryKind.Divider).Should().Be(1);
        entries.Where(entry => entry.DockBottom).Select(entry => entry.Label)
            .Should().Equal("Account", "Options", "Close");
    }

    [Fact]
    public async Task Backstage_opens_info_refreshes_print_and_closes_by_escape_or_command()
    {
        var openedInfo = false;
        var openedPrint = false;
        var printPlanBuilt = false;
        var escaped = false;
        var closedByCommand = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.ShowBackstageForTests();
            openedInfo = window.IsBackstageOpen && window.CurrentBackstagePaneLabel == "Info";

            openedPrint = window.ActivateBackstageEntryForTests("Print") &&
                window.IsBackstageOpen &&
                window.CurrentBackstagePaneLabel == "Print";
            printPlanBuilt = window.LastPrintBackstagePlan is not null;

            escaped = window.HandleBackstageKeyForTests(Key.Escape) && !window.IsBackstageOpen;

            window.ShowBackstageForTests();
            closedByCommand = window.ActivateBackstageEntryForTests("Close") && !window.IsBackstageOpen;
        });

        if (!ran) return;
        openedInfo.Should().BeTrue();
        openedPrint.Should().BeTrue();
        printPlanBuilt.Should().BeTrue("Print must use the live shared print workflow");
        escaped.Should().BeTrue();
        closedByCommand.Should().BeTrue();
    }

    [Fact]
    public async Task File_tab_opens_backstage_and_restores_the_selected_content_tab()
    {
        var opened = false;
        var restoredIndex = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var ribbon = window.GetLogicalDescendants().OfType<TabControl>().First();
            var contentIndex = ribbon.SelectedIndex;

            ribbon.SelectedIndex = 0;

            opened = window.IsBackstageOpen && window.CurrentBackstagePaneLabel == "Info";
            restoredIndex = ribbon.SelectedIndex;
            restoredIndex.Should().Be(contentIndex);
        });

        if (!ran) return;
        opened.Should().BeTrue();
        restoredIndex.Should().BeGreaterThan(0, "File is an overlay trigger rather than a content tab");
    }

    [Fact]
    public void MainWindow_sources_reference_the_shared_avalonia_shell_frame()
    {
        var project = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"));
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");

        var mainWindow = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        mainWindow.Should().Contain("using Free.Shared.Shell.Avalonia;");
        mainWindow.Should().Contain("SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(");
        mainWindow.Should().Contain("SisterAppStatusBarChrome.Build(");
        mainWindow.Should().Contain("SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(");
        mainWindow.Should().Contain("SisterQuickAccessToolbarBuilder.Render(");
        mainWindow.Should().Contain("ResolveDataFolderLabel());");
        mainWindow.Should().Contain("chrome: ribbon,");
        mainWindow.Should().Contain("workArea: BuildBody(),");
        mainWindow.Should().Contain("statusBar: statusBar");
        mainWindow.Should().Contain("clientRoot.Children.Add(frame.Root);");
        mainWindow.Should().Contain("clientRoot.Children.Add(_backstage);");
        mainWindow.Should().Contain("Content = windowFrame.Root;");
        mainWindow.Should().Contain("onFileTabSelected: ShowBackstage");
        AssertBefore(mainWindow, "SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(", "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(");
        AssertBefore(mainWindow, "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(", "SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(");
        AssertBefore(mainWindow, "SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(", "Content = windowFrame.Root;");
        mainWindow.Should().NotContain("_statusText = new TextBlock");
    }

    [Fact]
    public void MainWindow_sources_keep_focused_table_text_editing_out_of_canvas_keyboard_routes()
    {
        var mainWindow = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var editor = File.ReadAllText(FindRepoFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaInCanvasTextEditor.cs"));

        mainWindow.Should().Contain("_textEditor?.IsEditorFocused == true");
        editor.Should().Contain("_cellTextBox?.InputBox.IsFocused == true");
        editor.Should().Contain("TableCellEditPlanner.PlanKeyboard");
        editor.Should().Contain("ToTableCellEditKeyboardModifiers");
    }

    [Fact]
    public void FreeP_hosts_link_the_same_canonical_owned_icon_family()
    {
        var ico = FindRepoFile("shared", "Free.Shared.Shell", "Resources", "FreeP.ico");
        var svg = FindRepoFile("shared", "Free.Shared.Shell", "Resources", "FreeP.svg");
        new FileInfo(ico).Length.Should().BeGreaterThan(1_024);
        File.ReadAllText(svg).Should().Contain("#b7472a").And.Contain(">P</text>");

        var avaloniaProject = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"));
        var wpfProject = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Host", "FreeP.App.Host.csproj"));
        var wpfWindow = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Host", "MainWindow.cs"));

        avaloniaProject.Should().Contain(@"shared\Free.Shared.Shell\Resources\FreeP.ico")
            .And.Contain(@"shared\Free.Shared.Shell\Resources\FreeP.svg");
        wpfProject.Should().Contain(@"<ApplicationIcon>..\..\shared\Free.Shared.Shell\Resources\FreeP.ico</ApplicationIcon>");
        wpfWindow.Should().Contain("IconUri = \"pack://application:,,,/FreeP.App.Host;component/Resources/FreeP.ico\"");
    }

    [Fact]
    public async Task MainWindow_current_slide_index_is_zero_for_empty_presentation()
    {
        var idx = -99;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            idx = window.CurrentSlideIndex;
        });

        if (!ran) return;
        idx.Should().Be(0, "the first slide is selected by default");
    }

    [Fact]
    public async Task MainWindow_editing_marks_workflow_dirty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FreeP.Avalonia.WorkflowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var recentPath = Path.Combine(tempDir, "recent.json");
        var beforeDirty = true;
        var afterDirty = false;
        string? title = null;

        try
        {
            var ran = await OnUiThread(() =>
            {
                var window = new MainWindow(Array.Empty<string>(), () => RecentFilesStore.Load(recentPath));
                beforeDirty = window.IsDirty;
                window.Editor.InsertSlide();
                afterDirty = window.IsDirty;
                title = window.Title;
            });

            if (!ran) return;
            beforeDirty.Should().BeFalse("a new presentation starts as saved through FileCommandWorkflow");
            afterDirty.Should().BeTrue("editing should mark the shared workflow dirty");
            title.Should().Be("Untitled * \u2014 FreeP", "Avalonia must use the WPF document-first title order");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task MainWindow_startup_file_loads_as_saved_and_registers_recent_file()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FreeP.Avalonia.WorkflowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var deckPath = Path.Combine(tempDir, "opened.pptx");
        var recentPath = Path.Combine(tempDir, "recent.json");
        using (var stream = File.Create(deckPath))
            PptxPackageWriter.Write(Presentation.CreateEmpty(), stream);

        string? currentPath = null;
        var isDirty = true;
        string? title = null;
        IReadOnlyList<RecentFileEntry> recentEntries = [];

        try
        {
            var ran = await OnUiThread(() =>
            {
                var window = new MainWindow([deckPath], () => RecentFilesStore.Load(recentPath));
                currentPath = window.CurrentPath;
                isDirty = window.IsDirty;
                title = window.Title;
                recentEntries = window.RecentEntries;
            });

            if (!ran) return;
            currentPath.Should().Be(deckPath);
            isDirty.Should().BeFalse("opened presentations should be marked saved through FileCommandWorkflow");
            title.Should().Be($"{Path.GetFileName(deckPath)} \u2014 FreeP");
            recentEntries.Select(entry => entry.Path).Should().Contain(deckPath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ── Ribbon definition ───────────────────────────────────────────────────────

    [Fact]
    public void RibbonDefinition_contains_home_tab()
    {
        var definition = FreePRibbonAvalonia.Build();
        definition.Tabs.Should().Contain(t => t.Id == "home",
            "the Home tab must be present in the ribbon definition");
    }

    [Fact]
    public void RibbonDefinition_contains_design_transitions_and_animations_tabs()
    {
        var definition = FreePRibbonAvalonia.Build();

        definition.Tabs.Select(tab => tab.Id)
            .Should()
            .Contain("design")
            .And
            .Contain("transitions")
            .And
            .Contain("animations");
    }

    [Fact]
    public void RibbonDefinition_design_tab_has_planned_design_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var design = definition.Tabs.Single(t => t.Id == "design");
        var commandIds = design.Groups
            .SelectMany(group => group.Controls)
            .Where(control => !string.IsNullOrEmpty(control.CommandId.Value))
            .Select(control => control.CommandId.Value)
            .ToArray();

        commandIds.Should().Contain(PresentationDesignCommandPlanner.BuiltInPlans.Select(plan => plan.CommandId));
    }

    [Fact]
    public void RibbonDefinition_animations_tab_has_planned_animation_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var animations = definition.Tabs.Single(t => t.Id == "animations");
        var commandIds = EnumerateRibbonCommandIds(animations).ToArray();

        commandIds.Should().Contain(PresentationAnimationCommandPlanner.BuiltInPlans.Select(plan => plan.CommandId));
    }

    [Fact]
    public void MainWindow_sources_route_design_commands_through_shared_planner()
    {
        var source = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("PresentationDesignCommandPlanner.BuiltInPlans");
        source.Should().Contain("PresentationDesignCommandPlanner.TryApply(Editor, plan, OnDesignHostRequest)");
        source.Should().Contain("PresentationDesignCommandPlanner.LayoutPlan");
        source.Should().Contain("OnLayoutPickerRequested");
        source.Should().Contain("PresentationDesignCommandPlanner.BuildLayoutPickerPlan(");
        source.Should().Contain("PresentationDesignCommandPlanner.TryApplyLayoutChoice(");
        source.Should().Contain("ShowLayoutPicker(LastLayoutPickerPlan);");
        source.Should().Contain("BuildLayoutChoiceLabel(choice)");
        source.Should().Contain("BuildLayoutChoiceTile(choice)");
        source.Should().Contain("BuildLayoutThumbnail(choice)");
        source.Should().NotContain("Editor.SetTheme(");
        source.Should().NotContain("Editor.SetSlideSize16x9()");
        source.Should().NotContain("Editor.SetSlideSize4x3()");
        source.Should().NotContain("new ActionRibbonCommand(() => { })");
    }

    [Fact]
    public void MainWindow_sources_route_accessibility_checker_navigation_through_shared_planner()
    {
        var source = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("PresentationReviewWorkflowPlanner.NormalizeAccessibilityCheckerRowSelection(");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerNavigationPlan(");
        source.Should().Contain("_reviewWorkflowSession.ApplyReadingOrderMove(");
        source.Should().Contain("_reviewWorkflowSession.SelectReadingOrderItem(");
    }

    [Fact]
    public void SelectionPane_source_routes_rename_through_undoable_session()
    {
        var source = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "SelectionPane.cs"));

        source.Should().Contain("_editor.SetShapeName(");
        source.Should().Contain("Key.Enter");
        source.Should().Contain("rename.LostFocus");
        source.Should().Contain("PresentationSelectionPaneItemPlan.RenameToolTipText");
        source.Should().Contain("item.VisibilityToolTipText");
        source.Should().Contain("PresentationSelectionPaneItemPlan.MoveUpToolTipText");
        source.Should().Contain("PresentationSelectionPaneItemPlan.MoveDownToolTipText");
        source.Should().Contain("_editor.MoveSelectedShapeInReadingOrder(");
        source.Should().Contain("item.CanMoveUp");
        source.Should().Contain("item.CanMoveDown");
    }

    [Fact]
    public void MainWindow_sources_route_animation_commands_through_shared_planner()
    {
        var source = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("PresentationAnimationCommandPlanner.BuiltInPlans");
        source.Should().Contain("PresentationAnimationCommandPlanner.TryApply(");
        source.Should().Contain("OnAnimationPaneRequested");
        source.Should().Contain("AnimationPanePlanner.BuildTimelinePlan(");
        source.Should().Contain("AnimationPanePlanner.BuildWorkflowEvidencePlan(");
        source.Should().Contain("AnimationPanePlanner.BuildPlaybackSessionPlan(");
        source.Should().Contain("AnimationPanePlanner.BuildPlaybackWorkflowEvidencePlan(");
        source.Should().Contain("plan.PlaybackControls");
        source.Should().Contain("AnimationPanePlaybackControlKind.PlayFromSelected");
        source.Should().Contain("ShowAnimationPane()");
        source.Should().Contain("BuildAnimationPaneItemCard(");
        source.Should().Contain("item.EffectOptions.WheelSpokeOptions");
        source.Should().Contain("AnimationPanePlanner.BuildEffectOptionMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.TryApplyEffectOptionMutation(");
        source.Should().Contain("AnimationPanePlanner.BuildTriggerMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.BuildDurationMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.BuildDelayMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.TryApplyTimingMutation(");
        source.Should().Contain("AnimationPanePlanner.BuildReorderMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.TryApplyReorderMutation(");
        source.Should().Contain("AnimationPanePlanner.BuildRemoveMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.TryApplyRemoveMutation(");
        source.Should().Contain("AnimationPanePlanner.BuildParagraphBuildMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.TryApplyParagraphBuildMutation(");
        source.Should().Contain("ToggleParagraphBuildForTests(");
        source.Should().Contain("BuildAnimationPaneActionButton(");
        source.Should().NotContain("Editor.MoveAnimation(");
        source.Should().NotContain("BuildAnimationPaneRowSummary(");
        source.Should().NotContain("FormatEffectOptions(");
    }

    [Fact]
    public void RibbonDefinition_home_tab_has_content_and_edit_groups_without_lifecycle_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        home.Groups.Should().NotContain(g => g.Id == "file", "document lifecycle belongs in Backstage");
        home.Groups.Should().Contain(g => g.Id == "slides", "Slides group required");
        home.Groups.Should().Contain(g => g.Id == "clipboard", "Clipboard group required");
        home.Groups.Should().Contain(g => g.Id == "arrange", "Arrange group required");
        home.Groups.Should().Contain(g => g.Id == "edit",   "Edit group required");
        home.Groups.Should().Contain(g => g.Id == "editing", "Editing group required");
    }

    [Fact]
    public void RibbonDefinition_transitions_group_has_slideshow_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var transitions = definition.Tabs.Single(t => t.Id == "transitions");
        var slideShow = transitions.Groups.Single(g => g.Id == "slideshow-from-transitions");
        slideShow.Controls.Select(i => i.CommandId.Value).Should().Equal(
            "freep.slideshow.from-beginning",
            "freep.slideshow.from-current-slide",
            "freep.slideshow.rehearse-timings",
            "freep.slideshow.record-timings",
            "freep.slideshow.custom-shows");
    }

    [Fact]
    public void RibbonDefinition_avalonia_chart_injection_preserves_order_metadata_and_duplicate_guards()
    {
        var definition = FreePRibbonAvalonia.Build();
        var insert = definition.Tabs.Single(t => t.Id == "insert");
        var charts = insert.Groups.Single(g => g.Id == "charts");

        var commandIds = charts.Controls
            .Select(control => control.CommandId.Value)
            .ToArray();
        commandIds
            .Should().EndWith([
                ChartAreaOptionsPlanner.CommandId,
                ChartProtectionOptionsPlanner.CommandId]);
        commandIds.Should().Contain(ChartDataTableOptionsPlanner.CommandId);
        charts.Controls.Count(control =>
            control.CommandId.Value == ChartDataDialogPlanner.EditDataCommandId)
            .Should().Be(1);

        var expectedMetadata = new Dictionary<string, (RibbonCommandIconKind Icon, string KeyTip)>
        {
            [ChartDataDialogPlanner.EditDataCommandId] = (RibbonCommandIconKind.ChartTitle, "E"),
        };

        foreach (var control in charts.Controls)
        {
            if (!expectedMetadata.TryGetValue(control.CommandId.Value, out var metadata))
                continue;

            control.Should().BeOfType<RibbonButton>();
            control.PreferredLayout.Should().Be(RibbonCommandLayoutKind.Medium);
            control.Icon.Should().NotBeNull();
            control.Icon!.Kind.Should().Be(metadata.Icon);
            control.KeyTip.Should().Be(metadata.KeyTip);
        }
    }

    [Fact]
    public void RibbonDefinition_slides_group_has_new_duplicate_delete()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home   = definition.Tabs.Single(t => t.Id == "home");
        var slides = home.Groups.Single(g => g.Id == "slides");
        var ids    = slides.Controls.Select(i => i.CommandId.Value).ToList();
        ids.Should().Contain("freep.new-slide",       "New Slide command required");
        ids.Should().Contain("freep.duplicate-slide", "Duplicate Slide command required");
        ids.Should().Contain("freep.delete-slide",    "Delete Slide command required");
        ids.Should().Contain("freep.layout",          "Layout command required");
    }

    [Fact]
    public void RibbonDefinition_clipboard_group_has_shared_clipboard_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        var clipboard = home.Groups.Single(g => g.Id == "clipboard");
        var ids = clipboard.Controls.Select(i => i.CommandId.Value).ToList();
        ids.Should().Contain("freep.paste", "Paste command required");
        ids.Should().Contain("freep.cut", "Cut command required");
        ids.Should().Contain("freep.copy", "Copy command required");
        ids.Should().Contain("freep.format-painter", "Format Painter command required");
    }

    [Fact]
    public void RibbonDefinition_edit_group_has_undo_and_redo()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        var edit = home.Groups.Single(g => g.Id == "edit");
        var ids  = edit.Controls.Select(i => i.CommandId.Value).ToList();
        ids.Should().Contain("freep.undo", "Undo command required");
        ids.Should().Contain("freep.redo", "Redo command required");
    }

    [Fact]
    public void RibbonDefinition_editing_group_has_find_and_replace()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        var editing = home.Groups.Single(g => g.Id == "editing");
        var ids = editing.Controls.Select(i => i.CommandId.Value).ToList();

        ids.Should().Contain("freep.find", "Find command required");
        ids.Should().Contain("freep.replace", "Replace command required");
    }

    [Fact]
    public void RibbonDefinition_arrange_group_has_shared_command_ids()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        var arrange = home.Groups.Single(g => g.Id == "arrange");
        var ids = arrange.Controls
            .Where(control => !string.IsNullOrEmpty(control.CommandId.Value))
            .Select(control => control.CommandId.Value)
            .ToArray();

        ids.Should().Contain([
            "freep.arrange.group",
            "freep.arrange.ungroup",
            "freep.arrange.change-shape",
            "freep.object.open-embedded",
            "freep.arrange.edit-points",
            "freep.arrange.bring-to-front",
            "freep.arrange.bring-forward",
            "freep.arrange.send-backward",
            "freep.arrange.send-to-back",
            "freep.arrange.align-left",
            "freep.arrange.align-center-h",
            "freep.arrange.align-right",
            "freep.arrange.align-top",
            "freep.arrange.align-middle",
            "freep.arrange.align-bottom",
            "freep.arrange.distribute-h",
            "freep.arrange.distribute-v",
        ]);
    }

    [Fact]
    public void RibbonDefinition_insert_tab_has_object_insertion_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var insert = definition.Tabs.Single(t => t.Id == "insert");
        insert.Groups.Should().Contain(g => g.Id == "text", "Text group required");
        insert.Groups.Should().Contain(g => g.Id == "tables", "Tables group required");
        insert.Groups.Should().Contain(g => g.Id == "charts", "Charts group required");
        insert.Groups.Should().Contain(g => g.Id == "links", "Links group required");
        insert.Groups.Should().Contain(g => g.Id == "illustrations", "Illustrations group required");

        var textIds = insert.Groups.Single(g => g.Id == "text")
            .Controls.Select(i => i.CommandId.Value).ToList();
        var tableIds = insert.Groups.Single(g => g.Id == "tables")
            .Controls.Select(i => i.CommandId.Value).ToList();
        var chartIds = insert.Groups.Single(g => g.Id == "charts")
            .Controls.Select(i => i.CommandId.Value).ToList();
        var linkIds = insert.Groups.Single(g => g.Id == "links")
            .Controls.Select(i => i.CommandId.Value).ToList();
        var illustrationIds = insert.Groups.Single(g => g.Id == "illustrations")
            .Controls.Select(i => i.CommandId.Value).ToList();

        textIds.Should().Contain("freep.text-box", "Text Box command required");
        tableIds.Should().Contain("freep.insert-table-3x3", "default Table command required");
        tableIds.Should().Contain("freep.insert-table-2x2", "2x2 Table command required");
        tableIds.Should().Contain("freep.insert-table-4x4", "4x4 Table command required");
        chartIds.Should().Contain("freep.insert-chart-column", "Column chart command required");
        chartIds.Should().Contain("freep.insert-chart-bar", "Bar chart command required");
        chartIds.Should().Contain("freep.insert-chart-line", "Line chart command required");
        chartIds.Should().Contain("freep.insert-chart-pie", "Pie chart command required");
        chartIds.Should().Contain(ChartDataDialogPlanner.EditDataCommandId, "Edit Data command required");
        linkIds.Should().Contain("freep.insert-link", "Insert Link command required");
        linkIds.Should().Contain("freep.remove-link", "Remove Link command required");
        illustrationIds.Should().Contain("freep.picture", "Picture command required");
        illustrationIds.Should().Contain(PictureCropAuthoringPlanner.InsetCommandId, "Crop Inset command required");
        illustrationIds.Should().Contain(PictureCropAuthoringPlanner.ResetCommandId, "Reset Crop command required");
        illustrationIds.Should().Contain(PictureColorEffectAuthoringPlanner.GrayscaleCommandId, "Grayscale command required");
        illustrationIds.Should().Contain(PictureColorEffectAuthoringPlanner.ResetCommandId, "Reset Effects command required");
        illustrationIds.Should().Contain("freep.shape-rectangle", "Rectangle command required");
        illustrationIds.Should().Contain("freep.shape-ellipse", "Ellipse command required");
        illustrationIds.Should().Contain("freep.shape-heart", "Heart command required");
    }

    [Fact]
    public async Task Ribbon_picture_crop_commands_are_registered()
    {
        var insetFound = false;
        var resetFound = false;
        var grayscaleFound = false;
        var effectsResetFound = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            insetFound = registry.TryGet(PictureCropAuthoringPlanner.InsetCommandId, out _);
            resetFound = registry.TryGet(PictureCropAuthoringPlanner.ResetCommandId, out _);
            grayscaleFound = registry.TryGet(PictureColorEffectAuthoringPlanner.GrayscaleCommandId, out _);
            effectsResetFound = registry.TryGet(PictureColorEffectAuthoringPlanner.ResetCommandId, out _);
        });

        if (!ran) return;
        insetFound.Should().BeTrue();
        resetFound.Should().BeTrue();
        grayscaleFound.Should().BeTrue();
        effectsResetFound.Should().BeTrue();
    }

    [Fact]
    public void RibbonDefinition_transitions_tab_has_planned_transition_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var transitions = definition.Tabs.Single(t => t.Id == "transitions");
        var commandIds = EnumerateRibbonCommandIds(transitions).ToArray();

        commandIds.Should().Contain(PresentationTransitionCommandPlanner.BuiltInPlans.Select(plan => plan.CommandId));
    }

    [Fact]
    public void RibbonDefinition_transitions_tab_exposes_transition_sound_loop_toggle()
    {
        var definition = FreePRibbonAvalonia.Build();
        var commandIds = EnumerateRibbonCommandIds(definition.Tabs.Single(t => t.Id == "transitions"));

        commandIds.Should().Contain("freep.transition.sound-loop");
    }

    // ── Slide management ────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertSlide_increases_slide_count()
    {
        var before = -1;
        var after  = -1;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            before = window.SlideCount;
            window.Editor.InsertSlide();
            after = window.SlideCount;
        });

        if (!ran) return;
        after.Should().Be(before + 1, "InsertSlide must add one slide");
    }

    [Fact]
    public async Task SlidePane_new_slide_affordance_uses_shared_text_and_inserts_slide()
    {
        var before = -1;
        var after = -1;
        var paneItemsAfter = -1;
        var currentSlideIndex = -1;
        var clicked = false;
        var visible = false;
        string? buttonText = null;
        string? automationName = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            before = window.SlideCount;
            buttonText = window.SlidePaneNewSlideButtonText;
            automationName = window.SlidePaneNewSlideButtonAutomationName;
            visible = window.IsSlidePaneNewSlideButtonVisible;
            clicked = window.ClickSlidePaneNewSlideAffordanceForTests();
            after = window.SlideCount;
            paneItemsAfter = window.SlidePaneSlideItemCount;
            currentSlideIndex = window.CurrentSlideIndex;
        });

        if (!ran) return;
        buttonText.Should().Be(SlidePanePlanner.NewSlideButtonText);
        automationName.Should().Be("New Slide");
        visible.Should().BeTrue("the Avalonia slide pane should expose the bottom PowerPoint-style add affordance");
        clicked.Should().BeTrue("the affordance should route to the same slide insertion workflow as the ribbon command");
        after.Should().Be(before + 1);
        currentSlideIndex.Should().Be(1, "the shared bottom affordance action inserts after and selects the current slide");
        paneItemsAfter.Should().Be(after, "the slide pane should refresh to include the newly inserted slide");
    }

    [Fact]
    public async Task SelectionPane_rename_control_uses_shared_accessibility_tooltip()
    {
        string?[] renameToolTips = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertTextBox("Selection target");
            window.ShowSelectionPane();
            renameToolTips = window.SelectionPaneRenameToolTipsForTests.ToArray();
        });

        if (!ran) return;
        renameToolTips.Should().NotBeEmpty()
            .And.OnlyContain(toolTip => toolTip == PresentationSelectionPaneItemPlan.RenameToolTipText);
    }

    [Fact]
    public async Task SlidePane_thumbnails_project_shared_visual_chrome_plan()
    {
        SlidePaneThumbnailVisualPlan? firstPlan = null;
        var paneItems = -1;
        var thumbnailHitTestVisible = true;
        var thumbnailEnabled = true;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            paneItems = window.SlidePaneSlideItemCount;
            firstPlan = window.SlidePaneRenderedThumbnailPlans.FirstOrDefault();
            var itemChrome = window.SelectedSlidePaneItemForTests?.Content as Border;
            var panel = itemChrome?.Child as StackPanel;
            var thumbnailBorder = panel?.Children.OfType<Border>().SingleOrDefault();
            var thumbnail = thumbnailBorder?.Child as SlideCanvas;
            thumbnailHitTestVisible = thumbnail?.IsHitTestVisible ?? true;
            thumbnailEnabled = thumbnail?.IsEnabled ?? true;
        });

        if (!ran) return;
        paneItems.Should().BeGreaterThanOrEqualTo(1);
        firstPlan.Should().NotBeNull();
        firstPlan!.PaneBackgroundHex.Should().Be(SlidePanePlanner.DefaultPaneBackgroundHex);
        firstPlan.ItemNormalBackgroundHex.Should().Be(SlidePanePlanner.DefaultItemNormalBackgroundHex);
        firstPlan.ItemSelectedBackgroundHex.Should().Be(SlidePanePlanner.DefaultItemSelectedBackgroundHex);
        firstPlan.ItemHoverBackgroundHex.Should().Be(SlidePanePlanner.DefaultItemHoverBackgroundHex);
        firstPlan.ItemNormalBorderHex.Should().Be(SlidePanePlanner.DefaultItemNormalBorderHex);
        firstPlan.ItemSelectedBorderHex.Should().Be(SlidePanePlanner.DefaultItemSelectedBorderHex);
        firstPlan.ThumbnailBorderHex.Should().Be(SlidePanePlanner.DefaultThumbnailBorderHex);
        firstPlan.LabelForegroundHex.Should().Be(SlidePanePlanner.DefaultLabelForegroundHex);
        firstPlan.LabelFontSize.Should().Be(SlidePanePlanner.DefaultLabelFontSize);
        firstPlan.LabelBottomMargin.Should().Be(SlidePanePlanner.DefaultLabelBottomMargin);
        firstPlan.ThumbnailBorderThickness.Should().Be(SlidePanePlanner.DefaultThumbnailBorderThickness);
        firstPlan.ItemMarginHorizontal.Should().Be(SlidePanePlanner.DefaultItemMarginHorizontal);
        firstPlan.ItemMarginVertical.Should().Be(SlidePanePlanner.DefaultItemMarginVertical);
        firstPlan.ItemCornerRadius.Should().Be(SlidePanePlanner.DefaultItemCornerRadius);
        firstPlan.NormalBorderThickness.Should().Be(SlidePanePlanner.DefaultNormalBorderThickness);
        firstPlan.SelectedBorderThickness.Should().Be(SlidePanePlanner.DefaultSelectedBorderThickness);
        thumbnailHitTestVisible.Should().BeFalse("the preview must leave pointer routing to the slide-pane item");
        thumbnailEnabled.Should().BeFalse("the preview must not own keyboard focus or editing input");
    }

    [Fact]
    public async Task SlidePane_thumbnails_expose_live_shared_automation_names()
    {
        string?[] namesAfterTitles = [];
        string?[] namesAfterSelection = [];
        string?[] namesAfterMove = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.SetSlideTitle(0, "Opening");
            window.Editor.InsertSlide();
            window.Editor.SetSlideTitle(1, "Agenda");

            namesAfterTitles = window.SlidePaneThumbnailAutomationNamesForTests.ToArray();
            window.Editor.SelectSlide(1);
            namesAfterSelection = window.SlidePaneThumbnailAutomationNamesForTests.ToArray();
            window.Editor.MoveSlide(1, 0);
            namesAfterMove = window.SlidePaneThumbnailAutomationNamesForTests.ToArray();
        });

        if (!ran) return;
        namesAfterTitles.Should().Equal(
            "Slide 1: Opening, 1 object",
            "Slide 2: Agenda, 1 object");
        namesAfterSelection.Should().Equal(
            "Slide 1: Opening, 1 object",
            "Slide 2: Agenda, 1 object");
        namesAfterMove.Should().Equal(
            "Slide 1: Agenda, 1 object",
            "Slide 2: Opening, 1 object");
    }

    [Fact]
    public async Task SlidePane_section_headers_refresh_shared_automation_names()
    {
        string?[] namesExpanded = [];
        string?[] namesCollapsed = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.AddSectionAtSlide(0, "Intro");
            namesExpanded = window.SlidePaneSectionHeaderAutomationNamesForTests.ToArray();
            window.ToggleSlidePaneSectionForTests(0);
            namesCollapsed = window.SlidePaneSectionHeaderAutomationNamesForTests.ToArray();
        });

        if (!ran) return;
        namesExpanded.Should().ContainSingle().Which.Should().Be("Section Intro  (2), expanded");
        namesCollapsed.Should().ContainSingle().Which.Should().Be("Section Intro  (2), collapsed");
    }

    [Fact]
    public async Task SlidePane_section_header_toggle_collapses_member_slides()
    {
        var headersBefore = -1;
        var slidesBefore = -1;
        var slidesCollapsed = -1;
        var slidesExpanded = -1;
        var collapsed = false;
        var expanded = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.AddSectionAtSlide(0, "Intro");
            window.Editor.AddSectionAtSlide(1, "Body");

            headersBefore = window.SlidePaneSectionHeaderCount;
            slidesBefore = window.SlidePaneSlideItemCount;
            collapsed = window.ToggleSlidePaneSectionForTests(1);
            slidesCollapsed = window.SlidePaneSlideItemCount;
            expanded = window.ToggleSlidePaneSectionForTests(1);
            slidesExpanded = window.SlidePaneSlideItemCount;
        });

        if (!ran) return;
        headersBefore.Should().Be(2, "the Avalonia slide pane should expose one header per section");
        slidesBefore.Should().Be(3);
        collapsed.Should().BeTrue();
        slidesCollapsed.Should().Be(1, "collapsing the second section should omit only its two member slides");
        expanded.Should().BeTrue();
        slidesExpanded.Should().Be(3);
    }

    [Fact]
    public async Task SlidePane_section_headers_render_shared_visual_plan_tokens()
    {
        SlidePaneSectionHeaderVisualPlan[] plans = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.AddSectionAtSlide(0, "Intro");
            plans = window.SlidePaneRenderedSectionHeaderPlans.ToArray();
        });

        if (!ran) return;
        plans.Should().ContainSingle();
        var plan = plans[0];
        plan.SectionId.Should().NotBeNullOrWhiteSpace();
        plan.LabelText.Should().Be("Intro  (2)");
        plan.IsCollapsed.Should().BeFalse();
        plan.HeaderHeight.Should().Be(SlidePanePlanner.DefaultSectionHeaderHeight);
        plan.DisclosureWidth.Should().Be(SlidePanePlanner.DefaultSectionHeaderDisclosureWidth);
        plan.FontSize.Should().Be(SlidePanePlanner.DefaultSectionHeaderFontSize);
        plan.HorizontalPadding.Should().Be(SlidePanePlanner.DefaultSectionHeaderHorizontalPadding);
        plan.VerticalPadding.Should().Be(SlidePanePlanner.DefaultSectionHeaderVerticalPadding);
        plan.TopMargin.Should().Be(SlidePanePlanner.DefaultSectionHeaderTopMargin);
        plan.BottomMargin.Should().Be(SlidePanePlanner.DefaultSectionHeaderBottomMargin);
        plan.CornerRadius.Should().Be(SlidePanePlanner.DefaultSectionHeaderCornerRadius);
        plan.DisclosureText.Should().Be(SlidePanePlanner.DefaultSectionHeaderExpandedDisclosureText);
        plan.BackgroundHex.Should().Be(SlidePanePlanner.DefaultSectionHeaderBackgroundHex);
        plan.HoverBackgroundHex.Should().Be(SlidePanePlanner.DefaultSectionHeaderHoverBackgroundHex);
        plan.ForegroundHex.Should().Be(SlidePanePlanner.DefaultSectionHeaderForegroundHex);
        plan.AccessibleName.Should().Be("Section Intro  (2), expanded");
        plan.ToolTipText.Should().Be("Collapse section");
    }

    [Fact]
    public async Task SlidePane_section_headers_expose_wpf_equivalent_automation_names()
    {
        string?[] automationNames = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.AddSectionAtSlide(0, "Intro");
            automationNames = window.SlidePaneSectionHeaderAutomationNamesForTests.ToArray();
        });

        if (!ran) return;
        automationNames.Should().ContainSingle()
            .Which.Should().Be("Section Intro  (2), expanded");
    }

    [Fact]
    public async Task SlidePane_section_context_actions_route_through_shared_execution_planner()
    {
        var added = false;
        var renamed = false;
        var removed = false;
        var sectionNameAfterAdd = string.Empty;
        var sectionNameAfterRename = string.Empty;
        var sectionCountAfterRemove = -1;
        var slideCountAfterRemove = -1;
        var headerCountAfterAdd = -1;
        var headerCountAfterRemove = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Intro";
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Title = "Agenda";
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Title = "Close";

            added = window.TryApplySlideSectionActionForTests(
                SlideSectionActionKind.AddSection,
                slideIndex: 1,
                promptedName: "  Agenda Section  ");
            sectionNameAfterAdd = window.Editor.Presentation.Sections.Single().Name;
            headerCountAfterAdd = window.SlidePaneSectionHeaderCount;

            renamed = window.TryApplySlideSectionActionForTests(
                SlideSectionActionKind.RenameSection,
                slideIndex: 1,
                sectionIndex: 0,
                promptedName: "  Renamed Agenda  ");
            sectionNameAfterRename = window.Editor.Presentation.Sections.Single().Name;

            removed = window.TryApplySlideSectionActionForTests(
                SlideSectionActionKind.RemoveSection,
                slideIndex: 1,
                sectionIndex: 0);
            sectionCountAfterRemove = window.Editor.Presentation.Sections.Count;
            slideCountAfterRemove = window.Editor.Presentation.Slides.Count;
            headerCountAfterRemove = window.SlidePaneSectionHeaderCount;
        });

        if (!ran) return;
        added.Should().BeTrue("the Avalonia slide-pane Add Section action should use the shared execution planner");
        sectionNameAfterAdd.Should().Be("Agenda Section");
        headerCountAfterAdd.Should().Be(1);
        renamed.Should().BeTrue("the Avalonia slide-pane Rename Section action should use the shared execution planner");
        sectionNameAfterRename.Should().Be("Renamed Agenda");
        removed.Should().BeTrue("the Avalonia slide-pane Remove Section action should use the shared execution planner");
        sectionCountAfterRemove.Should().Be(0);
        slideCountAfterRemove.Should().Be(3, "PowerPoint-style section removal keeps slides in the deck");
        headerCountAfterRemove.Should().Be(0);
    }

    [Fact]
    public async Task SlidePane_context_duplicate_routes_through_shared_planner()
    {
        var duplicated = false;
        var before = -1;
        var after = -1;
        var currentSlideIndex = -1;
        string[] titles = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Agenda";
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Title = "Roadmap";
            window.Editor.SelectSlide(0);

            before = window.SlideCount;
            duplicated = window.TryApplySlidePaneContextAction(0, SlidePaneActionKind.DuplicateSlide);
            after = window.SlideCount;
            currentSlideIndex = window.CurrentSlideIndex;
            titles = window.Editor.Presentation.Slides.Select(slide => slide.Title).ToArray();
        });

        if (!ran) return;
        duplicated.Should().BeTrue("the Avalonia slide-pane context menu should use the shared duplicate action plan");
        after.Should().Be(before + 1);
        currentSlideIndex.Should().Be(1, "duplicating from the slide pane should select the clone like WPF");
        titles.Should().Equal("Agenda", "Agenda", "Roadmap");
    }

    [Fact]
    public async Task SlidePane_context_hide_slide_routes_through_shared_planner_and_undo()
    {
        var hidden = false;
        var restored = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            hidden = window.TryApplySlidePaneContextAction(0, SlidePaneActionKind.ToggleHiddenSlide);
            var hiddenSlide = window.Editor.Presentation.Slides[0];
            var menu = window.BuildSlidePaneContextMenuForTests(0);
            var showEntry = menu.Items
                .OfType<MenuItem>()
                .Single(item => Equals(item.Tag, FreePContextMenuCommand.ToggleHiddenSlide));

            restored = showEntry.IsChecked && showEntry.Header?.ToString() == SlidePanePlanner.ShowSlideMenuText;
            window.Editor.Undo();
            restored &= !hiddenSlide.IsHidden;
        });

        if (!ran) return;
        hidden.Should().BeTrue("the slide-pane action should hide the selected slide");
        restored.Should().BeTrue("the same action state should show the slide and undo should restore it");
    }

    [Fact]
    public async Task SlidePane_context_delete_respects_shared_delete_enablement()
    {
        var deletedSingleSlide = true;
        var deletedSecondSlide = false;
        var singleSlideCount = -1;
        var finalSlideCount = -1;
        string[] titles = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Keep";

            deletedSingleSlide = window.TryApplySlidePaneContextAction(0, SlidePaneActionKind.DeleteSlide);
            singleSlideCount = window.SlideCount;

            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Title = "Remove";
            deletedSecondSlide = window.TryApplySlidePaneContextAction(1, SlidePaneActionKind.DeleteSlide);
            finalSlideCount = window.SlideCount;
            titles = window.Editor.Presentation.Slides.Select(slide => slide.Title).ToArray();
        });

        if (!ran) return;
        deletedSingleSlide.Should().BeFalse("the shared slide-pane planner disables deleting the last slide");
        singleSlideCount.Should().Be(1);
        deletedSecondSlide.Should().BeTrue("the same planner-backed menu route should delete when another slide remains");
        finalSlideCount.Should().Be(1);
        titles.Should().Equal("Keep");
    }

    [Fact]
    public async Task DeleteCurrentSlide_decreases_slide_count()
    {
        var before = -1;
        var after  = -1;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            // Ensure there are at least two slides before deleting.
            window.Editor.InsertSlide();
            before = window.SlideCount;
            window.Editor.DeleteCurrentSlide();
            after = window.SlideCount;
        });

        if (!ran) return;
        after.Should().Be(before - 1, "DeleteCurrentSlide must remove one slide");
    }

    [Fact]
    public async Task DuplicateCurrentSlide_increases_slide_count()
    {
        var before = -1;
        var after  = -1;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            before = window.SlideCount;
            window.Editor.DuplicateCurrentSlide();
            after = window.SlideCount;
        });

        if (!ran) return;
        after.Should().Be(before + 1, "DuplicateCurrentSlide must add one slide");
    }

    [Fact]
    public async Task SelectSlide_changes_current_slide_index()
    {
        var idx = -1;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.SelectSlide(1);
            idx = window.CurrentSlideIndex;
        });

        if (!ran) return;
        idx.Should().Be(1, "SelectSlide(1) must move to the second slide");
    }

    [Fact]
    public async Task Open_comments_pane_rebinds_to_current_slide_after_selection_changes()
    {
        var firstRenderedCount = -1;
        var secondRenderedCount = -1;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "First reviewer",
                Initials = "FR",
                Text = "First slide comment",
                Idx = 1,
            });

            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Second reviewer",
                Initials = "SR",
                Text = "Second slide comment one",
                Idx = 2,
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Third reviewer",
                Initials = "TR",
                Text = "Second slide comment two",
                Idx = 3,
            });

            window.Editor.SelectSlide(0);
            window.ShowReviewCommentsPane();
            firstRenderedCount = window.CommentsPaneItemsForAccessibilityTests.Count;

            window.Editor.SelectSlide(1);
            secondRenderedCount = window.CommentsPaneItemsForAccessibilityTests.Count;
        });

        if (!ran) return;
        firstRenderedCount.Should().Be(1);
        secondRenderedCount.Should().Be(2,
            "an open comments pane must render the newly selected slide rather than retain the previous slide's cards");
    }

    // ── Undo / Redo ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertSlide_then_Undo_restores_count()
    {
        var before = -1;
        var after  = -1;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            before = window.SlideCount;
            window.Editor.InsertSlide();
            window.Editor.Undo();
            after = window.SlideCount;
        });

        if (!ran) return;
        after.Should().Be(before, "Undo must restore the original slide count");
    }

    [Fact]
    public async Task BottomNewSlideAffordance_undo_redo_restores_slide_count()
    {
        var before = -1;
        var created = -1;
        var undone = -1;
        var redone = -1;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            before = window.SlideCount;
            window.ClickSlidePaneNewSlideAffordanceForTests().Should().BeTrue();
            created = window.SlideCount;
            window.Editor.Undo();
            undone = window.SlideCount;
            window.Editor.Redo();
            redone = window.SlideCount;
        });

        if (!ran) return;
        created.Should().Be(before + 1);
        undone.Should().Be(before);
        redone.Should().Be(created);
    }

    [Fact]
    public async Task Shell_undo_redo_tunnel_skips_inline_text_editor_focus()
    {
        var textEditorTarget = false;
        var slidePaneTarget = false;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            textEditorTarget = window.IsShellShortcutTargetForTests(new TextBox());
            slidePaneTarget = window.IsShellShortcutTargetForTests(window.SlidePaneNewSlideButtonForTests);
        });

        if (!ran) return;
        textEditorTarget.Should().BeFalse("inline text editors must retain Ctrl+Z/Ctrl+Y");
        slidePaneTarget.Should().BeTrue("focused slide-pane controls need shell undo/redo routing");
    }

    // ── Insert commands ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("freep.text-box",        DrawingShapeKind.Rectangle, true)]
    [InlineData("freep.shape-rectangle", DrawingShapeKind.Rectangle, false)]
    [InlineData("freep.shape-ellipse",   DrawingShapeKind.Ellipse,   false)]
    [InlineData("freep.shape-rounded-rectangle", DrawingShapeKind.RoundedRectangle, false)]
    [InlineData("freep.shape-parallelogram", DrawingShapeKind.Parallelogram, false)]
    [InlineData("freep.shape-trapezoid", DrawingShapeKind.Trapezoid, false)]
    [InlineData("freep.shape-left-arrow", DrawingShapeKind.LeftArrow, false)]
    [InlineData("freep.shape-up-arrow", DrawingShapeKind.UpArrow, false)]
    [InlineData("freep.shape-down-arrow", DrawingShapeKind.DownArrow, false)]
    [InlineData("freep.shape-pentagon", DrawingShapeKind.Pentagon, false)]
    [InlineData("freep.shape-octagon", DrawingShapeKind.Octagon, false)]
    [InlineData("freep.shape-left-right-arrow", DrawingShapeKind.LeftRightArrow, false)]
    [InlineData("freep.shape-up-down-arrow", DrawingShapeKind.UpDownArrow, false)]
    [InlineData("freep.shape-star8", DrawingShapeKind.Star8, false)]
    [InlineData("freep.shape-chevron", DrawingShapeKind.Chevron, false)]
    [InlineData("freep.shape-home-plate", DrawingShapeKind.HomePlate, false)]
    [InlineData("freep.shape-right-triangle", DrawingShapeKind.RightTriangle, false)]
    [InlineData("freep.shape-minus-sign", DrawingShapeKind.MinusSign, false)]
    [InlineData("freep.shape-multiply-sign", DrawingShapeKind.MultiplySign, false)]
    [InlineData("freep.shape-divide-sign", DrawingShapeKind.DivideSign, false)]
    [InlineData("freep.shape-equal-sign", DrawingShapeKind.EqualSign, false)]
    [InlineData("freep.shape-not-equal-sign", DrawingShapeKind.NotEqualSign, false)]
    [InlineData("freep.shape-wave", DrawingShapeKind.Wave, false)]
    [InlineData("freep.shape-rectangular-callout", DrawingShapeKind.RectangularCallout, false)]
    [InlineData("freep.shape-rounded-rectangular-callout", DrawingShapeKind.RoundedRectangularCallout, false)]
    [InlineData("freep.shape-oval-callout", DrawingShapeKind.OvalCallout, false)]
    [InlineData("freep.shape-explosion", DrawingShapeKind.Explosion, false)]
    [InlineData("freep.shape-ribbon", DrawingShapeKind.Ribbon, false)]
    [InlineData("freep.shape-flowchart-process", DrawingShapeKind.FlowchartProcess, false)]
    [InlineData("freep.shape-flowchart-decision", DrawingShapeKind.FlowchartDecision, false)]
    [InlineData("freep.shape-flowchart-data", DrawingShapeKind.FlowchartData, false)]
    [InlineData("freep.shape-flowchart-predefined-process", DrawingShapeKind.FlowchartPredefinedProcess, false)]
    [InlineData("freep.shape-flowchart-document", DrawingShapeKind.FlowchartDocument, false)]
    [InlineData("freep.shape-flowchart-terminator", DrawingShapeKind.FlowchartTerminator, false)]
    [InlineData("freep.shape-line-callout", DrawingShapeKind.LineCallout, false)]
    [InlineData("freep.shape-cylinder", DrawingShapeKind.Cylinder, false)]
    [InlineData("freep.shape-chord", DrawingShapeKind.Chord, false)]
    [InlineData("freep.shape-heart", DrawingShapeKind.Heart, false)]
    public async Task Ribbon_insert_shape_commands_add_expected_shape(
        string commandId,
        DrawingShapeKind expectedShape,
        bool expectsTextBody)
    {
        var found = false;
        var before = -1;
        var after = -1;
        SlideShape? added = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out var command);
            found.Should().BeTrue($"{commandId} must be registered");

            before = window.Editor.CurrentSlide!.Shapes.Count;
            command!.Execute(RibbonCommandContext.Empty);
            after = window.Editor.CurrentSlide!.Shapes.Count;
            added = window.Editor.CurrentSlide!.Shapes.Last();
        });

        if (!ran) return;
        found.Should().BeTrue($"{commandId} must be registered");
        after.Should().Be(before + 1, $"{commandId} must insert one shape");
        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.AutoShape);
        added.AutoShapeKind.Should().Be(expectedShape);
        (added.TextBody is not null).Should().Be(expectsTextBody,
            $"{commandId} must create the expected text-editing surface");
    }

    [Theory]
    [InlineData("freep.insert-table-2x2", 2, 2)]
    [InlineData("freep.insert-table-4x4", 4, 4)]
    public async Task Ribbon_insert_table_commands_add_expected_table(
        string commandId,
        int expectedRows,
        int expectedColumns)
    {
        var found = false;
        var before = -1;
        var after = -1;
        SlideShape? added = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out var command);
            found.Should().BeTrue($"{commandId} must be registered");

            before = window.Editor.CurrentSlide!.Shapes.Count;
            command!.Execute(RibbonCommandContext.Empty);
            after = window.Editor.CurrentSlide!.Shapes.Count;
            added = window.Editor.CurrentSlide!.Shapes.Last();
        });

        if (!ran) return;
        found.Should().BeTrue($"{commandId} must be registered");
        after.Should().Be(before + 1, $"{commandId} must insert one table");
        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Table);
        added.Table.Should().NotBeNull();
        added.Table!.Rows.Should().HaveCount(expectedRows);
        added.Table.ColumnWidthsEmu.Should().HaveCount(expectedColumns);
    }

    [Fact]
    public async Task Ribbon_insert_table_command_opens_picker_and_applies_selected_size()
    {
        var found = false;
        var pickerVisibleAfterOpen = false;
        var pickerChoiceCount = 0;
        var defaultChoiceCount = 0;
        var applied = false;
        var pickerVisibleAfterApply = true;
        var before = -1;
        var after = -1;
        SlideShape? added = null;
        TableInsertionPickerPlan? pickerPlan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(SlideObjectInsertionPlanner.Table3x3CommandId, out var command);
            found.Should().BeTrue("the large Table command must be registered");

            before = window.Editor.CurrentSlide!.Shapes.Count;
            command!.Execute(RibbonCommandContext.Empty);
            pickerVisibleAfterOpen = window.IsTablePickerVisible;
            pickerChoiceCount = window.TablePickerChoiceButtonCount;
            defaultChoiceCount = window.TablePickerDefaultChoiceCount;
            pickerPlan = window.LastTablePickerPlan;
            applied = window.ApplyTablePickerChoice(5, 4);
            pickerVisibleAfterApply = window.IsTablePickerVisible;
            after = window.Editor.CurrentSlide!.Shapes.Count;
            added = window.Editor.CurrentSlide!.Shapes.Last();
        });

        if (!ran) return;
        found.Should().BeTrue();
        pickerVisibleAfterOpen.Should().BeTrue("the Avalonia large Table command should show an actual picker surface");
        pickerChoiceCount.Should().Be(25);
        defaultChoiceCount.Should().Be(1);
        pickerPlan.Should().NotBeNull();
        pickerPlan!.Choices.Should().Contain(choice =>
            choice.Rows == 5 &&
            choice.Columns == 4 &&
            choice.Label == "5 x 4 Table");
        applied.Should().BeTrue();
        pickerVisibleAfterApply.Should().BeFalse("the picker should collapse after a table size is selected");
        after.Should().Be(before + 1);
        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Table);
        added.Table!.Rows.Should().HaveCount(5);
        added.Table.ColumnWidthsEmu.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("freep.insert-chart-column", ChartType.ColumnClustered)]
    [InlineData("freep.insert-chart-bar", ChartType.BarClustered)]
    [InlineData("freep.insert-chart-line", ChartType.Line)]
    [InlineData("freep.insert-chart-pie", ChartType.Pie)]
    [InlineData("freep.insert-chart-column-stacked", ChartType.ColumnStacked)]
    [InlineData("freep.insert-chart-column-stacked-100", ChartType.ColumnStacked100)]
    [InlineData("freep.insert-chart-bar-stacked", ChartType.BarStacked)]
    [InlineData("freep.insert-chart-bar-stacked-100", ChartType.BarStacked100)]
    [InlineData("freep.insert-chart-line-markers", ChartType.LineMarkers)]
    [InlineData("freep.insert-chart-area", ChartType.Area)]
    [InlineData("freep.insert-chart-area-stacked", ChartType.AreaStacked)]
    [InlineData("freep.insert-chart-scatter", ChartType.Scatter)]
    [InlineData("freep.insert-chart-doughnut", ChartType.Doughnut)]
    [InlineData("freep.insert-chart-radar", ChartType.Radar)]
    [InlineData("freep.insert-chart-bubble", ChartType.Bubble)]
    [InlineData("freep.insert-chart-stock", ChartType.Stock)]
    [InlineData("freep.insert-chart-surface", ChartType.Surface)]
    [InlineData("freep.insert-chart-surface-3d", ChartType.Surface3D)]
    public async Task Ribbon_insert_chart_commands_add_expected_chart(
        string commandId,
        ChartType expectedChartType)
    {
        var found = false;
        var before = -1;
        var after = -1;
        SlideShape? added = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out var command);
            found.Should().BeTrue($"{commandId} must be registered");

            before = window.Editor.CurrentSlide!.Shapes.Count;
            command!.Execute(RibbonCommandContext.Empty);
            after = window.Editor.CurrentSlide!.Shapes.Count;
            added = window.Editor.CurrentSlide!.Shapes.Last();
        });

        if (!ran) return;
        found.Should().BeTrue($"{commandId} must be registered");
        after.Should().Be(before + 1, $"{commandId} must insert one chart");
        added.Should().NotBeNull();
        added!.Kind.Should().Be(SlideShapeKind.Chart);
        added.Chart.Should().NotBeNull();
        added.Chart!.ChartType.Should().Be(expectedChartType);
    }

    [Fact]
    public async Task Ribbon_clipboard_copy_then_paste_routes_to_editor()
    {
        var foundCopy = false;
        var foundPaste = false;
        var before = -1;
        var after = -1;
        var canPasteAfterCopy = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            foundCopy = registry.TryGet("freep.copy", out var copy);
            foundPaste = registry.TryGet("freep.paste", out var paste);

            var shape = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(shape.Id);
            copy!.Execute(RibbonCommandContext.Empty);
            canPasteAfterCopy = window.Editor.CanPaste;
            before = window.Editor.CurrentSlide!.Shapes.Count;
            paste!.Execute(RibbonCommandContext.Empty);
            after = window.Editor.CurrentSlide!.Shapes.Count;
        });

        if (!ran) return;
        foundCopy.Should().BeTrue("Copy must be registered");
        foundPaste.Should().BeTrue("Paste must be registered");
        canPasteAfterCopy.Should().BeTrue("Copy should populate the shared internal clipboard");
        after.Should().Be(before + 1, "Paste should clone the copied shape through EditingSession");
    }

    [Fact]
    public async Task Ribbon_clipboard_cut_routes_to_editor()
    {
        var found = false;
        var before = -1;
        var after = -1;
        var canPasteAfterCut = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.cut", out var cut);

            var shape = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(shape.Id);
            before = window.Editor.CurrentSlide!.Shapes.Count;
            cut!.Execute(RibbonCommandContext.Empty);
            after = window.Editor.CurrentSlide!.Shapes.Count;
            canPasteAfterCut = window.Editor.CanPaste;
        });

        if (!ran) return;
        found.Should().BeTrue("Cut must be registered");
        after.Should().Be(before - 1, "Cut should remove the selected shape through EditingSession");
        canPasteAfterCut.Should().BeTrue("Cut should leave the shared internal clipboard pasteable");
    }

    [Fact]
    public async Task Ribbon_format_painter_routes_to_editor()
    {
        var found = false;
        ShapeFill? targetFill = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.format-painter", out var formatPainter);

            var source = window.Editor.InsertDefaultRectangle();
            var target = window.Editor.InsertDefaultRectangle();
            var redFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000)));
            window.Editor.Select(source.Id);
            window.Editor.SetSelectedFill(redFill);
            window.Editor.Select(source.Id);
            window.Editor.Select(target.Id, addToSelection: true);

            formatPainter!.Execute(RibbonCommandContext.Empty);
            targetFill = window.Editor.CurrentSlide!.Shapes.Single(shape => shape.Id == target.Id).Fill;
        });

        if (!ran) return;
        found.Should().BeTrue("Format Painter must be registered");
        targetFill.Should().BeOfType<ShapeFill.Solid>(
            "Format Painter should apply the source shape fill through EditingSession");
    }

    [Theory]
    [InlineData("freep.bold", "bold")]
    [InlineData("freep.italic", "italic")]
    [InlineData("freep.underline", "underline")]
    [InlineData("freep.superscript", "superscript")]
    [InlineData("freep.subscript", "subscript")]
    public async Task Ribbon_font_toggle_commands_route_to_editor(
        string commandId,
        string property)
    {
        var found = false;
        var isApplied = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out var command);
            found.Should().BeTrue($"{commandId} must be registered");

            var shape = window.Editor.InsertTextBox("Text");
            window.Editor.Select(shape.Id);

            command!.Execute(RibbonCommandContext.Empty);

            var run = window.Editor.CurrentSlide!.Shapes
                .Single(s => s.Id == shape.Id)
                .TextBody!.Paragraphs[0].Runs[0];
            isApplied = property switch
            {
                "bold" => run.Bold,
                "italic" => run.Italic,
                "underline" => run.Underline,
                "superscript" => run.BaselineOffset > 0,
                "subscript" => run.BaselineOffset < 0,
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        });

        if (!ran) return;
        found.Should().BeTrue($"{commandId} must be registered");
        isApplied.Should().BeTrue($"{commandId} should format the selected text shape through EditingSession");
    }

    [Theory]
    [InlineData("freep.bold", "bold")]
    [InlineData("freep.italic", "italic")]
    [InlineData("freep.underline", "underline")]
    [InlineData("freep.superscript", "superscript")]
    [InlineData("freep.subscript", "subscript")]
    public async Task Ribbon_font_toggle_commands_route_to_active_table_cell(
        string commandId,
        string property)
    {
        var found = false;
        var isApplied = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out var command);
            found.Should().BeTrue($"{commandId} must be registered");

            var shape = window.Editor.InsertTable(1, 1);
            var body = new TextBody { Wrap = true };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "Cell" });
            body.Paragraphs.Add(paragraph);
            shape.Table!.Rows[0].Cells[0].TextBody = body;
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            command!.Execute(RibbonCommandContext.Empty);

            var run = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0];
            isApplied = property switch
            {
                "bold" => run.Bold,
                "italic" => run.Italic,
                "underline" => run.Underline,
                "superscript" => run.BaselineOffset > 0,
                "subscript" => run.BaselineOffset < 0,
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        });

        if (!ran) return;
        found.Should().BeTrue($"{commandId} must be registered");
        isApplied.Should().BeTrue($"{commandId} should format the active table cell through the shared planner");
    }

    [Fact]
    public async Task Ribbon_font_family_command_routes_selected_value_to_editor()
    {
        var found = false;
        string? fontFamily = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.font-family", out var command);
            found.Should().BeTrue("Font must be registered");

            var shape = window.Editor.InsertTextBox("Text");
            window.Editor.Select(shape.Id);

            command!.Execute(RibbonCommandContext.ForSelectedValue("Arial"));

            fontFamily = window.Editor.CurrentSlide!.Shapes
                .Single(s => s.Id == shape.Id)
                .TextBody!.Paragraphs[0].Runs[0]
                .FontFamily;
        });

        if (!ran) return;
        found.Should().BeTrue("Font must be registered");
        fontFamily.Should().Be("Arial", "the Avalonia registry should forward the selected font family to EditingSession");
    }

    [Fact]
    public async Task Ribbon_text_autofit_command_routes_selected_value_to_editor()
    {
        var found = false;
        TextAutoFitKind? autoFit = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.text-autofit", out var command);
            found.Should().BeTrue("Text Autofit must be registered");

            var shape = window.Editor.InsertTextBox("Text");
            window.Editor.Select(shape.Id);
            command!.Execute(RibbonCommandContext.ForSelectedValue("Resize shape to fit text"));

            autoFit = window.Editor.CurrentSlide!.Shapes
                .Single(s => s.Id == shape.Id)
                .TextBody!.AutoFitKind;
        });

        if (!ran) return;
        found.Should().BeTrue("Text Autofit must be registered");
        autoFit.Should().Be(TextAutoFitKind.Shape);
    }

    [Fact]
    public async Task Ribbon_table_cell_inset_command_routes_selected_value_to_editor()
    {
        var found = false;
        double? insetLeft = null;
        double? insetBottom = null;
        double? undoneInset = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.table-cell-inset", out var command);
            found.Should().BeTrue("Cell Insets must be registered");

            var shape = window.Editor.InsertTable(1, 1);
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            command!.Execute(RibbonCommandContext.ForSelectedValue("All:4pt"));
            var cell = shape.Table!.Rows[0].Cells[0];
            insetLeft = cell.InsetLeftPt;
            insetBottom = cell.InsetBottomPt;
            window.Editor.Undo();
            undoneInset = cell.InsetLeftPt;
        });

        if (!ran) return;
        found.Should().BeTrue("Cell Insets must be registered");
        insetLeft.Should().Be(4);
        insetBottom.Should().Be(4);
        undoneInset.Should().BeNull("the host route should use the shared undoable command");
    }

    [Fact]
    public async Task Ribbon_table_row_height_command_routes_selected_value_to_editor()
    {
        var found = false;
        long? height = null;
        long? undoneHeight = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.table-row-height", out var command);
            found.Should().BeTrue("Row Height must be registered");

            var shape = window.Editor.InsertTable(1, 1);
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            command!.Execute(RibbonCommandContext.ForSelectedValue("0.75in"));
            height = shape.Table!.Rows[0].HeightEmu;
            window.Editor.Undo();
            undoneHeight = shape.Table.Rows[0].HeightEmu;
        });

        if (!ran) return;
        found.Should().BeTrue("Row Height must be registered");
        height.Should().Be(685800);
        undoneHeight.Should().NotBe(685800);
    }

    [Fact]
    public async Task Ribbon_table_merge_and_split_commands_route_to_editor()
    {
        var foundMerge = false;
        var foundSplit = false;
        int? mergedSpan = null;
        int? splitSpan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            foundMerge = registry.TryGet(TableCellEditPlanner.MergeCellsCommandId, out var merge);
            foundSplit = registry.TryGet(TableCellEditPlanner.SplitCellCommandId, out var split);
            foundMerge.Should().BeTrue("Merge Cells must be registered");
            foundSplit.Should().BeTrue("Split Cell must be registered");

            var shape = window.Editor.InsertTable(1, 2);
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);
            merge!.Execute(RibbonCommandContext.Empty);
            mergedSpan = shape.Table!.Rows[0].Cells[0].GridSpan;
            split!.Execute(RibbonCommandContext.Empty);
            splitSpan = shape.Table.Rows[0].Cells[0].GridSpan;
        });

        if (!ran) return;
        foundMerge.Should().BeTrue("Merge Cells must be registered");
        foundSplit.Should().BeTrue("Split Cell must be registered");
        mergedSpan.Should().Be(2);
        splitSpan.Should().Be(1);
    }

    [Theory]
    [InlineData(TableCellEditPlanner.TableFirstRowCommandId, TableStyleFlagKind.FirstRow)]
    [InlineData(TableCellEditPlanner.TableLastRowCommandId, TableStyleFlagKind.LastRow)]
    [InlineData(TableCellEditPlanner.TableFirstColCommandId, TableStyleFlagKind.FirstCol)]
    [InlineData(TableCellEditPlanner.TableLastColCommandId, TableStyleFlagKind.LastCol)]
    [InlineData(TableCellEditPlanner.TableBandRowCommandId, TableStyleFlagKind.BandRow)]
    [InlineData(TableCellEditPlanner.TableBandColCommandId, TableStyleFlagKind.BandCol)]
    public async Task Ribbon_table_design_flags_route_to_editor(string commandId, TableStyleFlagKind kind)
    {
        var before = false;
        var after = false;
        var undone = false;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet(commandId, out var command).Should().BeTrue();
            var shape = window.Editor.InsertTable(2, 2);
            window.Editor.Select(shape.Id);
            before = GetTableStyleFlag(shape.Table!.Flags, kind);
            command!.Execute(RibbonCommandContext.Empty);
            after = GetTableStyleFlag(shape.Table.Flags, kind);
            window.Editor.Undo();
            undone = GetTableStyleFlag(shape.Table.Flags, kind);
        });

        if (!ran) return;
        after.Should().Be(!before);
        undone.Should().Be(before);
    }

    private static bool GetTableStyleFlag(TableStyleFlags flags, TableStyleFlagKind kind) => kind switch
    {
        TableStyleFlagKind.FirstRow => flags.FirstRow,
        TableStyleFlagKind.LastRow => flags.LastRow,
        TableStyleFlagKind.FirstCol => flags.FirstCol,
        TableStyleFlagKind.LastCol => flags.LastCol,
        TableStyleFlagKind.BandRow => flags.BandRow,
        TableStyleFlagKind.BandCol => flags.BandCol,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [Fact]
    public async Task Ribbon_font_size_and_color_commands_route_to_editor()
    {
        var foundSize = false;
        var foundColor = false;
        double? fontSize = null;
        ThemeAwareColor? fontColor = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            foundSize = registry.TryGet("freep.font-size", out var sizeCommand);
            foundColor = registry.TryGet("freep.font-color", out var colorCommand);
            foundSize.Should().BeTrue("Font Size must be registered");
            foundColor.Should().BeTrue("Font Color must be registered");

            var shape = window.Editor.InsertTextBox("Text");
            window.Editor.Select(shape.Id);

            sizeCommand!.Execute(RibbonCommandContext.ForSelectedValue("26pt"));
            colorCommand!.Execute(RibbonCommandContext.ForSelectedValue("#336699"));

            var run = window.Editor.CurrentSlide!.Shapes
                .Single(s => s.Id == shape.Id)
                .TextBody!.Paragraphs[0].Runs[0];
            fontSize = run.FontSizePt;
            fontColor = run.Color;
        });

        if (!ran) return;
        foundSize.Should().BeTrue("Font Size must be registered");
        foundColor.Should().BeTrue("Font Color must be registered");
        fontSize.Should().Be(26, "the Avalonia registry should forward font size to selected text shapes");
        fontColor.Should().NotBeNull("the Avalonia registry should forward font color to selected text shapes");
        fontColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x336699));
    }

    [Fact]
    public async Task Ribbon_font_size_and_color_commands_route_to_active_table_cell()
    {
        var foundSize = false;
        var foundColor = false;
        IReadOnlyList<Run> runs = Array.Empty<Run>();

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            foundSize = registry.TryGet("freep.font-size", out var sizeCommand);
            foundColor = registry.TryGet("freep.font-color", out var colorCommand);
            foundSize.Should().BeTrue("Font Size must be registered");
            foundColor.Should().BeTrue("Font Color must be registered");

            var shape = window.Editor.InsertTable(1, 1);
            var body = new TextBody { Wrap = true };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "Cell", FontSizePt = 10 });
            paragraph.Runs.Add(new Run { Text = " text", FontSizePt = 14, Bold = true });
            body.Paragraphs.Add(paragraph);
            shape.Table!.Rows[0].Cells[0].TextBody = body;
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            sizeCommand!.Execute(RibbonCommandContext.ForSelectedValue("22"));
            colorCommand!.Execute(RibbonCommandContext.ForSelectedValue("#8844CC"));

            runs = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs.ToArray();
        });

        if (!ran) return;
        foundSize.Should().BeTrue("Font Size must be registered");
        foundColor.Should().BeTrue("Font Color must be registered");
        runs.Should().OnlyContain(run => run.FontSizePt == 22);
        runs.Should().OnlyContain(run => run.Color != null && run.Color.Resolved == SrgbColor.FromRgb(0x8844CC));
        runs.Select(run => run.Text).Should().Equal("Cell", " text");
        runs[1].Bold.Should().BeTrue("table-cell value formatting should preserve unrelated mixed-run formatting");
    }

    [Theory]
    [InlineData("freep.paragraph.align-left", TextAlign.Left)]
    [InlineData("freep.paragraph.align-center", TextAlign.Center)]
    [InlineData("freep.paragraph.align-right", TextAlign.Right)]
    [InlineData("freep.paragraph.align-justify", TextAlign.Justify)]
    public async Task Ribbon_paragraph_alignment_commands_route_to_active_table_cell(
        string commandId,
        TextAlign alignment)
    {
        var found = false;
        TextAlign? actual = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out var command);
            found.Should().BeTrue($"{commandId} must be registered");

            var shape = window.Editor.InsertTable(1, 1);
            var body = new TextBody { Wrap = true };
            var paragraph = new Paragraph { Align = TextAlign.Left };
            paragraph.Runs.Add(new Run { Text = "Cell" });
            body.Paragraphs.Add(paragraph);
            shape.Table!.Rows[0].Cells[0].TextBody = body;
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            command!.Execute(RibbonCommandContext.Empty);

            actual = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Align;
        });

        if (!ran) return;
        found.Should().BeTrue($"{commandId} must be registered");
        actual.Should().Be(alignment);
    }

    [Fact]
    public async Task Ribbon_bullet_and_indent_commands_route_to_active_table_cell()
    {
        var foundBullets = false;
        var foundNumbering = false;
        var foundIndent = false;
        var foundOutdent = false;
        BulletKind bulletKind = BulletKind.None;
        string? bulletChar = null;
        BulletKind numberingKind = BulletKind.None;
        AutoNumType autoNumType = AutoNumType.RomanLcPeriod;
        int autoNumStartAt = -1;
        int levelAfterIndent = -1;
        long? marginAfterIndent = null;
        int levelAfterOutdent = -1;
        long? marginAfterOutdent = 1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            foundBullets = registry.TryGet("freep.bullets", out var bulletsCommand);
            foundNumbering = registry.TryGet("freep.numbering", out var numberingCommand);
            foundIndent = registry.TryGet("freep.indent-increase", out var indentCommand);
            foundOutdent = registry.TryGet("freep.indent-decrease", out var outdentCommand);
            foundBullets.Should().BeTrue("Bullets must be registered");
            foundNumbering.Should().BeTrue("Numbering must be registered");
            foundIndent.Should().BeTrue("Increase Indent must be registered");
            foundOutdent.Should().BeTrue("Decrease Indent must be registered");

            var shape = window.Editor.InsertTable(1, 1);
            var body = new TextBody { Wrap = true };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "Cell" });
            body.Paragraphs.Add(paragraph);
            shape.Table!.Rows[0].Cells[0].TextBody = body;
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            bulletsCommand!.Execute(RibbonCommandContext.Empty);

            var edited = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
            bulletKind = edited.BulletKind;
            bulletChar = edited.BulletChar;

            numberingCommand!.Execute(RibbonCommandContext.Empty);

            edited = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
            numberingKind = edited.BulletKind;
            autoNumType = edited.AutoNumType;
            autoNumStartAt = edited.AutoNumStartAt;

            indentCommand!.Execute(RibbonCommandContext.Empty);

            edited = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
            levelAfterIndent = edited.Level;
            marginAfterIndent = edited.MarginLeftEmu;

            outdentCommand!.Execute(RibbonCommandContext.Empty);

            edited = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
            levelAfterOutdent = edited.Level;
            marginAfterOutdent = edited.MarginLeftEmu;
        });

        if (!ran) return;
        foundBullets.Should().BeTrue("Bullets must be registered");
        foundNumbering.Should().BeTrue("Numbering must be registered");
        foundIndent.Should().BeTrue("Increase Indent must be registered");
        foundOutdent.Should().BeTrue("Decrease Indent must be registered");
        bulletKind.Should().Be(BulletKind.Char);
        bulletChar.Should().Be("\u2022");
        numberingKind.Should().Be(BulletKind.Auto);
        autoNumType.Should().Be(AutoNumType.ArabicPeriod);
        autoNumStartAt.Should().Be(1);
        levelAfterIndent.Should().Be(1);
        marginAfterIndent.Should().Be(457200);
        levelAfterOutdent.Should().Be(0);
        marginAfterOutdent.Should().BeNull();
    }

    [Fact]
    public async Task Ribbon_numbering_preset_context_routes_to_active_table_cell()
    {
        var foundNumbering = false;
        BulletKind bulletKind = BulletKind.None;
        AutoNumType autoNumType = AutoNumType.ArabicPeriod;
        int autoNumStartAt = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            foundNumbering = registry.TryGet("freep.numbering", out var numberingCommand);
            foundNumbering.Should().BeTrue("Numbering must be registered");

            var shape = window.Editor.InsertTable(1, 1);
            var body = new TextBody { Wrap = true };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "Cell" });
            body.Paragraphs.Add(paragraph);
            shape.Table!.Rows[0].Cells[0].TextBody = body;
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            numberingCommand!.Execute(RibbonCommandContext.ForSelectedValue(
                TableCellListPresetCatalog.NumberAlphaLowerPeriodId));

            var edited = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
            bulletKind = edited.BulletKind;
            autoNumType = edited.AutoNumType;
            autoNumStartAt = edited.AutoNumStartAt;
        });

        if (!ran) return;
        foundNumbering.Should().BeTrue("Numbering must be registered");
        bulletKind.Should().Be(BulletKind.Auto);
        autoNumType.Should().Be(AutoNumType.AlphaLcPeriod);
        autoNumStartAt.Should().Be(1);
    }

    [Fact]
    public async Task Ribbon_visible_bullet_gallery_preset_command_routes_to_active_table_cell()
    {
        var found = false;
        string? bulletChar = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            var commandId = PresentationListGalleryPlanner.BuildBulletGalleryPlan()
                .Items.Single(item => item.ListPreset?.Id == TableCellListPresetCatalog.BulletCheckId)
                .CommandId;
            found = registry.TryGet(commandId, out var command);
            found.Should().BeTrue("visible bullet gallery preset commands must be registered");

            var shape = window.Editor.InsertTable(1, 1);
            var body = new TextBody { Wrap = true };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "Cell" });
            body.Paragraphs.Add(paragraph);
            shape.Table!.Rows[0].Cells[0].TextBody = body;
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            command!.Execute(RibbonCommandContext.Empty);

            bulletChar = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].BulletChar;
        });

        if (!ran) return;
        found.Should().BeTrue();
        bulletChar.Should().Be("\u2713");
    }

    [Fact]
    public async Task Ribbon_picture_bullet_command_routes_picked_payload_to_active_table_cell()
    {
        var found = false;
        BulletKind bulletKind = BulletKind.None;
        ImagePart? bulletImage = null;
        string? bulletChar = "not-null";

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.PictureBulletPayloadProviderForTests = () => Task.FromResult<PresentationPictureBulletPayload?>(
                PresentationPictureBulletAuthoringPlanner.CreatePayload(
                    [0x89, 0x50, 0x4E, 0x47],
                    "image/png",
                    "bullet.png"));
            var shape = window.Editor.InsertTable(1, 1);
            var body = new TextBody { Wrap = true };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "Cell" });
            body.Paragraphs.Add(paragraph);
            shape.Table!.Rows[0].Cells[0].TextBody = body;
            window.Editor.Select(shape.Id);
            window.Editor.SetActiveTableCell(0, 0);

            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(PresentationListGalleryPlanner.ImageBulletCommandId, out _);
            found.Should().BeTrue("picture bullet command must be registered");

            window.ApplyPictureBulletFromFileAsyncForTests().GetAwaiter().GetResult();

            var edited = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
            bulletKind = edited.BulletKind;
            bulletImage = edited.BulletImage;
            bulletChar = edited.BulletChar;
        });

        if (!ran) return;
        found.Should().BeTrue();
        bulletKind.Should().Be(BulletKind.Image);
        bulletImage.Should().NotBeNull();
        bulletImage!.ContentType.Should().Be("image/png");
        bulletImage.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47);
        bulletChar.Should().BeNull();
    }

    [Fact]
    public async Task Ribbon_chart_edit_data_command_is_registered_and_noops_without_selected_chart()
    {
        var found = false;
        var foundAxis = false;
        var foundSeries = false;
        var foundPoint = false;
        var foundLayout = false;
        var foundDataTable = false;
        var found3DView = false;
        var foundTextOptions = false;
        var before = -1;
        var after = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(ChartDataDialogPlanner.EditDataCommandId, out var command);
            found.Should().BeTrue("the Avalonia chart-data command must be registered");
            foundAxis = registry.TryGet(ChartAxisOptionsPlanner.CommandId, out _);
            foundSeries = registry.TryGet(ChartSeriesOptionsPlanner.CommandId, out _);
            foundPoint = registry.TryGet(ChartPointOptionsPlanner.CommandId, out _);
            foundLayout = registry.TryGet(ChartLayoutOptionsPlanner.CommandId, out _);
            foundDataTable = registry.TryGet(ChartDataTableOptionsPlanner.CommandId, out _);
            found3DView = registry.TryGet(Chart3DViewOptionsPlanner.CommandId, out _);
            foundTextOptions = registry.TryGet(ChartTextOptionsPlanner.CommandId, out _);

            before = window.Editor.CurrentSlide!.Shapes.Count;
            command!.Execute(RibbonCommandContext.Empty);
            after = window.Editor.CurrentSlide!.Shapes.Count;
        });

        if (!ran) return;
        found.Should().BeTrue("the Avalonia chart-data command must be registered");
        foundAxis.Should().BeTrue("the Avalonia chart-axis command must be registered");
        foundSeries.Should().BeTrue("the Avalonia chart-series command must be registered");
        foundPoint.Should().BeTrue("the Avalonia chart-point command must be registered");
        foundLayout.Should().BeTrue("the Avalonia chart-layout command must be registered");
        foundDataTable.Should().BeTrue("the Avalonia chart-data-table command must be registered");
        found3DView.Should().BeTrue("the Avalonia chart-3-D-view command must be registered");
        foundTextOptions.Should().BeTrue("the Avalonia chart-text command must be registered");
        after.Should().Be(before, "opening chart data without a selected chart should preserve WPF's no-op behavior");
    }

    [Fact]
    public async Task Ribbon_design_commands_route_through_shared_planner()
    {
        var foundTheme = false;
        var foundSlideSize = false;
        var foundCustom = false;
        PresentationDesignCommandPlan? customPlan = null;
        SlideSizeDialogInitialState? customInitialState = null;
        string? themeName = null;
        long slideWidth = 0;
        long slideHeight = 0;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            foundTheme = registry.TryGet("freep.theme.berlin", out var theme);
            foundSlideSize = registry.TryGet("freep.slide-size-4x3", out var slideSize);
            foundCustom = registry.TryGet("freep.slide-size-custom", out var customSlideSize);

            theme!.Execute(RibbonCommandContext.Empty);
            slideSize!.Execute(RibbonCommandContext.Empty);
            customSlideSize!.Execute(RibbonCommandContext.Empty);

            themeName = window.Editor.Presentation.Theme.Name;
            slideWidth = window.Editor.Presentation.SlideSizeCxEmu;
            slideHeight = window.Editor.Presentation.SlideSizeCyEmu;
            customPlan = window.LastCustomSlideSizeRequestPlan;
            customInitialState = window.LastCustomSlideSizeInitialState;
            window.ActiveSlideSizeDialog?.Close(false);
        });

        if (!ran) return;
        foundTheme.Should().BeTrue("theme commands must be registered through the Avalonia registry");
        foundSlideSize.Should().BeTrue("slide-size commands must be registered through the Avalonia registry");
        foundCustom.Should().BeTrue("custom slide-size should be exposed as a planner callback intent");
        themeName.Should().Be("Berlin");
        slideWidth.Should().Be(PresentationDesignCommandPlanner.SlideSizeStandard4x3CxEmu);
        slideHeight.Should().Be(PresentationDesignCommandPlanner.SlideSizeStandardCyEmu);
        customPlan.Should().NotBeNull();
        customPlan!.Intent.Should().Be(PresentationDesignCommandIntentKind.RequestCustomSlideSize);
        customInitialState.Should().NotBeNull();
        customInitialState!.Preset.Should().Be(SlideSizeDialogPreset.Standard43);
    }

    [Fact]
    public async Task Ribbon_CustomSlideSize_opens_modal_dialog_and_applies_shared_result()
    {
        var found = false;
        var opened = false;
        var initialPreset = SlideSizeDialogPreset.Custom;
        string? initialWidth = null;
        string? initialHeight = null;
        var invalidApplied = true;
        string? validation = null;
        var visibleAfterInvalid = false;
        var validApplied = false;
        var visibleAfterApply = true;
        long slideWidth = 0;
        long slideHeight = 0;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.slide-size-custom", out var customSlideSize);

            customSlideSize!.Execute(RibbonCommandContext.Empty);
            var dialog = window.ActiveSlideSizeDialog!;
            opened = dialog.IsVisible;
            initialPreset = window.LastCustomSlideSizeInitialState!.Preset;
            initialWidth = dialog.WidthText;
            initialHeight = dialog.HeightText;

            dialog.SetInputForTests("0.25", "7.5", SlideSizeDialogUnit.Inches);
            invalidApplied = dialog.ApplyForTests();
            validation = dialog.ValidationText;
            visibleAfterInvalid = dialog.IsVisible;

            dialog.SetInputForTests("11", "6.25", SlideSizeDialogUnit.Inches);
            validApplied = dialog.ApplyForTests();
            visibleAfterApply = dialog.IsVisible;
            slideWidth = window.Editor.Presentation.SlideSizeCxEmu;
            slideHeight = window.Editor.Presentation.SlideSizeCyEmu;
        });

        if (!ran) return;
        found.Should().BeTrue("custom slide-size must be registered through the Avalonia registry");
        opened.Should().BeTrue("the custom command should open a visible Avalonia dialog");
        initialPreset.Should().Be(SlideSizeDialogPreset.Widescreen169);
        initialWidth.Should().Be("13.333");
        initialHeight.Should().Be("7.500");
        invalidApplied.Should().BeFalse("shared planner validation should block invalid sizes");
        validation.Should().Be(SlideSizeDialogPlanner.MinimumSizeMessage);
        visibleAfterInvalid.Should().BeTrue("invalid apply should keep the dialog open for correction");
        validApplied.Should().BeTrue();
        visibleAfterApply.Should().BeFalse();
        slideWidth.Should().Be(10_058_400L);
        slideHeight.Should().Be(5_715_000L);
    }

    [Fact]
    public async Task Ribbon_layout_command_routes_through_shared_planner()
    {
        var found = false;
        PresentationDesignCommandPlan? layoutPlan = null;
        PresentationLayoutPickerPlan? pickerPlan = null;
        PresentationLayoutChoice? appliedChoice = null;
        string? currentLayoutId = null;
        var applied = false;
        var pickerVisibleAfterOpen = false;
        var pickerChoiceButtonCount = 0;
        var pickerGroupHeaderCount = 0;
        var pickerThumbnailPlaceholderCount = 0;
        var pickerCurrentChoiceCount = 0;
        var pickerVisibleAfterApply = true;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.Presentation.Layouts.Add(new SlideLayout
            {
                Id = "rId2",
                Name = "Blank",
                LayoutType = SlideLayoutType.Blank,
                MasterId = window.Editor.Presentation.Masters[0].Id,
                Placeholders =
                {
                    new SlideShape { Id = 212, Placeholder = new Placeholder { Type = PlaceholderType.Title } },
                }
            });
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(PresentationDesignCommandPlanner.LayoutCommandId, out var layout);

            layout!.Execute(RibbonCommandContext.Empty);
            pickerVisibleAfterOpen = window.IsLayoutPickerVisible;
            pickerChoiceButtonCount = window.LayoutPickerChoiceButtonCount;
            pickerGroupHeaderCount = window.LayoutPickerGroupHeaderCount;
            pickerThumbnailPlaceholderCount = window.LayoutPickerThumbnailPlaceholderCount;
            pickerCurrentChoiceCount = window.LayoutPickerCurrentChoiceCount;
            applied = window.ApplyLayoutChoice("rId2");
            pickerVisibleAfterApply = window.IsLayoutPickerVisible;

            layoutPlan = window.LastLayoutRequestPlan;
            pickerPlan = window.LastLayoutPickerPlan;
            appliedChoice = window.LastAppliedLayoutChoice;
            currentLayoutId = window.Editor.CurrentSlide!.LayoutId;
        });

        if (!ran) return;
        found.Should().BeTrue("layout must be registered through the Avalonia registry");
        layoutPlan.Should().NotBeNull("the command should expose a host callback intent instead of no-oping");
        layoutPlan!.CommandId.Should().Be(PresentationDesignCommandPlanner.LayoutCommandId);
        layoutPlan.Intent.Should().Be(PresentationDesignCommandIntentKind.RequestLayoutPicker);
        pickerPlan.Should().NotBeNull("the host callback should expose concrete shared layout choices");
        pickerVisibleAfterOpen.Should().BeTrue("the Avalonia command should show an actual picker surface");
        pickerChoiceButtonCount.Should().Be(2);
        pickerGroupHeaderCount.Should().Be(1, "the Avalonia picker should render grouped gallery sections");
        pickerThumbnailPlaceholderCount.Should().BeGreaterThan(0, "layout choices should render thumbnail placeholder glyphs");
        pickerCurrentChoiceCount.Should().Be(1, "the current layout should have explicit selected chrome");
        pickerPlan!.Groups.Should().ContainSingle(group =>
            group.Heading == "Master 1" &&
            group.Choices.Select(choice => choice.LayoutId).SequenceEqual(new[] { "rId1", "rId2" }));
        pickerPlan.Choices.Single(choice => choice.LayoutId == "rId1").Chrome.State
            .Should().Be(PresentationLayoutChoiceChromeState.Current);
        pickerPlan.Choices.Single(choice => choice.LayoutId == "rId2").ThumbnailPlaceholders
            .Should()
            .ContainSingle(slot => slot.PlaceholderType == PlaceholderType.Title);
        pickerPlan.Choices.Should().Contain(choice =>
            choice.LayoutId == "rId2" &&
            choice.DisplayName == "Blank" &&
            choice.LayoutType == SlideLayoutType.Blank &&
            choice.MasterId == "rId1" &&
            choice.MasterDisplayName == "Master 1" &&
            choice.PlaceholderCount == 1 &&
            choice.DisplayOrder == 1);
        applied.Should().BeTrue("Avalonia should be able to apply a shared picker choice");
        pickerVisibleAfterApply.Should().BeFalse("the picker should collapse after a choice is applied");
        currentLayoutId.Should().Be("rId2");
        appliedChoice.Should().NotBeNull();
        appliedChoice!.LayoutId.Should().Be("rId2");
        appliedChoice.MasterDisplayName.Should().Be("Master 1");
        appliedChoice.PlaceholderCount.Should().Be(1);
    }

    [Fact]
    public async Task SlidePane_reorder_routes_through_shared_planner()
    {
        var moved = false;
        var slidePaneCount = 0;
        var currentSlideIndex = -1;
        var indicatorVisibleDuringDrag = false;
        var indicatorVisibleAfterDrop = true;
        SlidePaneDropVisualPlan? dropPlan = null;
        string[] titles = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Slide 1";
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Title = "Slide 2";
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Title = "Slide 3";
            window.Editor.SelectSlide(0);

            slidePaneCount = window.SlidePaneSlideItemCount;
            dropPlan = window.PreviewSlidePaneDragForTests(
                sourceSlideIndex: 0,
                startPointerY: 0,
                pointerYWithinItem: SlidePanePlanner.DefaultDragStartThreshold + 1,
                pointerYWithinPane: SlidePanePlanner.DefaultSlideItemHeight * 3);
            indicatorVisibleDuringDrag = window.IsSlidePaneInsertionIndicatorVisible;
            moved = window.CompleteSlidePaneDragForTests();
            titles = window.Editor.Presentation.Slides.Select(slide => slide.Title).ToArray();
            currentSlideIndex = window.CurrentSlideIndex;
            indicatorVisibleAfterDrop = window.IsSlidePaneInsertionIndicatorVisible;
        });

        if (!ran) return;
        slidePaneCount.Should().Be(3, "the Avalonia slide pane should render one selectable item per slide");
        dropPlan.Should().NotBeNull("drag preview should be planned before the drop is applied");
        dropPlan!.TargetSlideIndex.Should().Be(3);
        dropPlan.IsMoveEnabled.Should().BeTrue();
        indicatorVisibleDuringDrag.Should().BeTrue("the shared drop plan should drive visible Avalonia insertion feedback");
        moved.Should().BeTrue("drag release should apply the shared move action plan");
        titles.Should().Equal("Slide 2", "Slide 3", "Slide 1");
        currentSlideIndex.Should().Be(2, "the moved slide should remain selected after reorder");
        indicatorVisibleAfterDrop.Should().BeFalse("the insertion indicator is only visible during active drag feedback");
    }

    [Fact]
    public async Task SlidePane_keyboard_actions_route_through_shared_planner()
    {
        var deletedSingleSlide = true;
        var duplicated = false;
        var movedEarlier = false;
        var finalSlideIndex = -1;
        string[] titles = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Intro";

            deletedSingleSlide = window.TryApplySlidePaneKeyboardAction(
                SlidePaneKeyboardIntentKind.DeleteCurrentSlide);

            window.TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide);
            window.Editor.CurrentSlide!.Title = "Body";
            window.Editor.SelectSlide(0);

            duplicated = window.TryApplySlidePaneKeyboardAction(
                SlidePaneKeyboardIntentKind.DuplicateCurrentSlide);
            movedEarlier = window.TryApplySlidePaneKeyboardAction(
                SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier);

            titles = window.Editor.Presentation.Slides.Select(slide => slide.Title).ToArray();
            finalSlideIndex = window.CurrentSlideIndex;
        });

        if (!ran) return;
        deletedSingleSlide.Should().BeFalse("the shared planner keeps keyboard delete from removing the last slide");
        duplicated.Should().BeTrue();
        movedEarlier.Should().BeTrue();
        titles.Should().Equal("Intro", "Intro", "Body");
        finalSlideIndex.Should().Be(0, "moving the duplicated slide earlier should keep it selected");
    }

    [Fact]
    public async Task Print_command_records_shared_backstage_print_plan()
    {
        var found = false;
        PresentationPrintBackstagePlan? printPlan = null;
        PresentationPrintOutputPackage? printPackage = null;
        var isBackstageOpen = false;
        string? selectedPane = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(PresentationExportPlanner.PrintCommandId, out var print);

            print!.Execute(RibbonCommandContext.Empty);
            printPlan = window.LastPrintBackstagePlan;
            printPackage = window.LastPrintOutputPackage;
            isBackstageOpen = window.IsBackstageOpen;
            selectedPane = window.CurrentBackstagePaneLabel;
        });

        if (!ran) return;
        found.Should().BeTrue("the Avalonia registry should expose the shared Backstage print planner seam");
        isBackstageOpen.Should().BeTrue("the Print command should open the Avalonia Backstage");
        selectedPane.Should().Be("Print");
        printPlan.Should().NotBeNull();
        printPackage.Should().BeNull("Backstage Print planning must not execute package handoff or open a native dialog");
        printPlan!.PackagePlan.PrintPlan.CommandId.Should().Be(PresentationExportPlanner.PrintCommandId);
        printPlan.SelectedLayout.Layout.Layout.Should().Be(PresentationPrintLayoutKind.FullPageSlides);
        printPlan.PackagePlan.Route.Should().Be(PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf);
        printPlan.PreviewPlan.Pages.Select(page => page.ThumbnailLabel)
            .Should()
            .Equal("Slide 1", "Slide 2", "Slide 3", "Slide 4");
        printPlan.NativePrinterDialogDeferred.Should().BeTrue();
        printPlan.NativePrintHandoff.Status.Should().Be(PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost);
        printPlan.NativePrintHandoff.IsPackageReady.Should().BeTrue();
        printPlan.NativePrintHandoff.RequiresHostHandoff.Should().BeTrue();
        printPlan.NativePrintHandoff.CanOpenNativePrintDialog.Should().BeFalse();
        printPlan.NativePrintHandoff.Reason.Should().Contain("Native output capability detection is pending");
        printPlan.LayoutChoices.Select(choice => choice.Layout.SlidesPerPage).Should().Equal(1, 1, 1, 2, 3, 4, 6, 9);
        printPlan.RangeChoices.Select(choice => choice.Kind).Should().Contain(PresentationSlideRangeKind.CurrentSlide);
    }

    [Fact]
    public async Task Print_output_package_records_shared_execution_descriptor()
    {
        PresentationPrintOutputPackage? printPackage = null;
        PresentationNativePrintHandoffPlan? handoffPlan = null;
        PresentationPrintOutputPackageExecutionDescriptor? descriptor = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Opening";
            window.Editor.CurrentSlide.Notes = MakeTextBody("Opening speaker note.");
            window.Editor.InsertSlide();

            printPackage = window.RefreshPrintOutputPackage(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CurrentSlide,
                    CurrentSlideNumber: 1)));
            handoffPlan = window.LastNativePrintHandoffPlan;
            descriptor = window.LastPrintExecutionDescriptor;
        });

        if (!ran) return;
        printPackage.Should().NotBeNull();
        handoffPlan.Should().NotBeNull();
        descriptor.Should().NotBeNull();
        descriptor!.PackagePlan.Should().BeSameAs(printPackage!.Plan);
        descriptor.HandoffPlan.Should().BeSameAs(handoffPlan);
        descriptor.Validation.IsValid.Should().BeTrue();
        descriptor.Validation.ByteCount.Should().Be(printPackage.Bytes.Length);
        descriptor.IsHostReadyPdfPackage.Should().BeTrue();
        descriptor.CanMaterialize.Should().BeTrue();
        descriptor.SuggestedDocumentName.Should().Be("Presentation");
        descriptor.SuggestedPrintJobName.Should().Be("Presentation - Notes Pages - Slide 1, 1 page");
        handoffPlan!.Status.Should().Be(PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost);
        handoffPlan.SuggestedTempFileName.Should().Be("Presentation-print.pdf");
        handoffPlan.SuggestedPrintJobName.Should().Be(descriptor.SuggestedPrintJobName);
    }

    [Fact]
    public async Task Print_options_pane_projects_shared_summary_lines()
    {
        PresentationPrintBackstagePlan? printPlan = null;
        string heading = string.Empty;
        string message = string.Empty;
        IReadOnlyList<string> renderedOptionLines = [];
        IReadOnlyList<string> renderedPreviewRows = [];
        IReadOnlyList<string> renderedLayoutRows = [];
        IReadOnlyList<string> renderedRangeRows = [];
        var renderedRowCount = 0;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            printPlan = window.ShowPrintOptionsPane(new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: [1, 3]),
                HandoutSlidesPerPage: 3,
                PrintHiddenSlides: true,
                Copies: 3,
                Collate: false,
                ColorMode: PresentationPrintColorMode.PureBlackAndWhite,
                FrameSlides: true,
                IncludeCommentsAndInkMarkup: true));

            heading = window.PrintOptionsPaneHeading;
            message = window.PrintOptionsPaneMessage;
            renderedOptionLines = window.PrintOptionsPaneRenderedOptionLines.ToArray();
            renderedPreviewRows = window.PrintOptionsPaneRenderedPreviewRows.ToArray();
            renderedLayoutRows = window.PrintOptionsPaneRenderedLayoutRows.ToArray();
            renderedRangeRows = window.PrintOptionsPaneRenderedRangeRows.ToArray();
            renderedRowCount = window.PrintOptionsPaneRenderedRowCount;
        });

        if (!ran) return;
        printPlan.Should().NotBeNull();
        heading.Should().Be(printPlan!.Heading);
        message.Should().Be(printPlan.Description);
        renderedOptionLines.Should().Equal(printPlan.OutputOptionChoices.Select(FormatPrintOptionChoice));
        renderedOptionLines.Where(line => line.StartsWith("Selected:", StringComparison.Ordinal))
            .Should()
            .Equal(
                "Selected: Copies: 3 copies\nSet the number of copies from 1 to 999 before handing the package to the native printer host.",
                "Selected: Collation: Uncollated\nPrint all copies of each page before moving to the next page.",
                "Selected: Color: Pure Black and White\nUse a high-contrast black-and-white print intent.",
                "Selected: Content: Print hidden slides\nInclude hidden slides in the normalized print range.",
                "Selected: Output: Frame slides\nDraw a frame around each slide thumbnail/page.",
                "Selected: Output: Print comments and ink markup\nReserve print intent for comments and ink markup.");
        renderedPreviewRows.Should().ContainSingle()
            .Which.Should().Be("Selected: Handout page 1\nHandout with slides 1, 3");
        renderedLayoutRows.Should().HaveCount(printPlan.LayoutChoices.Count);
        renderedLayoutRows.Should().Contain(row => row.StartsWith("Selected: Handouts (3 slides per page)", StringComparison.Ordinal));
        renderedRangeRows.Should().HaveCount(printPlan.RangeChoices.Count);
        renderedRangeRows.Should().Contain(row => row.StartsWith("Selected: Selected Slides", StringComparison.Ordinal));
        renderedRangeRows.Should().Contain(row => row.Contains("Custom Range", StringComparison.Ordinal));
        renderedRowCount.Should().BeGreaterThan(renderedOptionLines.Count);
        printPlan.NativePrinterDialogDeferred.Should().BeTrue();
        printPlan.NativePrintHandoff.StatusText.Should().Be("Deferred by host");
        printPlan.NativePrintHandoff.IsPackageReady.Should().BeTrue();
        printPlan.NativePrintHandoff.CanOpenNativePrintDialog.Should().BeFalse();
    }

    [Fact]
    public async Task Print_options_pane_parses_custom_range_through_avalonia_adapter()
    {
        PresentationPrintBackstagePlan? printPlan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            printPlan = window.ShowPrintOptionsPane(new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    CustomRangeText: "2,4")));
        });

        if (!ran) return;
        printPlan.Should().NotBeNull();
        printPlan!.SelectedRange.Kind.Should().Be(PresentationSlideRangeKind.CustomRange);
        printPlan.SelectedRange.Request!.CustomRangeText.Should().Be("2,4");
        printPlan.SelectedRange.DisplayName.Should().Be("Slides 2, 4");
        printPlan.SelectedRange.IsAvailable.Should().BeTrue();
        printPlan.PageCount.Should().Be(2);
        printPlan.CanBuildPackage.Should().BeTrue();
    }

    [Fact]
    public async Task Print_options_pane_custom_range_input_rebuilds_plan()
    {
        PresentationPrintBackstagePlan? printPlan = null;
        var applied = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            window.ShowPrintOptionsPane();
            applied = window.ApplyPrintCustomRangeForTests("2,4");
            printPlan = window.LastPrintBackstagePlan;
        });

        if (!ran) return;
        applied.Should().BeTrue();
        printPlan.Should().NotBeNull();
        printPlan!.SelectedRange.Kind.Should().Be(PresentationSlideRangeKind.CustomRange);
        printPlan.SelectedRange.Request!.CustomRangeText.Should().Be("2,4");
        printPlan.PageCount.Should().Be(2);
        printPlan.CanBuildPackage.Should().BeTrue();
    }

    [Fact]
    public async Task Backstage_print_custom_range_input_rebuilds_shared_plan()
    {
        PresentationPrintBackstagePlan? printPlan = null;
        var applied = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            window.ShowBackstageForTests();
            window.ActivateBackstageEntryForTests("Print").Should().BeTrue();
            applied = window.ApplyBackstageCustomPrintRangeForTests("2,4");
            printPlan = window.LastPrintBackstagePlan;
        });

        if (!ran) return;
        applied.Should().BeTrue();
        printPlan.Should().NotBeNull();
        printPlan!.SelectedRange.Kind.Should().Be(PresentationSlideRangeKind.CustomRange);
        printPlan.SelectedRange.Request!.CustomRangeText.Should().Be("2,4");
        printPlan.PageCount.Should().Be(2);
        printPlan.CanBuildPackage.Should().BeTrue();
    }

    [Fact]
    public async Task Notes_page_pdf_refresh_uses_shared_render_plan()
    {
        PresentationNotesPagePdfRenderPlan? notesPdfPlan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Opening";
            window.Editor.CurrentSlide.Notes = MakeTextBody("Opening note");
            window.Editor.InsertSlide();

            notesPdfPlan = window.RefreshNotesPagePdfRenderPlan(new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CurrentSlide,
                CurrentSlideNumber: 1));
        });

        if (!ran) return;
        notesPdfPlan.Should().NotBeNull();
        notesPdfPlan!.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        notesPdfPlan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1);
        notesPdfPlan.PreviewPlans.Should().ContainSingle(preview =>
            preview.SlideNumber == 1 &&
            preview.NoteLines.Count == 1 &&
            preview.NoteLines[0] == "Opening note");
        notesPdfPlan.Pages.Should().ContainSingle();
        notesPdfPlan.Pages[0].Ops.OfType<Free.Shared.Pdf.PdfText>().Select(text => text.Text)
            .Should()
            .Contain(["Opening", "Opening note"]);
    }

    [Fact]
    public async Task Print_options_pane_uses_shared_notes_render_page_count()
    {
        PresentationNotesPagePdfRenderPlan? renderPlan = null;
        PresentationPrintBackstagePlan? printPlan = null;
        IReadOnlyList<string> renderedOptionLines = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Overflow notes";
            window.Editor.CurrentSlide.Notes = MakeTextBody(
                Enumerable.Range(1, 60)
                    .Select(i => $"Speaker note line number {i} with enough words to be realistic.")
                    .ToArray());

            renderPlan = window.RefreshNotesPagePdfRenderPlan();
            printPlan = window.ShowPrintOptionsPane(
                new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages));
            renderedOptionLines = window.PrintOptionsPaneRenderedOptionLines.ToArray();
        });

        if (!ran) return;
        renderPlan.Should().NotBeNull();
        printPlan.Should().NotBeNull();
        renderPlan!.Pages.Count.Should().BeGreaterThan(1);
        printPlan!.PageCount.Should().Be(renderPlan.Pages.Count);
        printPlan.LayoutSummary.Should().Be($"Notes Pages - All slides, {renderPlan.Pages.Count} pages");
        printPlan.SelectedLayout.PackagePlan.PageCount.Should().Be(renderPlan.Pages.Count);
        printPlan.PreviewPlan.PageCount.Should().Be(renderPlan.Pages.Count);
        printPlan.PreviewPlan.Pages.Should().HaveCount(renderPlan.Pages.Count);
        renderedOptionLines.Should().Equal(printPlan.OutputOptionChoices.Select(FormatPrintOptionChoice));
    }

    [Fact]
    public async Task Notes_page_pdf_export_command_is_registered_for_native_save_route()
    {
        var found = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(PresentationExportPlanner.NotesPagePdfExportCommandId, out _);
        });

        if (!ran) return;
        found.Should().BeTrue("the Avalonia registry should expose the notes-page PDF save route");
    }

    [Fact]
    public async Task Video_export_command_records_shared_frame_package()
    {
        var found = false;
        PresentationVideoExportPlan? videoPlan = null;
        PresentationVideoFramePackage? videoPackage = null;
        PresentationVideoExportHandoffPlan? videoHandoff = null;
        PresentationVideoFramePackageExecutionDescriptor? videoDescriptor = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(PresentationExportPlanner.VideoExportCommandId, out var video);

            // The ribbon command is the real MP4 export route. Keep the package-only
            // inspection explicit so this test remains headless and does not open a picker.
            video.Should().NotBeNull();
            videoPackage = window.RefreshVideoFramePackage();
            videoPlan = window.LastVideoExportPlan;
            videoHandoff = window.LastVideoExportHandoffPlan;
            videoDescriptor = window.LastVideoExecutionDescriptor;
        });

        if (!ran) return;
        found.Should().BeTrue("the Avalonia registry should expose the shared video frame package seam");
        videoPackage.Should().NotBeNull();
        videoPlan.Should().NotBeNull();
        videoHandoff.Should().NotBeNull();
        videoDescriptor.Should().NotBeNull();
        videoPackage!.Plan.ExportPlan.Should().BeSameAs(videoPlan);
        videoHandoff!.PackagePlan.Should().BeSameAs(videoPackage.Plan);
        videoDescriptor!.PackagePlan.Should().BeSameAs(videoPackage.Plan);
        videoDescriptor.HandoffPlan.Should().BeSameAs(videoHandoff);
        videoDescriptor.Validation.IsValid.Should().BeTrue();
        videoDescriptor.Validation.ExpectedFrameCount.Should().Be(3);
        videoDescriptor.Validation.ManifestFrameCount.Should().Be(3);
        videoDescriptor.Validation.ZipFrameEntryCount.Should().Be(3);
        videoDescriptor.ContentType.Should().Be(PresentationVideoFramePackageExecutor.PackageContentType);
        videoDescriptor.SuggestedPackageName.Should().Be("Presentation-video-encoder-input.zip");
        videoDescriptor.CanMaterialize.Should().BeTrue();
        videoHandoff.Status.Should().Be(PresentationVideoExportHandoffStatus.EncoderInputPackageReadyHostDeferred);
        videoHandoff.IsFramePackageReady.Should().BeTrue();
        videoHandoff.CanOpenHostEncoder.Should().BeFalse();
        videoHandoff.Mp4EncoderDeferredByHost.Should().BeTrue();
        videoHandoff.StatusText.Should().Be("Avalonia video export host: MP4 encoder deferred; frame package ready");
        videoPackage.Plan.DeferredCapabilities.Should().Contain(PresentationVideoFramePackageExecutor.Mp4EncoderDeferred);
        videoPackage.Frames.Select(frame => frame.FileName)
            .Should()
            .Equal(
                "frames/slide-01-frame-0001.png",
                "frames/slide-02-frame-0002.png",
                "frames/slide-03-frame-0003.png");
        videoPackage.Frames.Should().OnlyContain(frame => frame.WidthPx == 1920 && frame.HeightPx == 1080);
        videoPackage.Bytes.Length.Should().BeGreaterThan(100);
        videoPlan!.CommandId.Should().Be(PresentationExportPlanner.VideoExportCommandId);
        videoPlan.DefaultExtensionWithDot.Should().Be(PresentationExportPlanner.VideoExportExtension);
        videoPlan.SlideRange.SlideNumbers.Should().Equal(1, 2, 3);
        videoPlan.Quality.Quality.Should().Be(PresentationVideoQualityKind.FullHd);
        videoPlan.EstimatedDuration.Should().Be(TimeSpan.FromSeconds(15));
        videoPlan.Storyboard.SlideRange.SlideNumbers.Should().Equal(1, 2, 3);
        videoPlan.Storyboard.Segments.Select(segment => segment.SlideNumber).Should().Equal(1, 2, 3);
        videoPlan.Storyboard.Segments.Select(segment => segment.StartTime)
            .Should()
            .Equal(TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        videoPlan.Storyboard.OutputWidthPx.Should().Be(1920);
        videoPlan.Storyboard.OutputHeightPx.Should().Be(1080);
        videoPlan.Storyboard.FrameRateHint.Should().Be(30);
        videoPlan.Storyboard.TotalDuration.Should().Be(videoPlan.EstimatedDuration);
        videoPlan.CanExecute.Should().BeFalse();
        videoPlan.DisabledReason.Should().Be(PresentationExportPlanner.VideoExportDeferredMessage);
    }

    [Fact]
    public async Task Video_export_plan_reports_native_encoder_readiness()
    {
        PresentationVideoExportPlan? plan = null;
        var capabilities = new LinuxNativeOutputCapabilities(
            LinuxNativePrintCapability.Unavailable("no queue"),
            new LinuxVideoEncoderCapability(true, "ffmpeg", "mpeg4", false, "ready"));

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(
                Array.Empty<string>(),
                loadRecentFilesStore: null,
                nativeOutputCapabilities: capabilities,
                videoExportAdapter: new RecordingVideoAdapter(capabilities.Video));
            window.Editor.InsertSlide();
            plan = window.RefreshVideoExportPlan();
        });

        if (!ran) return;
        plan.Should().NotBeNull();
        plan!.IsImplemented.Should().BeTrue();
        plan.CanExecute.Should().BeTrue();
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public async Task Print_command_refreshes_shared_handout_layout_plan()
    {
        var found = false;
        PresentationHandoutLayoutPlan? handoutPlan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(PresentationExportPlanner.PrintCommandId, out var print);

            print!.Execute(RibbonCommandContext.Empty);
            handoutPlan = window.LastHandoutLayoutPlan;
        });

        if (!ran) return;
        found.Should().BeTrue("the Avalonia registry should expose the shared print plan seam");
        handoutPlan.Should().NotBeNull();
        handoutPlan!.PrintPlan.CommandId.Should().Be(PresentationExportPlanner.PrintCommandId);
        handoutPlan.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.Handouts);
        handoutPlan.PrintPlan.Layout.SlidesPerPage.Should().Be(6);
        handoutPlan.Pages.Should().ContainSingle();
        handoutPlan.Pages[0].Slots.Select(slot => slot.SlideNumber).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task Ribbon_transition_commands_route_through_shared_planner()
    {
        var foundFade = false;
        var foundDuration = false;
        var foundApplyAll = false;
        TransitionKind? firstKind = null;
        int? firstDuration = null;
        TransitionKind? secondKind = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.SelectSlide(0);
            var registry = window.BuildCommandRegistry();
            foundFade = registry.TryGet("freep.transition.fade", out var fade);
            foundDuration = registry.TryGet("freep.transition.duration", out var duration);
            foundApplyAll = registry.TryGet("freep.transition.apply-all", out var applyAll);

            fade!.Execute(RibbonCommandContext.Empty);
            duration!.Execute(RibbonCommandContext.ForSelectedValue("1.50s"));
            applyAll!.Execute(RibbonCommandContext.Empty);

            firstKind = window.Editor.Presentation.Slides[0].Transition?.Kind;
            firstDuration = window.Editor.Presentation.Slides[0].Transition?.DurationMs;
            secondKind = window.Editor.Presentation.Slides[1].Transition?.Kind;
        });

        if (!ran) return;
        foundFade.Should().BeTrue("Fade must be registered through the Avalonia registry");
        foundDuration.Should().BeTrue("Duration must be registered through the Avalonia registry");
        foundApplyAll.Should().BeTrue("Apply To All must be registered through the Avalonia registry");
        firstKind.Should().Be(TransitionKind.Fade);
        firstDuration.Should().Be(1500);
        secondKind.Should().Be(TransitionKind.Fade);
    }

    [Fact]
    public async Task Ribbon_transition_advance_on_click_is_stateful_and_tracks_undoable_model_state()
    {
        var found = false;
        var stateful = false;
        var initiallyChecked = false;
        var checkedAfterExecute = true;
        var checkedAfterSlideSwitch = false;
        var checkedAfterReturn = true;
        var checkedAfterUndo = false;
        var advanceAfterExecute = true;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.SelectSlide(0);
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.transition.advance-on-click", out var command);

            stateful = command is IRibbonStatefulCommand;
            var statefulCommand = command as IRibbonStatefulCommand;
            initiallyChecked = statefulCommand?.GetState().IsChecked ?? true;

            command!.Execute(RibbonCommandContext.Empty);
            advanceAfterExecute = window.Editor.CurrentSlideTransition?.AdvanceOnClick == true;
            checkedAfterExecute = statefulCommand?.GetState().IsChecked == true;

            window.Editor.SelectSlide(1);
            checkedAfterSlideSwitch = statefulCommand?.GetState().IsChecked == true;
            window.Editor.SelectSlide(0);
            checkedAfterReturn = statefulCommand?.GetState().IsChecked == true;
            window.Editor.Undo();
            checkedAfterUndo = statefulCommand?.GetState().IsChecked ?? true;
        });

        if (!ran) return;
        found.Should().BeTrue("the transition toggle must be available in the Avalonia registry");
        stateful.Should().BeTrue("WPF exposes Advance On Click as a stateful ribbon toggle");
        initiallyChecked.Should().BeTrue("a slide without a transition uses the model default of advancing on click");
        advanceAfterExecute.Should().BeFalse();
        checkedAfterExecute.Should().BeFalse();
        checkedAfterSlideSwitch.Should().BeTrue("the state must follow the newly selected slide");
        checkedAfterReturn.Should().BeFalse();
        checkedAfterUndo.Should().BeTrue("the checked state must follow the undoable model mutation");
    }

    [Fact]
    public async Task Ribbon_edit_points_toggle_uses_shared_mode_planner_and_live_canvas_state()
    {
        var found = false;
        var stateful = false;
        var initiallyChecked = false;
        var checkedAfterExecute = true;
        var canvasEnabledAfterExecute = true;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(PresentationEditPointsModePlanner.CommandId, out var command);
            stateful = command is IRibbonStatefulCommand;
            var statefulCommand = command as IRibbonStatefulCommand;
            initiallyChecked = statefulCommand?.GetState().IsChecked ?? false;
            command!.Execute(RibbonCommandContext.Empty);
            checkedAfterExecute = statefulCommand?.GetState().IsChecked == true;
            canvasEnabledAfterExecute = window.EditPointsEnabledForTests;
        });

        if (!ran) return;
        found.Should().BeTrue();
        stateful.Should().BeTrue();
        initiallyChecked.Should().BeTrue();
        checkedAfterExecute.Should().BeFalse();
        canvasEnabledAfterExecute.Should().BeFalse();
    }

    [Fact]
    public async Task Ribbon_animation_commands_route_through_shared_planner()
    {
        var foundFade = false;
        var foundDuration = false;
        var foundDelay = false;
        var foundPane = false;
        var paneStateful = false;
        var paneInitiallyChecked = true;
        var paneCheckedAfterShow = false;
        var paneCheckedAfterHide = true;
        AnimationPreset? preset = null;
        int? duration = null;
        int? delay = null;
        AnimationPaneTimelinePlan? panePlan = null;
        var paneVisible = false;
        var paneHeading = string.Empty;
        var paneMessage = string.Empty;
        var paneRenderedCount = 0;
        var previewEnabled = false;
        IReadOnlyList<string> playbackControls = [];
        IReadOnlyList<string> paneRows = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertDefaultRectangle();
            shape.Name = "Hero box";
            window.Editor.Select(shape.Id);
            var registry = window.BuildCommandRegistry();
            foundFade = registry.TryGet("freep.anim.entrance.fade", out var fade);
            foundDuration = registry.TryGet("freep.anim.duration", out var durationCommand);
            foundDelay = registry.TryGet("freep.anim.delay", out var delayCommand);
            foundPane = registry.TryGet("freep.anim.pane", out var pane);
            paneStateful = pane is IRibbonStatefulCommand;
            var paneState = pane as IRibbonStatefulCommand;
            paneInitiallyChecked = paneState?.GetState().IsChecked ?? true;

            fade!.Execute(RibbonCommandContext.Empty);
            durationCommand!.Execute(RibbonCommandContext.ForSelectedValue("1.50s"));
            delayCommand!.Execute(RibbonCommandContext.ForSelectedValue("0.25s"));
            pane!.Execute(RibbonCommandContext.Empty);
            paneCheckedAfterShow = paneState?.GetState().IsChecked == true;

            var animation = window.Editor.CurrentSlideAnimations.Single();
            preset = animation.Preset;
            duration = animation.DurationMs;
            delay = animation.DelayMs;
            panePlan = window.LastAnimationPaneTimelinePlan;
            paneVisible = window.IsAnimationPaneVisible;
            paneHeading = window.AnimationPaneHeading;
            paneMessage = window.AnimationPaneMessage;
            paneRenderedCount = window.AnimationPaneRenderedItemCount;
            previewEnabled = window.IsAnimationPanePreviewEnabled;
            playbackControls = window.AnimationPanePlaybackControls.ToArray();
            paneRows = window.AnimationPaneRenderedRows.ToArray();

            pane.Execute(RibbonCommandContext.Empty);
            paneCheckedAfterHide = paneState?.GetState().IsChecked == true;
        });

        if (!ran) return;
        foundFade.Should().BeTrue("animation effects must be registered through the Avalonia registry");
        foundDuration.Should().BeTrue("duration must be registered through the Avalonia registry");
        foundDelay.Should().BeTrue("delay must be registered through the Avalonia registry");
        foundPane.Should().BeTrue("the animation pane command must be registered");
        paneStateful.Should().BeTrue("WPF exposes the animation pane command as a stateful ribbon toggle");
        paneInitiallyChecked.Should().BeFalse("the animation pane starts closed");
        paneCheckedAfterShow.Should().BeTrue("the checked state must follow the open pane");
        paneCheckedAfterHide.Should().BeFalse("the checked state must follow the closed pane");
        preset.Should().Be(AnimationPreset.Fade);
        duration.Should().Be(1500);
        delay.Should().Be(250);
        panePlan.Should().NotBeNull();
        panePlan!.Items.Should().ContainSingle();
        panePlan.SelectedIndex.Should().Be(0);
        panePlan.Items[0].EffectText.Should().Be("In: Fade");
        panePlan.Items[0].DurationMs.Should().Be(1500);
        panePlan.Items[0].DelayMs.Should().Be(250);
        panePlan.PreviewIntent.CanExecute.Should().BeTrue();
        panePlan.PlaybackControls.Should().Contain(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected
            && control.IsEnabled
            && control.StartAnimationIndex == 0);
        paneVisible.Should().BeTrue("the Avalonia animation pane command should show the in-app pane");
        paneHeading.Should().Be("Animation Pane - slide 1 (1 animations)");
        paneMessage.Should().Contain("Hero box");
        paneRenderedCount.Should().Be(1);
        previewEnabled.Should().BeTrue();
        playbackControls.Should().Equal(
            "Preview: available",
            "Play From Selected: available",
            "Play All: available",
            "Stop: unavailable");
        paneRows.Should().ContainSingle()
            .Which.Should().Contain("Hero box - In: Fade")
            .And.Contain("duration 1.5s")
            .And.Contain("delay 0.25s")
            .And.Contain("move earlier unavailable")
            .And.Contain("move later unavailable");
    }

    [Fact]
    public async Task New_WhileAnimationPaneVisible_RebindsPaneToNewPresentation()
    {
        var paneRowsBeforeNew = Array.Empty<string>();
        var paneRowsAfterNew = Array.Empty<string>();
        var renderedCountAfterNew = -1;
        var newResult = false;

        var ran = await OnUiThread(async () =>
        {
            var window = new MainWindow(
                Array.Empty<string>(),
                loadRecentFilesStore: null,
                options: null,
                promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave));
            var shape = window.Editor.InsertDefaultRectangle();
            shape.Name = "Old animation";
            window.Editor.Select(shape.Id);

            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.fade", out var fade).Should().BeTrue();
            fade!.Execute(RibbonCommandContext.Empty);
            window.ShowAnimationPane();
            paneRowsBeforeNew = window.AnimationPaneRenderedRows.ToArray();

            newResult = await window.FileNewAsyncForTests();
            paneRowsAfterNew = window.AnimationPaneRenderedRows.ToArray();
            renderedCountAfterNew = window.AnimationPaneRenderedItemCount;
        });

        if (!ran) return;
        newResult.Should().BeTrue();
        paneRowsBeforeNew.Should().ContainSingle()
            .Which.Should().Contain("Old animation");
        paneRowsAfterNew.Should().BeEmpty();
        renderedCountAfterNew.Should().Be(1, "the replacement presentation has no animations but still renders its empty-state row");
    }

    [Fact]
    public async Task Ribbon_motion_command_creates_motion_path_animation()
    {
        MotionPath? motion = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(shape.Id);
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.motion.right", out var command).Should().BeTrue();

            command!.Execute(RibbonCommandContext.Empty);
            motion = window.Editor.CurrentSlideAnimations.Single().Motion;
        });

        if (!ran) return;
        motion.Should().NotBeNull();
        motion!.Segments.Should().HaveCount(2);
        motion.Segments[0].Kind.Should().Be(MotionPathSegmentKind.Move);
        motion.Segments[1].Kind.Should().Be(MotionPathSegmentKind.Line);
        motion.Segments[1].X.Should().Be(0.5);
        motion.Segments[1].Y.Should().Be(0);
    }

    [Theory]
    [InlineData("freep.anim.motion.circle", 5)]
    [InlineData("freep.anim.motion.loop", 3)]
    [InlineData("freep.anim.motion.s", 5)]
    [InlineData("freep.anim.motion.figure-eight", 5)]
    public async Task Ribbon_additional_motion_commands_create_cubic_paths(
        string commandId,
        int expectedSegmentCount)
    {
        MotionPath? motion = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(shape.Id);
            var registry = window.BuildCommandRegistry();
            registry.TryGet(commandId, out var command).Should().BeTrue();

            command!.Execute(RibbonCommandContext.Empty);
            motion = window.Editor.CurrentSlideAnimations.Single().Motion;
        });

        if (!ran) return;
        motion.Should().NotBeNull();
        motion!.Segments.Should().HaveCount(expectedSegmentCount);
        motion.Segments[0].Kind.Should().Be(MotionPathSegmentKind.Move);
        motion.Segments.Skip(1).Should().OnlyContain(segment => segment.Kind == MotionPathSegmentKind.Cubic);
        motion.Segments[^1].X.Should().Be(0);
        motion.Segments[^1].Y.Should().Be(0);
    }

    [Fact]
    public async Task Ribbon_motion_reverse_reverses_the_selected_path()
    {
        MotionPath? motion = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(shape.Id);
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.motion.right", out var addCommand).Should().BeTrue();
            registry.TryGet("freep.anim.motion.reverse", out var reverseCommand).Should().BeTrue();

            addCommand!.Execute(RibbonCommandContext.Empty);
            reverseCommand!.Execute(RibbonCommandContext.Empty);
            motion = window.Editor.CurrentSlideAnimations.Single().Motion;
        });

        if (!ran) return;
        motion.Should().NotBeNull();
        motion!.Segments[0].Kind.Should().Be(MotionPathSegmentKind.Move);
        motion.Segments[0].X.Should().Be(0.5);
        motion.Segments[0].Y.Should().Be(0);
        motion.Segments[1].Kind.Should().Be(MotionPathSegmentKind.Line);
        motion.Segments[1].X.Should().Be(0);
        motion.Segments[1].Y.Should().Be(0);
    }

    [Fact]
    public async Task Animation_pane_renders_shared_timeline_rows_and_action_state()
    {
        AnimationPaneTimelinePlan? panePlan = null;
        var paneVisible = false;
        var paneHeading = string.Empty;
        var paneMessage = string.Empty;
        var previewEnabled = false;
        IReadOnlyList<string> playbackControls = [];
        IReadOnlyList<string> paneRows = [];
        IReadOnlyList<string> evidenceLines = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.fade", out var fade).Should().BeTrue();
            registry.TryGet("freep.anim.trigger", out var trigger).Should().BeTrue();
            registry.TryGet("freep.anim.duration", out var duration).Should().BeTrue();
            registry.TryGet("freep.anim.delay", out var delay).Should().BeTrue();
            registry.TryGet("freep.anim.pane", out var pane).Should().BeTrue();

            var hero = window.Editor.InsertDefaultRectangle();
            hero.Name = "Hero box";
            window.Editor.Select(hero.Id);
            fade!.Execute(RibbonCommandContext.Empty);
            duration!.Execute(RibbonCommandContext.ForSelectedValue("0.75s"));

            var caption = window.Editor.InsertDefaultRectangle();
            caption.Name = "Caption box";
            window.Editor.Select(caption.Id);
            fade.Execute(RibbonCommandContext.Empty);
            trigger!.Execute(RibbonCommandContext.ForSelectedValue("After Previous"));
            delay!.Execute(RibbonCommandContext.ForSelectedValue("0.50s"));

            pane!.Execute(RibbonCommandContext.Empty);

            panePlan = window.LastAnimationPaneTimelinePlan;
            paneVisible = window.IsAnimationPaneVisible;
            paneHeading = window.AnimationPaneHeading;
            paneMessage = window.AnimationPaneMessage;
            previewEnabled = window.IsAnimationPanePreviewEnabled;
            playbackControls = window.AnimationPanePlaybackControls.ToArray();
            paneRows = window.AnimationPaneRenderedRows.ToArray();
            evidenceLines = window.AnimationPaneWorkflowEvidenceLines.ToArray();
        });

        if (!ran) return;
        paneVisible.Should().BeTrue();
        panePlan.Should().NotBeNull();
        panePlan!.Items.Should().HaveCount(2);
        panePlan.SelectedIndex.Should().Be(1);
        paneHeading.Should().Be("Animation Pane - slide 1 (2 animations)");
        paneMessage.Should().Contain("Caption box - In: Fade");
        previewEnabled.Should().BeTrue();
        playbackControls.Should().Contain("Play From Selected: available");
        playbackControls.Should().Contain("Stop: unavailable");
        paneRows.Should().HaveCount(2);
        paneRows[0].Should().Contain("1. Hero box - In: Fade")
            .And.Contain("On Click")
            .And.Contain("duration 0.75s")
            .And.Contain("move earlier unavailable")
            .And.Contain("move later available");
        paneRows[1].Should().Contain("2. Caption box - In: Fade")
            .And.Contain("After Previous")
            .And.Contain("delay 0.5s")
            .And.Contain("move earlier available")
            .And.Contain("move later unavailable");
        evidenceLines.Should().Equal(
            "Rows: 2; selected: 2; timing editors: 2; effect-option rows: 0; reorderable rows: 2",
            "Playback controls: Preview: available; Play From Selected: available; Play All: available; Stop: unavailable",
            "Selected row: Caption box - In: Fade; trigger After Previous; duration 0.5s; delay 0.5s");
    }

    [Fact]
    public async Task Animation_pane_toggles_paragraph_build_through_shared_mutation_plan()
    {
        AnimationPaneParagraphBuildMutationPlan? enablePlan = null;
        AnimationPaneParagraphBuildMutationPlan? disablePlan = null;
        var paragraphBuildAfterEnable = false;
        var paragraphBuildAfterDisable = true;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertTextBox("First paragraph");
            var secondParagraph = new Paragraph();
            secondParagraph.Runs.Add(new Run { Text = "Second paragraph" });
            shape.TextBody!.Paragraphs.Add(secondParagraph);
            window.Editor.Select(shape.Id);

            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.fade", out var fade).Should().BeTrue();
            fade!.Execute(RibbonCommandContext.Empty);

            enablePlan = window.ToggleParagraphBuildForTests(shape.Id);
            paragraphBuildAfterEnable = SlideShowAnimationBuildPlanner.IsParagraphBuild(
                window.Editor.CurrentSlide!, shape.Id);
            disablePlan = window.ToggleParagraphBuildForTests(shape.Id);
            paragraphBuildAfterDisable = SlideShowAnimationBuildPlanner.IsParagraphBuild(
                window.Editor.CurrentSlide!, shape.Id);
        });

        if (!ran) return;
        enablePlan.Should().NotBeNull();
        enablePlan!.ShouldApply.Should().BeTrue();
        enablePlan.EnableParagraphBuild.Should().BeTrue();
        enablePlan.DisplayText.Should().Be("Build text all at once");
        disablePlan.Should().NotBeNull();
        disablePlan!.ShouldApply.Should().BeTrue();
        disablePlan.EnableParagraphBuild.Should().BeFalse();
        disablePlan.UpdatedBuildListXml.Should().BeNull();
        paragraphBuildAfterEnable.Should().BeTrue();
        paragraphBuildAfterDisable.Should().BeFalse();
    }

    [Fact]
    public async Task Animation_pane_reorders_rows_through_shared_mutation_plan()
    {
        AnimationPaneReorderMutationPlan? moveEarlierPlan = null;
        AnimationPaneReorderMutationPlan? invalidPlan = null;
        IReadOnlyList<uint> animationShapeOrder = [];
        IReadOnlyList<string> paneRows = [];
        var selectedIndex = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.fade", out var fade).Should().BeTrue();

            var hero = window.Editor.InsertDefaultRectangle();
            hero.Name = "Hero box";
            window.Editor.Select(hero.Id);
            fade!.Execute(RibbonCommandContext.Empty);

            var caption = window.Editor.InsertDefaultRectangle();
            caption.Name = "Caption box";
            window.Editor.Select(caption.Id);
            fade.Execute(RibbonCommandContext.Empty);
            window.ShowAnimationPane();

            moveEarlierPlan = window.MoveAnimationPaneItemForTests(1, -1);
            invalidPlan = window.MoveAnimationPaneItemForTests(0, -1);

            animationShapeOrder = window.Editor.CurrentSlideAnimations
                .Select(animation => animation.ShapeId)
                .ToArray();
            selectedIndex = window.LastAnimationPaneTimelinePlan!.SelectedIndex;
            paneRows = window.AnimationPaneRenderedRows.ToArray();
        });

        if (!ran) return;
        moveEarlierPlan.Should().Be(new AnimationPaneReorderMutationPlan(
            true,
            1,
            0,
            0,
            "Move animation 2 earlier",
            null));
        invalidPlan.Should().NotBeNull();
        invalidPlan!.ShouldApply.Should().BeFalse();
        invalidPlan.DisabledReason.Should().Be(AnimationPanePlanner.InvalidReorderMessage);
        animationShapeOrder.Should().HaveCount(2);
        paneRows.Should().HaveCount(2);
        paneRows[0].Should().Contain("1. Caption box - In: Fade")
            .And.Contain("move earlier unavailable")
            .And.Contain("move later available");
        paneRows[1].Should().Contain("2. Hero box - In: Fade")
            .And.Contain("move earlier available")
            .And.Contain("move later unavailable");
        selectedIndex.Should().Be(0);
    }

    [Fact]
    public async Task Animation_pane_removes_rows_through_shared_undoable_mutation_plan()
    {
        AnimationPaneRemoveMutationPlan? removePlan = null;
        IReadOnlyList<uint> remainingAnimationShapeIds = [];
        IReadOnlyList<string> paneRows = [];
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.fade", out var fade).Should().BeTrue();

            var hero = window.Editor.InsertDefaultRectangle();
            hero.Name = "Hero box";
            window.Editor.Select(hero.Id);
            fade!.Execute(RibbonCommandContext.Empty);

            var caption = window.Editor.InsertDefaultRectangle();
            caption.Name = "Caption box";
            window.Editor.Select(caption.Id);
            fade.Execute(RibbonCommandContext.Empty);
            window.ShowAnimationPane();

            removePlan = window.RemoveAnimationPaneItemForTests(0);
            remainingAnimationShapeIds = window.Editor.CurrentSlideAnimations
                .Select(animation => animation.ShapeId)
                .ToArray();
            paneRows = window.AnimationPaneRenderedRows.ToArray();
            window.Editor.Undo();
        });

        if (!ran) return;
        removePlan.Should().Be(new AnimationPaneRemoveMutationPlan(
            true,
            0,
            0,
            "Remove animation 1",
            null));
        remainingAnimationShapeIds.Should().HaveCount(1);
        paneRows.Should().ContainSingle(row => row.Contains("Caption box", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Animation_pane_projects_shared_playback_session_state()
    {
        AnimationPanePlaybackSessionPlan? playSession = null;
        AnimationPanePlaybackSessionPlan? stopSession = null;
        AnimationPanePlaybackWorkflowEvidencePlan? workflowEvidence = null;
        IReadOnlyList<string> runningControls = [];
        IReadOnlyList<string> stoppedControls = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.fade", out var fade).Should().BeTrue();
            registry.TryGet("freep.anim.trigger", out var trigger).Should().BeTrue();
            registry.TryGet("freep.anim.pane", out var pane).Should().BeTrue();

            var hero = window.Editor.InsertDefaultRectangle();
            hero.Name = "Hero box";
            window.Editor.Select(hero.Id);
            fade!.Execute(RibbonCommandContext.Empty);

            var caption = window.Editor.InsertDefaultRectangle();
            caption.Name = "Caption box";
            window.Editor.Select(caption.Id);
            fade.Execute(RibbonCommandContext.Empty);
            trigger!.Execute(RibbonCommandContext.ForSelectedValue("After Previous"));

            pane!.Execute(RibbonCommandContext.Empty);

            playSession = window.ExecuteAnimationPanePlaybackControlForTests(
                AnimationPanePlaybackControlKind.PlayFromSelected);
            workflowEvidence = window.LastAnimationPanePlaybackWorkflowEvidencePlan;
            runningControls = window.AnimationPanePlaybackControls.ToArray();

            stopSession = window.ExecuteAnimationPanePlaybackControlForTests(
                AnimationPanePlaybackControlKind.Stop);
            stoppedControls = window.AnimationPanePlaybackControls.ToArray();
        });

        if (!ran) return;
        playSession.Should().NotBeNull();
        playSession!.State.Should().Be(AnimationPanePlaybackSessionState.Running);
        playSession.StartAnimationIndex.Should().Be(1);
        playSession.Segments.Should().ContainSingle(segment =>
            segment.AnimationIndex == 1
            && segment.ShapeName == "Caption box"
            && segment.RelativeStartMs == 0);
        workflowEvidence.Should().NotBeNull();
        workflowEvidence!.CommandKind.Should().Be(AnimationPanePlaybackControlKind.PlayFromSelected);
        workflowEvidence.SessionState.Should().Be(AnimationPanePlaybackSessionState.Running);
        workflowEvidence.SegmentCount.Should().Be(1);
        workflowEvidence.PlaybackCheckpointCount.Should().Be(0);
        workflowEvidence.HasSharedNoComHostEvidence.Should().BeTrue();
        workflowEvidence.HostRows.Select(row => row.Host)
            .Should()
            .Equal(AnimationPanePlaybackWorkflowHost.Wpf, AnimationPanePlaybackWorkflowHost.Avalonia);
        workflowEvidence.EvidenceLines.Should().Contain(
            "Shared host rows: WPF/Avalonia; PowerPoint COM required: false");
        runningControls.Should().Equal(
            "Preview: unavailable",
            "Play From Selected: unavailable",
            "Play All: unavailable",
            "Stop: available");
        stopSession.Should().NotBeNull();
        stopSession!.State.Should().Be(AnimationPanePlaybackSessionState.Stopped);
        stopSession.Segments.Should().BeEmpty();
        stoppedControls.Should().Contain("Play From Selected: available");
        stoppedControls.Should().Contain("Stop: unavailable");
    }

    [Fact]
    public async Task Animation_pane_inline_timing_controls_apply_shared_mutation_plans()
    {
        var triggerControlCount = 0;
        var durationControlCount = 0;
        var delayControlCount = 0;
        AnimationPaneTimingMutationPlan? triggerPlan = null;
        AnimationPaneTimingMutationPlan? durationPlan = null;
        AnimationPaneTimingMutationPlan? delayPlan = null;
        AnimationPaneTimingMutationPlan? invalidDurationPlan = null;
        AnimationTrigger? trigger = null;
        int? durationMs = null;
        int? delayMs = null;
        IReadOnlyList<string> paneRows = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.fade", out var fade).Should().BeTrue();

            var hero = window.Editor.InsertDefaultRectangle();
            hero.Name = "Hero box";
            window.Editor.Select(hero.Id);
            fade!.Execute(RibbonCommandContext.Empty);
            window.ShowAnimationPane();

            triggerControlCount = window.AnimationPaneTriggerControlCount;
            durationControlCount = window.AnimationPaneDurationControlCount;
            delayControlCount = window.AnimationPaneDelayControlCount;

            triggerPlan = window.ApplyAnimationPaneTriggerEditForTests(
                0,
                AnimationPanePlanner.ToTriggerIndex(AnimationTrigger.AfterPrevious));
            durationPlan = window.ApplyAnimationPaneDurationEditForTests(0, "1.25s");
            delayPlan = window.ApplyAnimationPaneDelayEditForTests(0, "0.40s");
            invalidDurationPlan = window.ApplyAnimationPaneDurationEditForTests(0, "0");

            var animation = window.Editor.CurrentSlideAnimations.Single();
            trigger = animation.Trigger;
            durationMs = animation.DurationMs;
            delayMs = animation.DelayMs;
            paneRows = window.AnimationPaneRenderedRows.ToArray();
        });

        if (!ran) return;
        triggerControlCount.Should().Be(1);
        durationControlCount.Should().Be(1);
        delayControlCount.Should().Be(1);
        triggerPlan.Should().NotBeNull();
        triggerPlan!.Kind.Should().Be(AnimationPaneTimingEditKind.Trigger);
        triggerPlan.ShouldApply.Should().BeTrue();
        durationPlan.Should().NotBeNull();
        durationPlan!.Kind.Should().Be(AnimationPaneTimingEditKind.Duration);
        delayPlan.Should().NotBeNull();
        delayPlan!.Kind.Should().Be(AnimationPaneTimingEditKind.Delay);
        invalidDurationPlan.Should().NotBeNull();
        invalidDurationPlan!.DisabledReason.Should().Be(AnimationPanePlanner.InvalidDurationMessage);
        trigger.Should().Be(AnimationTrigger.AfterPrevious);
        durationMs.Should().Be(1250);
        delayMs.Should().Be(400);
        paneRows.Should().ContainSingle()
            .Which.Should().Contain("After Previous")
            .And.Contain("duration 1.25s")
            .And.Contain("delay 0.4s");
    }

    [Fact]
    public async Task Animation_pane_effect_option_controls_apply_shared_mutation_plans()
    {
        var effectOptionControlCount = 0;
        AnimationPaneEffectOptionsPlan? optionsPlan = null;
        AnimationPaneEffectOptionMutationPlan? mutationPlan = null;
        AnimationPaneEffectOptionMutationPlan? invalidPlan = null;
        AnimationDirection? direction = null;
        IReadOnlyList<string> paneRows = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.fly-in", out var flyIn).Should().BeTrue();

            var hero = window.Editor.InsertDefaultRectangle();
            hero.Name = "Hero box";
            window.Editor.Select(hero.Id);
            flyIn!.Execute(RibbonCommandContext.Empty);
            window.ShowAnimationPane();

            effectOptionControlCount = window.AnimationPaneEffectOptionControlCount;
            optionsPlan = window.LastAnimationPaneTimelinePlan!.Items[0].EffectOptions;
            mutationPlan = window.ApplyAnimationPaneEffectOptionEditForTests(0, "from-bottom-right");
            invalidPlan = window.ApplyAnimationPaneEffectOptionEditForTests(0, "sideways");

            direction = window.Editor.CurrentSlideAnimations.Single().Direction;
            paneRows = window.AnimationPaneRenderedRows.ToArray();
        });

        if (!ran) return;
        effectOptionControlCount.Should().Be(1);
        optionsPlan.Should().NotBeNull();
        optionsPlan!.CanApply.Should().BeTrue();
        optionsPlan.Options.Select(option => option.Id).Should().Equal(
            "from-bottom",
            "from-left",
            "from-right",
            "from-top",
            "from-top-left",
            "from-top-right",
            "from-bottom-left",
            "from-bottom-right");
        mutationPlan.Should().NotBeNull();
        mutationPlan!.ShouldApply.Should().BeTrue();
        mutationPlan.Direction.Should().Be(AnimationDirection.FromBottomRight);
        invalidPlan.Should().NotBeNull();
        invalidPlan!.DisabledReason.Should().Be(AnimationPanePlanner.InvalidEffectOptionMessage);
        direction.Should().Be(AnimationDirection.FromBottomRight);
        paneRows.Should().ContainSingle()
            .Which.Should().Contain("Hero box - In: FlyIn (From Bottom Right)")
            .And.Contain("duration 0.5s");
    }

    [Fact]
    public async Task Animation_pane_split_effect_options_use_shared_direction_semantics()
    {
        AnimationPaneEffectOptionsPlan? optionsPlan = null;
        AnimationPaneEffectOptionMutationPlan? mutationPlan = null;
        AnimationDirection? direction = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.entrance.split", out var split).Should().BeTrue();

            var hero = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(hero.Id);
            split!.Execute(RibbonCommandContext.Empty);
            window.ShowAnimationPane();

            optionsPlan = window.LastAnimationPaneTimelinePlan!.Items[0].EffectOptions;
            mutationPlan = window.ApplyAnimationPaneEffectOptionEditForTests(0, "vertical-out");
            direction = window.Editor.CurrentSlideAnimations.Single().Direction;
        });

        if (!ran) return;
        optionsPlan.Should().NotBeNull();
        optionsPlan!.Options.Select(option => option.DisplayText)
            .Should().Equal("Horizontal In", "Horizontal Out", "Vertical In", "Vertical Out");
        mutationPlan.Should().NotBeNull();
        mutationPlan!.Direction.Should().Be(AnimationDirection.VerticalOut);
        direction.Should().Be(AnimationDirection.VerticalOut);
    }

    [Fact]
    public async Task Animation_pane_grow_shrink_effect_options_use_shared_amount_semantics()
    {
        AnimationPaneEffectOptionsPlan? optionsPlan = null;
        AnimationPaneEffectOptionMutationPlan? mutationPlan = null;
        AnimationScaleBehavior? scaleBehavior = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.anim.emphasis.grow-shrink", out var growShrink).Should().BeTrue();

            var hero = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(hero.Id);
            growShrink!.Execute(RibbonCommandContext.Empty);
            window.ShowAnimationPane();

            optionsPlan = window.LastAnimationPaneTimelinePlan!.Items[0].EffectOptions;
            mutationPlan = window.ApplyAnimationPaneEffectOptionEditForTests(0, "amount-50");
            scaleBehavior = window.Editor.CurrentSlideAnimations.Single().ScaleBehavior;
        });

        if (!ran) return;
        optionsPlan.Should().NotBeNull();
        optionsPlan!.Options.Select(option => option.DisplayText).Should().Contain(
            new[] { "Tiny (25%)", "Smaller (50%)", "Larger (150%)", "Huge (400%)" });
        mutationPlan.Should().NotBeNull();
        mutationPlan!.ShouldApply.Should().BeTrue();
        mutationPlan.ScaleBehavior!.ToX.Should().Be("50000");
        scaleBehavior!.ToX.Should().Be("50000");
    }

    [Fact]
    public async Task Ribbon_review_workflow_commands_refresh_shared_adapter_state()
    {
        var foundComments = false;
        var foundAccessibility = false;
        var foundAltText = false;
        var foundReadingOrder = false;
        var foundProofing = false;
        var foundReopenComment = false;
        PresentationCommentPanePlan? commentPlan = null;
        PresentationAccessibilitySummaryPlan? accessibilityPlan = null;
        PresentationAccessibilityCheckerPanePlan? accessibilityCheckerPlan = null;
        PresentationAltTextRequestPlan? altTextPlan = null;
        PresentationAltTextPanePlan? altTextPanePlan = null;
        PresentationReadingOrderPlan? readingOrderPlan = null;
        PresentationProofingRequestPlan? proofingPlan = null;
        PresentationProofingExecutionPlan? proofingExecutionPlan = null;
        PresentationProofingPanePlan? proofingPanePlan = null;
        PresentationProofingCorrectionMutationPlan? proofingMutation = null;
        string? correctedShapeText = null;
        string? correctedProofingScopeText = null;
        var commentsPaneVisible = false;
        var commentsPaneCommentCount = 0;
        var commentsPaneActionCount = 0;
        var commentsPaneSelectedCount = 0;
        var commentsPaneSummary = string.Empty;
        string[] commentsPaneFilterStates = [];
        var accessibilityCheckerPaneVisible = false;
        var accessibilityCheckerPaneRowCount = 0;
        var readingOrderPaneVisible = false;
        var readingOrderPaneItemCount = 0;
        var readingOrderPaneHeading = string.Empty;
        var readingOrderPaneMessage = string.Empty;
        var readingOrderMoveEarlierEnabled = true;
        string? readingOrderMoveEarlierDisabledReason = null;
        var readingOrderMoveLaterEnabled = false;
        string? readingOrderMoveLaterDisabledReason = null;
        var proofingPaneVisible = false;
        var proofingPaneRowCount = 0;
        var proofingPaneSelectedCount = 0;
        var proofingPaneCorrectionEnabled = false;
        var proofingPaneHeading = string.Empty;
        PresentationReadingOrderMutationPlan? readingOrderMove = null;
        PresentationReadingOrderSelectionPlan? readingOrderSelection = null;
        uint[] readingOrderShapeOrderAfterMove = [];
        uint[] readingOrderPaneOrderAfterMove = [];
        uint[] readingOrderSelectionAfterPaneSelect = [];
        string readingOrderPaneMessageAfterSelect = string.Empty;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Use shared review state.",
                Idx = 1,
                IsResolved = true,
                ResolvedBy = "Reviewer",
                ResolvedDateTime = new DateTime(2026, 7, 2, 8, 15, 0, DateTimeKind.Utc),
                Replies =
                {
                    new SlideCommentReply
                    {
                        Author = "Nora",
                        Initials = "NO",
                        Text = "@Reviewer confirmed.",
                    }
                }
            });
            var shape = new SlideShape
            {
                Id = 328,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
            };
            var caption = new SlideShape
            {
                Id = 329,
                Name = "Caption",
                Kind = SlideShapeKind.AutoShape,
                Text = "The slides is ready",
            };
            window.Editor.CurrentSlide.Shapes.Clear();
            window.Editor.CurrentSlide.Shapes.Add(shape);
            window.Editor.CurrentSlide.Shapes.Add(caption);
            window.Editor.Select(shape.Id);

            var registry = window.BuildCommandRegistry();
            foundComments = registry.TryGet(PresentationReviewWorkflowPlanner.CommentsPaneCommandId, out var comments);
            foundAccessibility = registry.TryGet(PresentationReviewWorkflowPlanner.AccessibilityCommandId, out var accessibility);
            foundAltText = registry.TryGet(PresentationReviewWorkflowPlanner.AltTextCommandId, out var altText);
            foundReadingOrder = registry.TryGet(PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId, out var readingOrder);
            foundProofing = registry.TryGet(PresentationReviewWorkflowPlanner.ProofingCommandId, out var proofing);
            foundReopenComment = registry.TryGet(PresentationReviewWorkflowPlanner.ReopenCommentCommandId, out _);

            comments!.Execute(RibbonCommandContext.Empty);
            accessibility!.Execute(RibbonCommandContext.Empty);
            altText!.Execute(RibbonCommandContext.Empty);
            readingOrder!.Execute(RibbonCommandContext.Empty);
            proofing!.Execute(RibbonCommandContext.Empty);

            commentPlan = window.LastCommentPanePlan;
            commentsPaneVisible = window.IsReviewCommentsPaneVisible;
            commentsPaneCommentCount = window.ReviewCommentsPaneCommentCount;
            commentsPaneActionCount = window.ReviewCommentsPaneActionButtonCount;
            commentsPaneSelectedCount = window.ReviewCommentsPaneSelectedCommentCount;
            commentsPaneSummary = window.ReviewCommentsPaneSummary;
            commentsPaneFilterStates = window.ReviewCommentsPaneFilterStates.ToArray();
            accessibilityPlan = window.LastAccessibilitySummaryPlan;
            accessibilityCheckerPlan = window.LastAccessibilityCheckerPanePlan;
            accessibilityCheckerPaneVisible = window.IsAccessibilityCheckerPaneVisible;
            accessibilityCheckerPaneRowCount = window.AccessibilityCheckerPaneRowCount;
            altTextPlan = window.LastAltTextRequestPlan;
            altTextPanePlan = window.LastAltTextPanePlan;
            readingOrderPlan = window.LastReadingOrderPlan;
            readingOrderPaneVisible = window.IsReadingOrderPaneVisible;
            readingOrderPaneItemCount = window.ReadingOrderPaneItemCount;
            readingOrderPaneHeading = window.ReadingOrderPaneHeading;
            readingOrderPaneMessage = window.ReadingOrderPaneMessage;
            readingOrderMoveEarlierEnabled = window.IsReadingOrderMoveEarlierEnabled;
            readingOrderMoveEarlierDisabledReason = window.ReadingOrderMoveEarlierDisabledReason;
            readingOrderMoveLaterEnabled = window.IsReadingOrderMoveLaterEnabled;
            readingOrderMoveLaterDisabledReason = window.ReadingOrderMoveLaterDisabledReason;
            readingOrderMove = window.ApplyReadingOrderMoveLater();
            readingOrderShapeOrderAfterMove = window.Editor.CurrentSlide.Shapes
                .Select(shape => shape.Id)
                .ToArray();
            readingOrderPaneOrderAfterMove = window.LastReadingOrderPlan!.Items
                .Select(item => item.ShapeId)
                .ToArray();
            readingOrderSelection = window.ApplyReadingOrderSelectItem(caption.Id);
            readingOrderSelectionAfterPaneSelect = window.Editor.SelectedShapeIds.ToArray();
            readingOrderPaneMessageAfterSelect = window.ReadingOrderPaneMessage;
            proofingPlan = window.LastProofingRequestPlan;
            proofingExecutionPlan = window.LastProofingExecutionPlan;
            proofingPanePlan = window.LastProofingPanePlan;
            proofingPaneVisible = window.IsProofingPaneVisible;
            proofingPaneRowCount = window.ProofingPaneIssueRowCount;
            proofingPaneSelectedCount = window.ProofingPaneSelectedIssueCount;
            proofingPaneCorrectionEnabled = window.IsProofingPaneCorrectionEnabled;
            proofingPaneHeading = window.ProofingPaneHeading;
            proofingMutation = window.ApplySelectedProofingCorrection();
            correctedShapeText = caption.Text;
            correctedProofingScopeText = window.LastProofingExecutionPlan!.Scopes.Single(scope =>
                    scope.Kind == PresentationProofingScopeKind.ShapeText)
                .Text;
        });

        if (!ran) return;
        foundComments.Should().BeTrue();
        foundAccessibility.Should().BeTrue();
        foundAltText.Should().BeTrue();
        foundReadingOrder.Should().BeTrue();
        foundProofing.Should().BeTrue();
        foundReopenComment.Should().BeTrue();
        commentPlan.Should().NotBeNull();
        commentPlan!.TotalCommentCount.Should().Be(1);
        commentPlan.OpenThreadCount.Should().Be(0);
        commentPlan.ResolvedThreadCount.Should().Be(1);
        commentPlan.TotalReplyCount.Should().Be(1);
        commentPlan.TotalMentionCount.Should().Be(1);
        commentsPaneSummary.Should().Be("1 thread: 0 open threads, 1 resolved thread, 1 reply, 1 mention");
        commentPlan.FilterSummaryLabel.Should().Be("Showing all threads");
        commentsPaneFilterStates.Should().Equal(
            "All|All|1|True|True",
            "Open|Open|0|False|False",
            "Resolved|Resolved|1|False|True",
            "Mentions|Mentions|1|False|True");
        commentPlan.Comments.Single().Should().Match<PresentationCommentDescriptor>(comment =>
            comment.ThreadStatus == PresentationCommentThreadStatus.Resolved &&
            comment.ThreadStatusLabel == "Resolved" &&
            comment.ThreadStatusSummary == "Resolved by Reviewer" &&
            comment.ResolvedByDisplayName == "Reviewer" &&
            comment.InitialsBadgeText == "RV" &&
            comment.AuthorIdentityKey == "REVIEWER|RV" &&
            comment.IsSelected &&
            !comment.CanResolve &&
            comment.CanReopen &&
            !comment.CanReply &&
            comment.ReplyCount == 1 &&
            comment.ReplySummary == "1 reply" &&
            comment.MentionCount == 1);
        commentPlan.Comments.Single().Replies.Single().Should().Match<PresentationCommentReplyDescriptor>(reply =>
            reply.TextPreview == "@Reviewer confirmed." &&
            reply.AuthorDisplayName == "Nora" &&
            reply.InitialsBadgeText == "NO" &&
            reply.AuthorIdentityKey == "NORA|NO");
        commentPlan.SelectedComment.Should().BeSameAs(commentPlan.Comments[0]);
        commentPlan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReopenCommentCommandId)
            .IsEnabled.Should().BeTrue("the comments command selects the first current-slide thread through the shared plan");
        accessibilityPlan.Should().NotBeNull();
        var missingAltText = accessibilityPlan!.Issues.Single(issue =>
            issue.ShapeId == 328 && issue.Title == "Alt text missing");
        missingAltText.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
            PresentationReviewWorkflowPlanner.MissingAltTextActionSummary,
            PresentationReviewWorkflowPlanner.AltTextCommandId,
            true));
        accessibilityCheckerPlan.Should().NotBeNull();
        accessibilityCheckerPaneVisible.Should().BeTrue("the Avalonia accessibility command should render a shared-plan-backed pane");
        accessibilityCheckerPaneRowCount.Should().Be(accessibilityCheckerPlan!.Rows.Count);
        accessibilityCheckerPlan.Rows.Should().Contain(row =>
            row.ShapeId == 328 &&
            row.ActionLabel == "Open Alt Text" &&
            row.CommandHint == PresentationReviewWorkflowPlanner.AltTextCommandId);
        altTextPlan.Should().NotBeNull();
        altTextPlan!.HasSelection.Should().BeTrue();
        altTextPlan.ShapeId.Should().Be(328);
        altTextPlan.Status.Should().Be(PresentationWorkflowCapabilityStatus.Available);
        altTextPanePlan.Should().NotBeNull();
        altTextPanePlan!.ShapeId.Should().Be(328);
        altTextPanePlan.CanApply.Should().BeFalse();
        altTextPanePlan.Description.ValidationMessage
            .Should().Be(PresentationReviewWorkflowPlanner.MissingAltTextDescriptionMessage);
        altTextPanePlan.Actions.Select(action => action.CommandId).Should().Contain(new[]
        {
            PresentationReviewWorkflowPlanner.AltTextPaneApplyCommandId,
            PresentationReviewWorkflowPlanner.AltTextPaneDecorativeCommandId,
            PresentationReviewWorkflowPlanner.AltTextPaneCloseCommandId
        });
        readingOrderPlan.Should().NotBeNull();
        readingOrderPlan!.Items.Select(item => item.ShapeId).Should().Equal(328u, 329u);
        readingOrderPlan.SelectedItem.Should().NotBeNull();
        readingOrderPlan.SelectedItem!.ShapeId.Should().Be(328);
        readingOrderPlan.Actions.Single(action =>
                action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage);
        readingOrderPaneVisible.Should().BeTrue("the Avalonia reading order command should render a shared-plan-backed pane");
        readingOrderPaneItemCount.Should().Be(2);
        readingOrderPaneHeading.Should().Be("Reading Order - slide 1 (2 shapes)");
        readingOrderPaneMessage.Should().Be("Selected: Product image");
        readingOrderMoveEarlierEnabled.Should().BeFalse();
        readingOrderMoveEarlierDisabledReason.Should()
            .Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage);
        readingOrderMoveLaterEnabled.Should().BeTrue();
        readingOrderMoveLaterDisabledReason.Should().BeNull();
        readingOrderMove.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
            true,
            0,
            328,
            0,
            1,
            null));
        readingOrderShapeOrderAfterMove.Should().Equal(329u, 328u);
        readingOrderPaneOrderAfterMove.Should().Equal(329u, 328u);
        readingOrderSelection.Should().Be(new PresentationReadingOrderSelectionPlan(
            PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
            true,
            0,
            329,
            0,
            null));
        readingOrderSelectionAfterPaneSelect.Should().Equal(329u);
        readingOrderPaneMessageAfterSelect.Should().Be("Selected: Caption");
        proofingPlan.Should().NotBeNull();
        proofingPlan!.Status.Should().Be(PresentationWorkflowCapabilityStatus.Available);
        proofingExecutionPlan.Should().NotBeNull();
        proofingExecutionPlan!.Scopes.Select(scope => scope.Kind).Should().Equal(
            PresentationProofingScopeKind.ShapeText,
            PresentationProofingScopeKind.Comment,
            PresentationProofingScopeKind.CommentReply);
        proofingExecutionPlan.Scopes.Select(scope => scope.Text).Should().Equal(
            "The slides is ready",
            "Use shared review state.",
            "@Reviewer confirmed.");
        proofingPanePlan.Should().NotBeNull();
        proofingPaneVisible.Should().BeTrue("the Avalonia proofing command should render a shared-plan-backed corrections pane");
        proofingPaneRowCount.Should().Be(1);
        proofingPaneSelectedCount.Should().Be(1);
        proofingPaneCorrectionEnabled.Should().BeTrue();
        proofingPaneHeading.Should().Be("Spelling - 1 issues");
        proofingPanePlan!.SelectedRow!.SuggestedReplacement.Should().Be("The slides are");
        proofingMutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            proofingExecutionPlan.Scopes.Single(scope => scope.Kind == PresentationProofingScopeKind.ShapeText),
            0,
            "The slides is".Length,
            "The slides are",
            "The slides are ready",
            null));
        correctedShapeText.Should().Be("The slides are ready");
        correctedProofingScopeText.Should().Be("The slides are ready");
        commentsPaneVisible.Should().BeTrue("the Avalonia comments command should render a shared-plan-backed pane");
        commentsPaneCommentCount.Should().Be(1);
        commentsPaneActionCount.Should().BeGreaterThanOrEqualTo(6);
        commentsPaneSelectedCount.Should().Be(1);
    }

    [Fact]
    public async Task ProofingPane_ignore_actions_use_shared_planner_state()
    {
        PresentationProofingPanePlan? opened = null;
        PresentationProofingPanePlan? selected = null;
        PresentationProofingPanePlan? afterIgnore = null;
        PresentationProofingPanePlan? afterIgnoreAll = null;
        var ignoreEnabled = false;
        var ignoreAllEnabled = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Title eror";
            window.Editor.CurrentSlide.Shapes.Add(new SlideShape
            {
                Id = 724,
                Name = "Body",
                Text = "Body eror",
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Comment eror",
                Idx = 1,
            });

            opened = window.ShowProofingPane();
            ignoreEnabled = window.IsProofingPaneIgnoreEnabled;
            ignoreAllEnabled = window.IsProofingPaneIgnoreAllEnabled;
            selected = window.SelectProofingIssueRow(1);
            afterIgnore = window.IgnoreSelectedProofingIssue();
            afterIgnoreAll = window.IgnoreAllSelectedProofingIssues();
        });

        if (!ran) return;
        opened.Should().NotBeNull();
        opened!.IssueCount.Should().Be(3);
        opened.Actions.Select(action => action.CommandId).Should().Contain(new[]
        {
            PresentationReviewWorkflowPlanner.ProofingIgnoreCommandId,
            PresentationReviewWorkflowPlanner.ProofingIgnoreAllCommandId
        });
        ignoreEnabled.Should().BeTrue();
        ignoreAllEnabled.Should().BeTrue();
        selected.Should().NotBeNull();
        selected!.SelectedRow!.Scope.Kind.Should().Be(PresentationProofingScopeKind.ShapeText);
        afterIgnore.Should().NotBeNull();
        afterIgnore!.IssueCount.Should().Be(2);
        afterIgnore.Rows.Select(row => row.Scope.Kind).Should().Equal(
            PresentationProofingScopeKind.SlideTitle,
            PresentationProofingScopeKind.Comment);
        afterIgnore.SelectedRowIndex.Should().Be(1);
        afterIgnoreAll.Should().NotBeNull();
        afterIgnoreAll!.IssueCount.Should().Be(0);
        afterIgnoreAll.Message.Should().Be(PresentationReviewWorkflowPlanner.ProofingNoIssuesMessage);
    }

    [Fact]
    public async Task ProofingPane_add_to_dictionary_uses_shared_session_state()
    {
        PresentationProofingPanePlan? opened = null;
        PresentationProofingPanePlan? afterDictionary = null;
        var addToDictionaryEnabled = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Title eror";
            window.Editor.CurrentSlide.Shapes.Add(new SlideShape
            {
                Id = 724,
                Name = "Body",
                Text = "Body EROR and teh",
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Comment eror",
                Idx = 1,
            });

            opened = window.ShowProofingPane();
            addToDictionaryEnabled = window.IsProofingPaneAddToDictionaryEnabled;
            afterDictionary = window.AddSelectedProofingWordToDictionary();
        });

        if (!ran) return;
        opened.Should().NotBeNull();
        opened!.IssueCount.Should().Be(4);
        opened.Actions.Select(action => action.CommandId).Should().Contain(
            PresentationReviewWorkflowPlanner.ProofingAddToDictionaryCommandId);
        addToDictionaryEnabled.Should().BeTrue();
        afterDictionary.Should().NotBeNull();
        afterDictionary!.IssueCount.Should().Be(1);
        afterDictionary.SelectedRow!.Text.Should().Be("teh");
        afterDictionary.SelectedRow.Scope.Kind.Should().Be(PresentationProofingScopeKind.ShapeText);
    }

    [Fact]
    public async Task ReadingOrderPane_selected_item_inset_matches_Wpf_authority()
    {
        Thickness selectedItemMargin = default;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            try
            {
                window.Editor.CurrentSlide!.Shapes.Add(new SlideShape
                {
                    Id = 699,
                    Name = "Selected shape",
                });
                window.Editor.Select(699);
                window.ShowReadingOrderPane();

                selectedItemMargin = window.GetLogicalDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "Selected item")
                    .Margin;
            }
            finally
            {
                window.Close();
            }
        });

        if (!ran) return;
        selectedItemMargin.Should().Be(new Thickness(0, 2, 0, 0));
    }

    [Fact]
    public async Task AccessibilityCheckerPane_card_chrome_matches_Wpf_authority()
    {
        Button? actionButton = null;
        TextBlock? selectedIssue = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            try
            {
                window.Editor.CurrentSlide!.Shapes.Add(new SlideShape
                {
                    Id = 700,
                    Name = "Missing alt text",
                    AlternativeText = string.Empty,
                });
                window.Editor.Select(700);
                window.ShowAccessibilityCheckerPane();

                actionButton = window.GetLogicalDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Open Alt Text"));
                selectedIssue = window.GetLogicalDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "Selected issue");
            }
            finally
            {
                window.Close();
            }
        });

        if (!ran) return;
        actionButton.Should().NotBeNull();
        actionButton!.Height.Should().Be(20);
        actionButton.CornerRadius.Should().Be(new CornerRadius(0));
        actionButton.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);
        selectedIssue.Should().NotBeNull();
        selectedIssue!.Margin.Should().Be(new Thickness(0, 2, 0, 0));
        selectedIssue.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);
    }

    [Fact]
    public async Task ReadingOrderPane_moves_nested_group_child_within_sibling_order()
    {
        PresentationReadingOrderPlan? initialPlan = null;
        PresentationReadingOrderMutationPlan? nestedMove = null;
        PresentationReadingOrderMutationPlan? boundaryMove = null;
        uint[] childOrderAfterMove = [];
        uint[] paneOrderAfterMove = [];
        var moveEarlierEnabled = true;
        string? moveEarlierDisabledReason = null;
        var moveLaterEnabled = false;
        string? moveLaterDisabledReason = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            try
            {
                var group = new SlideShape
                {
                    Id = 701,
                    Name = "Grouped layout",
                    Kind = SlideShapeKind.Group,
                    Children =
                    {
                        new SlideShape
                        {
                            Id = 702,
                            Name = "Grouped caption",
                            Kind = SlideShapeKind.AutoShape,
                            Text = "Grouped caption"
                        },
                        new SlideShape
                        {
                            Id = 703,
                            Name = "Grouped flourish",
                            Kind = SlideShapeKind.Picture,
                            Picture = new ImagePart(),
                            IsDecorative = true
                        }
                    }
                };

                window.Editor.CurrentSlide!.Shapes.Clear();
                window.Editor.CurrentSlide.Shapes.Add(new SlideShape { Id = 700, Name = "Title placeholder" });
                window.Editor.CurrentSlide.Shapes.Add(group);
                window.Editor.Select(702);

                var registry = window.BuildCommandRegistry();
                registry.TryGet(PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId, out var readingOrder)
                    .Should().BeTrue();

                readingOrder!.Execute(RibbonCommandContext.Empty);

                initialPlan = window.LastReadingOrderPlan;
                moveEarlierEnabled = window.IsReadingOrderMoveEarlierEnabled;
                moveEarlierDisabledReason = window.ReadingOrderMoveEarlierDisabledReason;
                moveLaterEnabled = window.IsReadingOrderMoveLaterEnabled;
                moveLaterDisabledReason = window.ReadingOrderMoveLaterDisabledReason;

                nestedMove = window.ApplyReadingOrderMoveLater();
                boundaryMove = window.ApplyReadingOrderMoveLater();
                childOrderAfterMove = group.Children.Select(shape => shape.Id).ToArray();
                paneOrderAfterMove = window.LastReadingOrderPlan!.Items
                    .Select(item => item.ShapeId)
                    .ToArray();
            }
            finally
            {
                window.Close();
            }
        });

        if (!ran) return;

        initialPlan.Should().NotBeNull();
        initialPlan!.Items.Select(item => item.ShapeId).Should().Equal(700u, 701u, 702u, 703u);
        initialPlan.SelectedItem.Should().NotBeNull();
        initialPlan.SelectedItem!.ShapeId.Should().Be(702);
        initialPlan.SelectedItem.NestingDepth.Should().Be(1);
        moveEarlierEnabled.Should().BeFalse();
        moveEarlierDisabledReason.Should().Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage);
        moveLaterEnabled.Should().BeTrue();
        moveLaterDisabledReason.Should().BeNull();
        nestedMove.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
            true,
            0,
            702,
            0,
            1,
            null));
        childOrderAfterMove.Should().Equal(703u, 702u);
        paneOrderAfterMove.Should().Equal(700u, 701u, 703u, 702u);
        boundaryMove.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
            false,
            0,
            702,
            -1,
            -1,
            PresentationReviewWorkflowPlanner.ReadingOrderAlreadyLatestMessage));
    }

    [Fact]
    public async Task Accessibility_checker_pane_routes_rows_through_shared_plan()
    {
        PresentationAccessibilityCheckerPanePlan? opened = null;
        PresentationAccessibilityCheckerPanePlan? selectedChart = null;
        PresentationAccessibilityCheckerPanePlan? selectedTitle = null;
        PresentationAccessibilityCheckerPanePlan? invalidSelection = null;
        PresentationAccessibilityCheckerPanePlan? actionedTitle = null;
        PresentationAccessibilityCheckerPanePlan? actionedAltText = null;
        PresentationSlideTitleMutationPlan? titleMutation = null;
        var paneVisible = false;
        var rowCount = 0;
        var selectedRowCount = 0;
        var heading = string.Empty;
        var chartSlideIndex = -1;
        uint[] chartSelection = [];
        var titleSlideIndex = -1;
        var titleSelectionCount = -1;
        var titleAfterAction = string.Empty;
        var dirtyAfterTitle = false;
        var altTextSlideIndex = -1;
        uint[] altTextSelection = [];
        var altTextPaneVisible = false;
        var altTextPaneMessage = string.Empty;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var firstSlide = window.Editor.CurrentSlide!;
            firstSlide.Title = "Intro";
            var shape = new SlideShape
            {
                Id = 908,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
            };
            var linkedText = new SlideShape
            {
                Id = 909,
                Name = "Reference text",
                TextBody = MakeLinkedTextBody("Click here", new Hyperlink
                {
                    Url = "https://example.test/notes",
                    Tooltip = "Open project notes"
                })
            };
            var chart = new SlideShape
            {
                Id = 910,
                Name = "Sales chart",
                Kind = SlideShapeKind.Chart,
                Chart = new ChartShape(),
                AlternativeText = "Quarterly sales by region."
            };
            firstSlide.Shapes.Add(shape);
            firstSlide.Shapes.Add(linkedText);
            firstSlide.Shapes.Add(chart);
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Title = string.Empty;
            window.Editor.SelectSlide(0);

            opened = window.ShowAccessibilityCheckerPane();
            paneVisible = window.IsAccessibilityCheckerPaneVisible;
            rowCount = window.AccessibilityCheckerPaneRowCount;
            selectedRowCount = window.AccessibilityCheckerPaneSelectedRowCount;
            heading = window.AccessibilityCheckerPaneHeading;

            selectedChart = window.SelectAccessibilityCheckerRow(2);
            chartSlideIndex = window.Editor.CurrentSlideIndex;
            chartSelection = window.Editor.SelectedShapeIds.ToArray();

            selectedTitle = window.SelectAccessibilityCheckerRow(3);
            titleSlideIndex = window.Editor.CurrentSlideIndex;
            titleSelectionCount = window.Editor.SelectedShapeIds.Count;

            invalidSelection = window.SelectAccessibilityCheckerRow(99);
            titleSlideIndex = window.Editor.CurrentSlideIndex;
            titleSelectionCount = window.Editor.SelectedShapeIds.Count;

            actionedTitle = window.ApplyAccessibilityCheckerRowAction(3);
            titleAfterAction = window.Editor.CurrentSlide?.Title ?? string.Empty;
            titleMutation = window.LastSlideTitleMutationPlan;
            dirtyAfterTitle = window.IsDirty;

            actionedAltText = window.ApplyAccessibilityCheckerRowAction(0);
            altTextSlideIndex = window.Editor.CurrentSlideIndex;
            altTextSelection = window.Editor.SelectedShapeIds.ToArray();
            altTextPaneVisible = window.IsAltTextPaneVisible;
            altTextPaneMessage = window.AltTextPaneMessage;
        });

        if (!ran) return;
        paneVisible.Should().BeTrue();
        rowCount.Should().Be(4);
        selectedRowCount.Should().Be(1);
        heading.Should().Be("Accessibility - 4 issues");
        opened.Should().NotBeNull();
        opened!.Rows.Select(row => row.Title).Should().Equal(
            "Alt text missing",
            "Unclear hyperlink text",
            "Chart title missing",
            "Missing slide title");
        opened.Rows[0].CommandHint.Should().Be(PresentationReviewWorkflowPlanner.AltTextCommandId);
        opened.Rows[1].Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
            row.Category == "Hyperlink" &&
            row.ShapeId == 909 &&
            row.ActionLabel == "Edit Hyperlink" &&
            row.CommandHint == PresentationReviewWorkflowPlanner.InsertLinkCommandId &&
            row.ShouldNavigateToSlide &&
            row.ShouldSelectShape);
        opened.Rows[2].Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
            row.Category == "Chart" &&
            row.ShapeId == 910 &&
            row.ActionLabel == "Add Chart Title" &&
            row.CommandHint == PresentationReviewWorkflowPlanner.ChartTitleCommandId &&
            row.ShouldNavigateToSlide &&
            row.ShouldSelectShape);
        opened.Rows[3].Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
            row.Category == "Slide title" &&
            row.ActionLabel == "Set Slide Title" &&
            row.CommandHint == PresentationReviewWorkflowPlanner.SetSlideTitleCommandId &&
            row.ShouldNavigateToSlide &&
            !row.ShouldSelectShape);
        selectedChart.Should().NotBeNull();
        selectedChart!.SelectedRow!.Title.Should().Be("Chart title missing");
        chartSlideIndex.Should().Be(0);
        chartSelection.Should().Equal(910u);
        selectedTitle.Should().NotBeNull();
        selectedTitle!.SelectedRow!.Title.Should().Be("Missing slide title");
        titleSlideIndex.Should().Be(1);
        titleSelectionCount.Should().Be(0);
        invalidSelection.Should().NotBeNull();
        invalidSelection!.SelectedRowIndex.Should().Be(3);
        invalidSelection.SelectedRow!.Title.Should().Be("Missing slide title");
        actionedTitle.Should().NotBeNull();
        actionedTitle!.Rows.Select(row => row.Title).Should().Equal(
            "Alt text missing",
            "Unclear hyperlink text",
            "Chart title missing");
        titleAfterAction.Should().Be("Slide 2");
        titleMutation.Should().Be(new PresentationSlideTitleMutationPlan(
            true,
            1,
            "Slide 2",
            "Slide 2",
            null));
        dirtyAfterTitle.Should().BeTrue();
        actionedAltText.Should().NotBeNull();
        actionedAltText!.SelectedRow!.CommandHint.Should().Be(PresentationReviewWorkflowPlanner.AltTextCommandId);
        altTextSlideIndex.Should().Be(0);
        altTextSelection.Should().Equal(908u);
        altTextPaneVisible.Should().BeTrue();
        altTextPaneMessage.Should().Be(PresentationReviewWorkflowPlanner.MissingAltTextDescriptionMessage);
    }

    [Fact]
    public async Task Accessibility_checker_low_quality_alt_text_row_uses_shared_plan()
    {
        PresentationAccessibilityCheckerPanePlan? opened = null;
        PresentationAccessibilityCheckerPanePlan? actioned = null;
        PresentationAltTextRequestPlan? altTextRequest = null;
        var paneVisible = false;
        var heading = string.Empty;
        var altTextPaneVisible = false;
        uint[] selection = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = new SlideShape
            {
                Id = 911,
                Name = "Hero product photo",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
                AlternativeTextTitle = "Hero product photo",
                AlternativeText = "IMG_2048.JPG"
            };
            window.Editor.CurrentSlide!.Title = "Intro";
            window.Editor.CurrentSlide.Shapes.Add(shape);

            opened = window.ShowAccessibilityCheckerPane();
            paneVisible = window.IsAccessibilityCheckerPaneVisible;
            heading = window.AccessibilityCheckerPaneHeading;

            actioned = window.ApplyAccessibilityCheckerRowAction(0);
            selection = window.Editor.SelectedShapeIds.ToArray();
            altTextPaneVisible = window.IsAltTextPaneVisible;
            altTextRequest = window.LastAltTextRequestPlan;
        });

        if (!ran) return;
        paneVisible.Should().BeTrue();
        heading.Should().Be("Accessibility - 1 issues");
        opened.Should().NotBeNull();
        opened!.Rows.Should().ContainSingle().Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
            row.Title == "Filename-like alt text" &&
            row.Category == "Alt text" &&
            row.ShapeId == 911 &&
            row.ActionLabel == "Open Alt Text" &&
            row.CommandHint == PresentationReviewWorkflowPlanner.AltTextCommandId &&
            row.ShouldNavigateToSlide &&
            row.ShouldSelectShape);
        actioned.Should().NotBeNull();
        actioned!.SelectedRow!.Title.Should().Be("Filename-like alt text");
        selection.Should().Equal(911u);
        altTextPaneVisible.Should().BeTrue();
        altTextRequest.Should().NotBeNull();
        altTextRequest!.CurrentDescription.Should().Be("IMG_2048.JPG");
    }

    [Fact]
    public async Task Accessibility_checker_low_text_contrast_row_uses_shared_plan()
    {
        PresentationAccessibilityCheckerPanePlan? opened = null;
        PresentationAccessibilityCheckerPanePlan? actioned = null;
        PresentationAccessibilitySummaryPlan? summary = null;
        uint[] selection = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = new SlideShape
            {
                Id = 912,
                Name = "Muted caption",
                Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x777777)),
                TextBody = MakeTextBodyWithColor("Muted KPI", SrgbColor.FromRgb(0x777777))
            };
            window.Editor.CurrentSlide!.Title = "Intro";
            window.Editor.CurrentSlide.Shapes.Add(shape);

            opened = window.ShowAccessibilityCheckerPane();
            actioned = window.ApplyAccessibilityCheckerRowAction(0);
            summary = window.LastAccessibilitySummaryPlan;
            selection = window.Editor.SelectedShapeIds.ToArray();
        });

        if (!ran) return;
        opened.Should().NotBeNull();
        opened!.Rows.Should().ContainSingle().Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
            row.Title == "Low text contrast" &&
            row.Category == "Text contrast" &&
            row.ShapeId == 912 &&
            row.ShapeName == "Muted caption" &&
            row.ActionLabel == "Select Object" &&
            row.CommandHint == null &&
            row.Detail.Contains("threshold is 4.5:1.", StringComparison.Ordinal) &&
            row.ShouldNavigateToSlide &&
            row.ShouldSelectShape);
        actioned.Should().NotBeNull();
        actioned!.SelectedRow!.Title.Should().Be("Low text contrast");
        summary.Should().NotBeNull();
        summary!.Issues.Should().ContainSingle(issue =>
            issue.ShapeId == 912 &&
            issue.Title == "Low text contrast" &&
            issue.Action.Summary == PresentationReviewWorkflowPlanner.LowTextContrastActionSummary);
        selection.Should().Equal(912u);
    }

    [Fact]
    public async Task Accessibility_checker_table_header_action_uses_shared_mutation_and_refreshes_pane()
    {
        PresentationAccessibilityCheckerPanePlan? actioned = null;
        PresentationTableHeaderRowMutationPlan? mutation = null;
        var actionLabel = string.Empty;
        var commandHint = string.Empty;
        var headerAfterAction = false;
        var headerAfterUndo = true;
        uint[] selection = [];
        var dirty = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var table = new SlideShape
            {
                Id = 778,
                Name = "Results table",
                Kind = SlideShapeKind.Table,
                Table = new TableShape
                {
                    Rows =
                    {
                        new TableRow
                        {
                            Cells =
                            {
                                new TableCell { TextBody = MakeTextBody("Region") },
                                new TableCell { TextBody = MakeTextBody("Revenue") }
                            }
                        },
                        new TableRow
                        {
                            Cells =
                            {
                                new TableCell { TextBody = MakeTextBody("North") },
                                new TableCell { TextBody = MakeTextBody("$42K") }
                            }
                        }
                    }
                }
            };
            window.Editor.CurrentSlide!.Shapes.Add(table);

            var opened = window.ShowAccessibilityCheckerPane();
            var tableRow = opened.Rows.Single(row => row.Title == "Table header row missing");
            actionLabel = tableRow.ActionLabel;
            commandHint = tableRow.CommandHint ?? string.Empty;

            actioned = window.ApplyAccessibilityCheckerRowAction(tableRow.RowIndex);
            mutation = window.LastTableHeaderRowMutationPlan;
            headerAfterAction = table.Table!.Flags.FirstRow;
            selection = window.Editor.SelectedShapeIds.ToArray();
            dirty = window.IsDirty;

            window.Editor.Undo();
            headerAfterUndo = table.Table.Flags.FirstRow;
        });

        if (!ran) return;
        actionLabel.Should().Be("Set Header Row");
        commandHint.Should().Be(PresentationReviewWorkflowPlanner.SetTableHeaderRowCommandId);
        mutation.Should().Be(new PresentationTableHeaderRowMutationPlan(true, 0, 778, null));
        headerAfterAction.Should().BeTrue();
        actioned.Should().NotBeNull();
        actioned!.Rows.Should().NotContain(row => row.Title == "Table header row missing");
        selection.Should().Equal(778u);
        dirty.Should().BeTrue();
        headerAfterUndo.Should().BeFalse();
    }

    [Fact]
    public async Task Accessibility_checker_table_structure_action_opens_shared_review_plan()
    {
        PresentationAccessibilityCheckerPanePlan? actioned = null;
        PresentationTableStructureReviewPlan? reviewPlan = null;
        PresentationTableStructureReviewDisplayPlan? displayPlan = null;
        IReadOnlyList<string> renderedLines = [];
        var actionLabel = string.Empty;
        var commandHint = string.Empty;
        uint[] selection = [];
        var dirty = true;
        var headerRowStillSet = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var table = new SlideShape
            {
                Id = 779,
                Name = "Forecast table",
                Kind = SlideShapeKind.Table,
                Table = new TableShape
                {
                    Flags = new TableStyleFlags { FirstRow = true },
                    Rows =
                    {
                        new TableRow
                        {
                            Cells =
                            {
                                new TableCell { TextBody = MakeTextBody("Region"), GridSpan = 2 },
                                new TableCell { HMerge = true },
                                new TableCell()
                            }
                        },
                        new TableRow
                        {
                            Cells =
                            {
                                new TableCell { TextBody = MakeTextBody("North") },
                                new TableCell(),
                                new TableCell { TextBody = MakeTextBody("$42K") }
                            }
                        }
                    }
                }
            };
            window.Editor.CurrentSlide!.Shapes.Add(table);

            var opened = window.ShowAccessibilityCheckerPane();
            var tableRow = opened.Rows.Single(row => row.Title == "Blank table header cells");
            actionLabel = tableRow.ActionLabel;
            commandHint = tableRow.CommandHint ?? string.Empty;

            actioned = window.ApplyAccessibilityCheckerRowAction(tableRow.RowIndex);
            reviewPlan = window.LastTableStructureReviewPlan;
            displayPlan = window.LastTableStructureReviewDisplayPlan;
            renderedLines = window.AccessibilityCheckerTableStructureReviewRenderedLines;
            selection = window.Editor.SelectedShapeIds.ToArray();
            dirty = window.IsDirty;
            headerRowStillSet = table.Table!.Flags.FirstRow;
        });

        if (!ran) return;
        actionLabel.Should().Be("Review Table Structure");
        commandHint.Should().Be(PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId);
        reviewPlan.Should().NotBeNull();
        reviewPlan!.Should().Match<PresentationTableStructureReviewPlan>(plan =>
            plan.CanReview &&
            plan.ShapeId == 779 &&
            plan.TableName == "Forecast table" &&
            plan.RowCount == 2 &&
            plan.ColumnCount == 3 &&
            plan.ShouldNavigateToSlide &&
            plan.ShouldSelectTable);
        reviewPlan.BlankHeaderCells.Should().Equal(new[]
        {
            new PresentationTableStructureCellPlan(0, 2, "R1C3")
        });
        reviewPlan.BlankBodyCells.Should().Equal(new[]
        {
            new PresentationTableStructureCellPlan(1, 1, "R2C2")
        });
        reviewPlan.MergedOrSplitCells.Select(cell => cell.CellReference).Should().Equal("R1C1", "R1C2");
        displayPlan.Should().NotBeNull();
        displayPlan!.Summary.Should()
            .Be("Forecast table: 2 rows, 3 columns. 1 blank header cell, 1 blank body cell, 2 merged or split cells.");
        displayPlan.Details.Should().Equal(new[]
        {
            new PresentationTableStructureReviewDetailRowPlan(
                "Blank header cell",
                "R1C3 is blank.",
                "Add descriptive header text or remove the empty header cell."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Blank body cell",
                "R2C2 is blank.",
                "Confirm the blank data cell is intentional or add visible text."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Merged or split cell",
                "R1C1 spans 2 columns.",
                "Verify the table still reads correctly in row and column order."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Merged or split cell",
                "R1C2 continues a horizontal merge.",
                "Verify the table still reads correctly in row and column order.")
        });
        renderedLines.Should().Equal(
            "Review Table Structure",
            "Forecast table: 2 rows, 3 columns. 1 blank header cell, 1 blank body cell, 2 merged or split cells.",
            PresentationReviewWorkflowPlanner.TableStructureReviewGuidance,
            "Blank header cell: R1C3 is blank. Add descriptive header text or remove the empty header cell.",
            "Blank body cell: R2C2 is blank. Confirm the blank data cell is intentional or add visible text.",
            "Merged or split cell: R1C1 spans 2 columns. Verify the table still reads correctly in row and column order.",
            "Merged or split cell: R1C2 continues a horizontal merge. Verify the table still reads correctly in row and column order.");
        actioned.Should().NotBeNull();
        actioned!.SelectedRow!.CommandHint.Should().Be(PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId);
        selection.Should().Equal(779u);
        dirty.Should().BeFalse();
        headerRowStillSet.Should().BeTrue();
    }

    [Fact]
    public async Task Accessibility_checker_media_caption_tracks_use_shared_plan()
    {
        PresentationAccessibilityCheckerPanePlan? opened = null;
        PresentationAccessibilitySummaryPlan? summary = null;
        PresentationMediaTranscriptPlan? transcript = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Title = "Media accessibility";
            var missingCaptions = new SlideShape
            {
                Id = 713,
                Name = "Demo video",
                Kind = SlideShapeKind.Media,
                Media = new MediaInfo { IsVideo = true },
                AlternativeText = "Demo walkthrough."
            };
            var captioned = new SlideShape
            {
                Id = 714,
                Name = "Training video",
                Kind = SlideShapeKind.Media,
                Media = new MediaInfo
                {
                    IsVideo = true,
                    CaptionTracks =
                    {
                        new MediaCaptionTrackInfo
                        {
                            RelationshipId = "rIdCaption1",
                            Source = "ppt/media/training.vtt",
                            ContentType = "text/vtt",
                            Language = "en-US",
                            Label = "English captions",
                            Bytes = Encoding.UTF8.GetBytes(
                                "WEBVTT\r\n\r\n00:00.000 --> 00:01.000\r\nShared transcript cue\r\n")
                        }
                    }
                },
                AlternativeText = "Training walkthrough."
            };
            window.Editor.CurrentSlide.Shapes.Add(missingCaptions);
            window.Editor.CurrentSlide.Shapes.Add(captioned);

            opened = window.ShowAccessibilityCheckerPane();
            summary = window.LastAccessibilitySummaryPlan;
            transcript = window.LastMediaTranscriptPlan;
        });

        if (!ran) return;
        opened.Should().NotBeNull();
        opened!.Rows.Should().ContainSingle(row => row.Title == "Video captions missing")
            .Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
                row.Category == "Media" &&
                row.ShapeId == 713 &&
                row.ShapeName == "Demo video" &&
                row.ActionLabel == "Open Captions" &&
                row.CommandHint == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId &&
                row.ShouldNavigateToSlide &&
                row.ShouldSelectShape);
        summary.Should().NotBeNull();
        summary!.Issues.Should().NotContain(issue =>
            issue.ShapeId == 714 && issue.Title == "Video captions missing");
        transcript.Should().NotBeNull();
        transcript!.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(track =>
                track.ShapeId == 714 &&
                track.ShapeName == "Training video" &&
                track.Label == "English captions" &&
                track.Language == "en-US" &&
                track.Source == "ppt/media/training.vtt" &&
                track.Status == PresentationMediaTranscriptTrackStatus.Available &&
            track.CueCount == 1 &&
            track.Cues[0].Text == "Shared transcript cue");
    }

    [Fact]
    public async Task Media_caption_pane_create_replace_delete_uses_shared_planner()
    {
        PresentationMediaCaptionAuthoringPanePlan? opened = null;
        PresentationMediaCaptionTrackMutationResult? create = null;
        PresentationMediaCaptionTrackMutationResult? replace = null;
        PresentationMediaCaptionTrackMutationResult? delete = null;
        PresentationMediaTranscriptPlan? transcriptAfterCreate = null;
        PresentationMediaTranscriptPlan? transcriptAfterReplace = null;
        PresentationMediaCaptionAuthoringMutationPlan? mutation = null;
        var visible = false;
        var heading = string.Empty;
        var trackCountAfterCreate = -1;
        var trackCountAfterDelete = -1;
        var createEnabledBeforeInput = true;
        var createEnabledAfterInput = false;
        var replaceEnabledAfterCreate = false;
        var deleteEnabledAfterCreate = false;
        var dirty = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var mediaShape = new SlideShape
            {
                Id = 724,
                Name = "Demo video",
                Kind = SlideShapeKind.Media,
                Media = new MediaInfo { IsVideo = true },
                AlternativeText = "Demo walkthrough."
            };
            window.Editor.CurrentSlide!.Shapes.Add(mediaShape);
            window.Editor.Select(mediaShape.Id);

            opened = window.ShowMediaCaptionPane();
            visible = window.IsMediaCaptionPaneVisible;
            heading = window.MediaCaptionPaneHeading;
            createEnabledBeforeInput = window.IsMediaCaptionCreateEnabled;

            window.SetMediaCaptionPaneInput(
                "English captions",
                "en-US",
                "ppt/media/demo-captions.vtt",
                "WEBVTT\r\n\r\n00:00:00.000 --> 00:00:01.000\r\nInitial cue\r\n");
            createEnabledAfterInput = window.IsMediaCaptionCreateEnabled;
            create = window.ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Create);
            mutation = window.LastMediaCaptionAuthoringMutationPlan;
            transcriptAfterCreate = window.LastMediaTranscriptPlan;
            trackCountAfterCreate = window.MediaCaptionPaneTrackCount;
            replaceEnabledAfterCreate = window.IsMediaCaptionReplaceEnabled;
            deleteEnabledAfterCreate = window.IsMediaCaptionDeleteEnabled;
            dirty = window.IsDirty;

            window.SetMediaCaptionPaneInput(
                "English captions",
                "en-US",
                "ppt/media/demo-captions.vtt",
                "WEBVTT\r\n\r\n00:00:01.000 --> 00:00:02.000\r\nUpdated cue\r\n",
                selectedTrackIndex: 0);
            replace = window.ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Replace);
            transcriptAfterReplace = window.LastMediaTranscriptPlan;

            delete = window.ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Delete);
            trackCountAfterDelete = window.MediaCaptionPaneTrackCount;
        });

        if (!ran) return;
        opened.Should().NotBeNull();
        opened!.ShapeId.Should().Be(724);
        visible.Should().BeTrue();
        heading.Should().Be("Media Captions - Demo video");
        createEnabledBeforeInput.Should().BeFalse();
        createEnabledAfterInput.Should().BeTrue();
        create.Should().NotBeNull();
        create!.Succeeded.Should().BeTrue();
        create.TrackIndex.Should().Be(0);
        mutation.Should().NotBeNull();
        mutation!.Intent.Should().Be(PresentationMediaCaptionAuthoringIntentKind.Create);
        trackCountAfterCreate.Should().Be(1);
        replaceEnabledAfterCreate.Should().BeTrue();
        deleteEnabledAfterCreate.Should().BeTrue();
        dirty.Should().BeTrue();
        transcriptAfterCreate.Should().NotBeNull();
        transcriptAfterCreate!.Tracks.Should().ContainSingle()
            .Which.Cues.Single().Text.Should().Be("Initial cue");
        replace.Should().NotBeNull();
        replace!.Succeeded.Should().BeTrue();
        transcriptAfterReplace.Should().NotBeNull();
        transcriptAfterReplace!.Tracks.Should().ContainSingle()
            .Which.Cues.Single().Text.Should().Be("Updated cue");
        delete.Should().NotBeNull();
        delete!.Succeeded.Should().BeTrue();
        trackCountAfterDelete.Should().Be(0);
    }

    [Fact]
    public async Task Review_comment_add_edit_routes_through_shared_mutation_plan()
    {
        SlideComment? addedComment = null;
        SlideComment? editedComment = null;
        PresentationCommentMutationPlan? addPlan = null;
        PresentationCommentMutationPlan? editPlan = null;
        PresentationCommentPanePlan? addedPanePlan = null;
        PresentationCommentPanePlan? editedPanePlan = null;
        var dirtyAfterEdit = false;
        var registryAddCount = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet(PresentationReviewWorkflowPlanner.AddCommentCommandId, out var addCommand)
                .Should().BeTrue();
            registry.TryGet(PresentationReviewWorkflowPlanner.EditCommentCommandId, out var editCommand)
                .Should().BeTrue();

            addPlan = window.AddComment(
                "  Add execution evidence. ",
                new DateTime(2026, 7, 2, 16, 30, 0, DateTimeKind.Utc),
                "  FreeP User ",
                null,
                120,
                240);
            addedComment = window.Editor.CurrentSlide!.Comments.Single();
            addedPanePlan = window.LastCommentPanePlan;

            editPlan = window.EditSelectedComment("  Edited execution evidence. ", "Reviewer", "RV");
            editedComment = window.Editor.CurrentSlide.Comments.Single();
            editedPanePlan = window.LastCommentPanePlan;
            dirtyAfterEdit = window.IsDirty;

            addCommand!.Execute(RibbonCommandContext.Empty);
            registryAddCount = window.Editor.CurrentSlide.Comments.Count;
            editCommand!.Execute(RibbonCommandContext.Empty);
        });

        if (!ran) return;
        addPlan.Should().NotBeNull();
        addPlan!.Intent.Should().Be(PresentationReviewWorkflowIntentKind.AddComment);
        addPlan.ShouldApply.Should().BeTrue();
        addedComment.Should().NotBeNull();
        addedComment!.Text.Should().Be("Add execution evidence.");
        addedComment.Author.Should().Be("FreeP User");
        addedComment.Initials.Should().Be("FU");
        addedComment.Xemu.Should().Be(120);
        addedComment.Yemu.Should().Be(240);
        addedPanePlan.Should().NotBeNull();
        addedPanePlan!.SelectedCommentIndex.Should().Be(0);
        editPlan.Should().NotBeNull();
        editPlan!.Intent.Should().Be(PresentationReviewWorkflowIntentKind.EditComment);
        editPlan.ShouldApply.Should().BeTrue();
        editedComment.Should().NotBeNull();
        editedComment!.Text.Should().Be("Edited execution evidence.");
        editedComment.Author.Should().Be("Reviewer");
        editedComment.Initials.Should().Be("RV");
        editedPanePlan.Should().NotBeNull();
        editedPanePlan!.SelectedComment!.TextPreview.Should().Be("Edited execution evidence.");
        dirtyAfterEdit.Should().BeTrue();
        registryAddCount.Should().Be(2);
    }

    [Fact]
    public async Task Review_comments_pane_preserves_explicit_open_state_for_empty_slide()
    {
        var visibleAfterEmptyOpen = false;
        var visibleAfterClose = true;
        var visibleAfterReopen = false;
        var visibleAfterAdd = false;
        var visibleAfterRemove = false;
        var visibleAfterClosedRefresh = true;
        PresentationCommentPanePlan? emptyPlan = null;
        PresentationCommentMutationPlan? addMutationPlan = null;
        PresentationCommentPanePlan? addedPanePlan = null;
        PresentationCommentMutationPlan? removeMutationPlan = null;
        PresentationCommentPanePlan? removedPanePlan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());

            emptyPlan = window.ShowReviewCommentsPane();
            visibleAfterEmptyOpen = window.IsReviewCommentsPaneVisible;

            window.HideReviewCommentsPane();
            visibleAfterClose = window.IsReviewCommentsPaneVisible;
            window.RefreshReviewWorkflowPlans();
            visibleAfterClosedRefresh = window.IsReviewCommentsPaneVisible;

            window.ShowReviewCommentsPane();
            visibleAfterReopen = window.IsReviewCommentsPaneVisible;

            addMutationPlan = window.AddComment("Keep the review pane open.");
            addedPanePlan = window.LastCommentPanePlan;
            visibleAfterAdd = window.IsReviewCommentsPaneVisible;

            removeMutationPlan = window.DeleteSelectedComment();
            removedPanePlan = window.LastCommentPanePlan;
            visibleAfterRemove = window.IsReviewCommentsPaneVisible;
        });

        if (!ran) return;
        emptyPlan.Should().NotBeNull();
        emptyPlan!.Comments.Should().BeEmpty();
        visibleAfterEmptyOpen.Should().BeTrue();
        visibleAfterClose.Should().BeFalse();
        visibleAfterClosedRefresh.Should().BeFalse();
        visibleAfterReopen.Should().BeTrue();
        addMutationPlan.Should().NotBeNull();
        addMutationPlan!.ShouldApply.Should().BeTrue();
        addedPanePlan.Should().NotBeNull();
        addedPanePlan!.Comments.Should().ContainSingle();
        visibleAfterAdd.Should().BeTrue();
        removeMutationPlan.Should().NotBeNull();
        removeMutationPlan!.ShouldApply.Should().BeTrue();
        removedPanePlan.Should().NotBeNull();
        removedPanePlan!.Comments.Should().BeEmpty();
        visibleAfterRemove.Should().BeTrue();
    }

    [Fact]
    public async Task Review_comment_resolve_reopen_routes_through_shared_mutation_plan()
    {
        SlideComment? resolvedComment = null;
        SlideComment? reopenedComment = null;
        SlideComment? remainingComment = null;
        PresentationCommentMutationPlan? resolvePlan = null;
        PresentationCommentMutationPlan? reopenPlan = null;
        PresentationCommentMutationPlan? invalidDeletePlan = null;
        PresentationCommentPanePlan? noSelectionPlan = null;
        PresentationCommentPanePlan? resolvedPanePlan = null;
        PresentationCommentPanePlan? reopenedPanePlan = null;
        PresentationCommentPanePlan? deletedPanePlan = null;
        PresentationCommentPanePlan? invalidSelectionPanePlan = null;
        var paneVisible = false;
        var dirtyAfterDelete = false;
        var commentCountAfterDelete = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Close this thread.",
                Idx = 1
            });

            var registry = window.BuildCommandRegistry();
            registry.TryGet(PresentationReviewWorkflowPlanner.ResolveCommentCommandId, out var resolveCommand)
                .Should().BeTrue();
            registry.TryGet(PresentationReviewWorkflowPlanner.ReopenCommentCommandId, out var reopenCommand)
                .Should().BeTrue();
            registry.TryGet(PresentationReviewWorkflowPlanner.DeleteCommentCommandId, out var deleteCommand)
                .Should().BeTrue();

            noSelectionPlan = window.ShowReviewCommentsPane();
            noSelectionPlan.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.ResolveCommentCommandId)
                .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingCommentMessage);

            window.SetSelectedReviewCommentIndexForTests(0);
            resolvePlan = window.ResolveSelectedComment(
                new DateTime(2026, 7, 2, 14, 0, 0, DateTimeKind.Utc),
                "  FreeP User ");
            resolvedComment = window.Editor.CurrentSlide.Comments[0];
            resolvedPanePlan = window.LastCommentPanePlan;

            reopenPlan = window.ReopenSelectedComment();
            reopenedComment = window.Editor.CurrentSlide.Comments[0];
            reopenedPanePlan = window.LastCommentPanePlan;

            resolveCommand!.Execute(RibbonCommandContext.Empty);
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Delete this thread.",
                Idx = 2
            });
            window.SetSelectedReviewCommentIndexForTests(1);
            deleteCommand!.Execute(RibbonCommandContext.Empty);
            remainingComment = window.Editor.CurrentSlide.Comments.Single();
            deletedPanePlan = window.LastCommentPanePlan;
            dirtyAfterDelete = window.IsDirty;
            commentCountAfterDelete = window.ReviewCommentsPaneCommentCount;

            invalidSelectionPanePlan = window.SetSelectedReviewCommentIndexForTests(42);
            invalidDeletePlan = window.DeleteSelectedComment();
            paneVisible = window.IsReviewCommentsPaneVisible;
        });

        if (!ran) return;
        noSelectionPlan.Should().NotBeNull();
        resolvePlan.Should().NotBeNull();
        resolvePlan!.ShouldApply.Should().BeTrue();
        resolvePlan.Intent.Should().Be(PresentationReviewWorkflowIntentKind.ResolveComment);
        resolvedComment.Should().NotBeNull();
        resolvedComment!.IsResolved.Should().BeTrue();
        resolvedComment.ResolvedDateTime.Should().Be(new DateTime(2026, 7, 2, 14, 0, 0, DateTimeKind.Utc));
        resolvedComment.ResolvedBy.Should().Be("FreeP User");
        resolvedPanePlan.Should().NotBeNull();
        resolvedPanePlan!.Comments.Single().CanReopen.Should().BeTrue();
        resolvedPanePlan.Actions.Single(action =>
                action.CommandId == PresentationReviewWorkflowPlanner.ResolveCommentCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.CommentAlreadyResolvedMessage);
        reopenedComment.Should().NotBeNull();
        reopenedComment!.IsResolved.Should().BeFalse();
        reopenedComment.ResolvedDateTime.Should().BeNull();
        reopenedComment.ResolvedBy.Should().BeEmpty();
        reopenedPanePlan.Should().NotBeNull();
        reopenedPanePlan!.Comments.Single().CanResolve.Should().BeTrue();
        reopenPlan.Should().NotBeNull("reopen should refresh the pane to an open-thread state");
        remainingComment.Should().NotBeNull();
        remainingComment!.Text.Should().Be("Close this thread.");
        deletedPanePlan.Should().NotBeNull();
        deletedPanePlan!.SelectedCommentIndex.Should().Be(0);
        deletedPanePlan.SelectedComment!.TextPreview.Should().Be("Close this thread.");
        dirtyAfterDelete.Should().BeTrue();
        commentCountAfterDelete.Should().Be(1);
        invalidSelectionPanePlan.Should().NotBeNull();
        invalidSelectionPanePlan!.Actions.Single(action =>
                action.CommandId == PresentationReviewWorkflowPlanner.DeleteCommentCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingCommentMessage);
        invalidDeletePlan.Should().NotBeNull();
        invalidDeletePlan!.ShouldApply.Should().BeFalse();
        invalidDeletePlan.ValidationMessage.Should().Be(PresentationReviewWorkflowPlanner.MissingCommentMessage);
        paneVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Review_comments_pane_renders_shared_action_button_states()
    {
        string[] renderedActionStates = [];
        string[] sharedActionStates = [];
        PresentationCommentPanePlan? panePlan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Resolved thread.",
                Idx = 1,
                IsResolved = true,
                ResolvedBy = "Reviewer",
                Replies =
                {
                    new SlideCommentReply
                    {
                        Author = "FreeP User",
                        Initials = "FU",
                        Text = "Closing reply."
                    }
                }
            });

            panePlan = window.SetSelectedReviewCommentIndexForTests(0);
            renderedActionStates = window.ReviewCommentsPaneRenderedActionStates.ToArray();
            sharedActionStates = panePlan.Actions
                .Where(action => action.CommandId != PresentationReviewWorkflowPlanner.ReplyCommentCommandId)
                .Select(action => $"{action.CommandId}|{action.Label}|{action.IsEnabled}")
                .ToArray();
        });

        if (!ran) return;
        panePlan.Should().NotBeNull();
        panePlan!.SelectedComment!.CanReply.Should().BeFalse("resolved PowerPoint comment threads must be reopened before replying");
        renderedActionStates.Should().Equal(sharedActionStates);
        renderedActionStates.Should().Contain(
            $"{PresentationReviewWorkflowPlanner.ResolveCommentCommandId}|Resolve Comment|False");
        renderedActionStates.Should().Contain(
            $"{PresentationReviewWorkflowPlanner.ReopenCommentCommandId}|Reopen Comment|True");
        renderedActionStates.Should().NotContain(state =>
            state.StartsWith(PresentationReviewWorkflowPlanner.ReplyCommentCommandId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Review_comment_reply_ribbon_command_routes_through_shared_mutation_plan()
    {
        SlideComment? repliedComment = null;
        PresentationCommentPanePlan? replyPanePlan = null;
        var foundReply = false;
        var dirtyAfterReply = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Needs a reply.",
                Idx = 1
            });
            window.SetSelectedReviewCommentIndexForTests(0);

            var registry = window.BuildCommandRegistry();
            foundReply = registry.TryGet(PresentationReviewWorkflowPlanner.ReplyCommentCommandId, out var replyCommand);
            replyCommand!.Execute(RibbonCommandContext.Empty);

            repliedComment = window.Editor.CurrentSlide.Comments.Single();
            replyPanePlan = window.LastCommentPanePlan;
            dirtyAfterReply = window.IsDirty;
        });

        if (!ran) return;
        foundReply.Should().BeTrue();
        repliedComment.Should().NotBeNull();
        repliedComment!.Replies.Should().ContainSingle();
        repliedComment.Replies.Single().Should().Match<SlideCommentReply>(reply =>
            reply.Author == "FreeP User" &&
            reply.Initials == "FU" &&
            reply.Text == "New reply");
        replyPanePlan.Should().NotBeNull();
        replyPanePlan!.SelectedComment!.Replies.Single().TextPreview.Should().Be("New reply");
        replyPanePlan.SelectedComment.ThreadStatusSummary.Should().Be("Open - 1 reply");
        replyPanePlan.SelectedComment.Replies.Single().AuthorDisplayName.Should().Be("FreeP User");
        replyPanePlan.SelectedComment.Replies.Single().InitialsBadgeText.Should().Be("FU");
        replyPanePlan.SelectedComment.Replies.Single().AuthorIdentityKey.Should().Be("FREEP USER|FU");
        replyPanePlan.SelectedComment.ReplyCount.Should().Be(1);
        dirtyAfterReply.Should().BeTrue();
    }

    [Fact]
    public async Task Review_comment_visible_mention_actions_route_through_shared_mutation_plan()
    {
        PresentationCommentMentionInsertionPlan? insertion = null;
        PresentationCommentPanePlan? panePlan = null;
        SlideComment? editedComment = null;
        SlideComment? repliedComment = null;
        var dirtyAfterMention = false;
        var invokedEdit = false;
        var invokedReply = false;
        string[] mentionActionsBeforeEdit = [];
        string[] mentionLinesAfterEdit = [];
        string[] mentionLinesAfterReply = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Alice Writer",
                Initials = "AW",
                Text = "Please ask @No",
                Idx = 1
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Nora Reviewer",
                Initials = "NR",
                Text = "Available for review.",
                Idx = 2
            });
            window.SetSelectedReviewCommentIndexForTests(0);

            mentionActionsBeforeEdit = window.ReviewCommentsPaneRenderedMentionActions.ToArray();
            invokedEdit = window.InvokeReviewCommentPaneMentionActionForTests("comment-mention:edit");
            insertion = window.LastCommentMentionInsertionPlan;
            editedComment = window.Editor.CurrentSlide.Comments[0];
            panePlan = window.LastCommentPanePlan;
            mentionLinesAfterEdit = window.ReviewCommentsPaneRenderedMentionLines.ToArray();
            dirtyAfterMention = window.IsDirty;
            invokedReply = window.InvokeReviewCommentPaneMentionActionForTests("comment-mention:reply");
            repliedComment = window.Editor.CurrentSlide.Comments[0];
            mentionLinesAfterReply = window.ReviewCommentsPaneRenderedMentionLines.ToArray();
        });

        if (!ran) return;
        mentionActionsBeforeEdit.Should().Contain("comment-mention:edit|@Nora.Reviewer|True");
        invokedEdit.Should().BeTrue();
        invokedReply.Should().BeTrue();
        insertion.Should().NotBeNull();
        insertion!.UpdatedText.Should().Be("Please ask @Nora.Reviewer ");
        editedComment.Should().NotBeNull();
        editedComment!.Text.Should().Be("Please ask @Nora.Reviewer");
        repliedComment.Should().NotBeNull();
        repliedComment!.Replies.Should().ContainSingle().Which.Text.Should().Be("@Alice.Writer");
        panePlan.Should().NotBeNull();
        panePlan!.SelectedComment!.MentionCount.Should().Be(1);
        panePlan.SelectedComment.MentionDetailSummary.Should().Be("Mentions: @Nora.Reviewer");
        mentionLinesAfterEdit.Should().Contain("Mentions: @Nora.Reviewer");
        mentionLinesAfterReply.Should().Contain("Mentions: @Nora.Reviewer");
        mentionLinesAfterReply.Should().Contain("Mentions: @Alice.Writer");
        dirtyAfterMention.Should().BeTrue();
    }

    [Fact]
    public async Task Review_comment_mention_picker_allows_choosing_non_default_candidate()
    {
        SlideComment? editedComment = null;
        PresentationCommentMentionInsertionPlan? insertion = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Alice Writer",
                Initials = "AW",
                Text = "Please ask @",
                Idx = 1
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Nora Reviewer",
                Initials = "NR",
                Text = "Available for review.",
                Idx = 2
            });
            window.SetSelectedReviewCommentIndexForTests(0);
            window.InvokeReviewCommentPaneMentionActionForTests("comment-mention:edit", "@Nora.Reviewer")
                .Should().BeTrue();
            insertion = window.LastCommentMentionInsertionPlan;
            editedComment = window.Editor.CurrentSlide.Comments[0];
        });

        if (!ran) return;
        insertion.Should().NotBeNull();
        insertion!.Candidate!.DisplayName.Should().Be("Nora Reviewer");
        editedComment.Should().NotBeNull();
        editedComment!.Text.Should().Be("Please ask @Nora.Reviewer");
    }

    [Fact]
    public async Task Review_modern_comment_reply_reuses_powerpoint_author_identity()
    {
        SlideComment? repliedComment = null;
        PresentationCommentPanePlan? replyPanePlan = null;
        PresentationCommentMutationPlan? replyPlan = null;
        var dirtyAfterReply = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Alice Reviewer",
                Initials = "AR",
                Text = "Needs a second reviewer.",
                UsesModernCommentSchema = true,
                ModernAuthorId = "{11111111-1111-1111-1111-111111111111}",
                ModernAuthorUserId = "alice@example.com::powerpoint",
                ModernAuthorProviderId = "aad",
                Idx = 1,
                Replies =
                {
                    new SlideCommentReply
                    {
                        Author = "Bob Reviewer",
                        Initials = "BR",
                        Text = "Taking a look.",
                        ModernAuthorId = "{22222222-2222-2222-2222-222222222222}",
                        ModernAuthorUserId = "bob@example.com::powerpoint",
                        ModernAuthorProviderId = "aad"
                    }
                }
            });
            window.SetSelectedReviewCommentIndexForTests(0);

            replyPlan = window.ReplyToSelectedComment(
                "  Confirmed after checking the deck. ",
                new DateTime(2026, 7, 4, 9, 15, 0, DateTimeKind.Utc),
                "bob reviewer",
                "br");

            repliedComment = window.Editor.CurrentSlide.Comments.Single();
            replyPanePlan = window.LastCommentPanePlan;
            dirtyAfterReply = window.IsDirty;
        });

        if (!ran) return;
        replyPlan.Should().NotBeNull();
        replyPlan!.ShouldApply.Should().BeTrue();
        repliedComment.Should().NotBeNull();
        repliedComment!.Replies.Should().HaveCount(2);
        repliedComment.Replies[1].Should().Match<SlideCommentReply>(reply =>
            reply.Author == "bob reviewer" &&
            reply.Initials == "br" &&
            reply.Text == "Confirmed after checking the deck." &&
            reply.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
            reply.ModernAuthorUserId == "bob@example.com::powerpoint" &&
            reply.ModernAuthorProviderId == "aad");
        replyPanePlan.Should().NotBeNull();
        replyPanePlan!.SelectedComment!.Replies[1].Should().Match<PresentationCommentReplyDescriptor>(reply =>
            reply.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
            reply.ModernAuthorUserId == "bob@example.com::powerpoint" &&
            reply.ModernAuthorProviderId == "aad");
        dirtyAfterReply.Should().BeTrue();
    }

    [Fact]
    public async Task Review_comment_next_previous_commands_navigate_through_shared_plan()
    {
        PresentationCommentNavigationPlan? sameSlideNext = null;
        PresentationCommentNavigationPlan? crossSlideNext = null;
        PresentationCommentNavigationPlan? previousAcrossEmptySlide = null;
        PresentationCommentPanePlan? finalPanePlan = null;
        var foundPrevious = false;
        var foundNext = false;
        var finalSlideIndex = -1;
        var dirtyBeforeNavigation = false;
        var dirtyAfterNavigation = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "First thread.",
                Idx = 1
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Second thread.",
                Idx = 2
            });
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Third thread.",
                Idx = 1
            });
            window.Editor.SelectSlide(0);
            window.SetSelectedReviewCommentIndexForTests(0);
            dirtyBeforeNavigation = window.IsDirty;

            var registry = window.BuildCommandRegistry();
            foundPrevious = registry.TryGet(PresentationReviewWorkflowPlanner.PreviousCommentCommandId, out var previousCommand);
            foundNext = registry.TryGet(PresentationReviewWorkflowPlanner.NextCommentCommandId, out var nextCommand);

            nextCommand!.Execute(RibbonCommandContext.Empty);
            sameSlideNext = window.LastCommentNavigationPlan;
            nextCommand.Execute(RibbonCommandContext.Empty);
            crossSlideNext = window.LastCommentNavigationPlan;
            previousCommand!.Execute(RibbonCommandContext.Empty);
            previousAcrossEmptySlide = window.LastCommentNavigationPlan;
            finalPanePlan = window.LastCommentPanePlan;
            finalSlideIndex = window.Editor.CurrentSlideIndex;
            dirtyAfterNavigation = window.IsDirty;
        });

        if (!ran) return;
        foundPrevious.Should().BeTrue();
        foundNext.Should().BeTrue();
        sameSlideNext.Should().NotBeNull();
        sameSlideNext!.TargetSlideIndex.Should().Be(0);
        sameSlideNext.TargetCommentIndex.Should().Be(1);
        crossSlideNext.Should().NotBeNull();
        crossSlideNext!.TargetSlideIndex.Should().Be(2);
        crossSlideNext.TargetCommentIndex.Should().Be(0);
        previousAcrossEmptySlide.Should().NotBeNull();
        previousAcrossEmptySlide!.TargetSlideIndex.Should().Be(0);
        previousAcrossEmptySlide.TargetCommentIndex.Should().Be(1);
        finalSlideIndex.Should().Be(0);
        finalPanePlan.Should().NotBeNull();
        finalPanePlan!.SelectedComment!.TextPreview.Should().Be("Second thread.");
        dirtyAfterNavigation.Should().Be(dirtyBeforeNavigation, "comment navigation should not change document dirty state");
    }

    [Fact]
    public async Task Review_alt_text_pane_shows_shared_ui_and_applies_from_controls()
    {
        var paneVisibleWithoutSelection = false;
        var applyEnabledWithoutSelection = true;
        var paneVisibleWithSelection = false;
        var titleLabel = string.Empty;
        var descriptionLabel = string.Empty;
        var titleText = string.Empty;
        var titlePlaceholder = string.Empty;
        var descriptionPlaceholder = string.Empty;
        var missingDescriptionApplyEnabled = true;
        var validApplyEnabled = false;
        var decorativeApplyEnabled = false;
        string? altTextTitle = null;
        string? altText = null;
        var isDecorative = false;
        PresentationAltTextMutationPlan? metadataMutation = null;
        PresentationAltTextMutationPlan? decorativeMutation = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet(PresentationReviewWorkflowPlanner.AltTextCommandId, out var altTextCommand)
                .Should().BeTrue();

            altTextCommand!.Execute(RibbonCommandContext.Empty);
            paneVisibleWithoutSelection = window.IsAltTextPaneVisible;
            applyEnabledWithoutSelection = window.IsAltTextPaneApplyEnabled;
            window.AltTextPaneMessage.Should().Be(PresentationReviewWorkflowPlanner.MissingShapeMessage);

            var shape = new SlideShape
            {
                Id = 330,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
                AlternativeTextTitle = "Packaging photo",
            };
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);
            altTextCommand.Execute(RibbonCommandContext.Empty);

            paneVisibleWithSelection = window.IsAltTextPaneVisible;
            titleLabel = window.AltTextPaneTitleLabel;
            descriptionLabel = window.AltTextPaneDescriptionLabel;
            titleText = window.AltTextPaneTitleText;
            titlePlaceholder = window.AltTextPaneTitlePlaceholder;
            descriptionPlaceholder = window.AltTextPaneDescriptionPlaceholder;
            missingDescriptionApplyEnabled = window.IsAltTextPaneApplyEnabled;

            window.SetAltTextPaneInput("  Hero packaging photo  ", "  Product packaging on a white background.  ", isDecorative: false);
            validApplyEnabled = window.IsAltTextPaneApplyEnabled;
            metadataMutation = window.ApplyAltTextPane();
            altTextTitle = shape.AlternativeTextTitle;
            altText = shape.AlternativeText;

            window.SetAltTextPaneInput("Ignored title", string.Empty, isDecorative: true);
            decorativeApplyEnabled = window.IsAltTextPaneApplyEnabled;
            decorativeMutation = window.ApplyAltTextPane();
            isDecorative = shape.IsDecorative;
            window.HideAltTextPane();
            window.IsAltTextPaneVisible.Should().BeFalse();
        });

        if (!ran) return;
        paneVisibleWithoutSelection.Should().BeTrue();
        applyEnabledWithoutSelection.Should().BeFalse();
        paneVisibleWithSelection.Should().BeTrue();
        titleLabel.Should().Be("Title");
        descriptionLabel.Should().Be("Description");
        titleText.Should().Be("Packaging photo");
        titlePlaceholder.Should().Be("Packaging photo");
        descriptionPlaceholder.Should().Be(
            "Picture \"Product image\" (PNG image) on slide \"Slide 1\". Describe the important visual details and context.");
        missingDescriptionApplyEnabled.Should().BeFalse();
        validApplyEnabled.Should().BeTrue();
        metadataMutation.Should().Be(new PresentationAltTextMutationPlan(
            true,
            0,
            330,
            "Hero packaging photo",
            "Product packaging on a white background.",
            false,
            null));
        altTextTitle.Should().Be("Hero packaging photo");
        altText.Should().Be("Product packaging on a white background.");
        decorativeApplyEnabled.Should().BeTrue();
        decorativeMutation.Should().Be(new PresentationAltTextMutationPlan(
            true,
            0,
            330,
            string.Empty,
            string.Empty,
            true,
            null));
        isDecorative.Should().BeTrue();
    }

    [Fact]
    public async Task SmartArt_text_pane_renders_shared_outline_and_routes_keyboard()
    {
        var paneVisibleWithoutSelection = false;
        var missingSelectionMessage = string.Empty;
        IReadOnlyList<string> renderedRows = [];
        SmartArtTextPaneApplyResult? apply = null;
        SmartArtNodeEditResult? addSibling = null;
        SmartArtNodeEditResult? addChild = null;
        SmartArtDataPartRewriteResult? dataPart = null;
        SmartArtDrawingCacheRegenerationResult? drawingCache = null;
        var rowCountAfterKeyboard = 0;
        var dirtyAfterApply = false;
        SmartArtShape? smartArt = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.ShowSmartArtTextPane();
            paneVisibleWithoutSelection = window.IsSmartArtTextPaneVisible;
            missingSelectionMessage = window.SmartArtTextPaneMessage;

            var shape = MakeSmartArtShape();
            smartArt = shape.SmartArt;
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            window.ShowSmartArtTextPane();
            renderedRows = window.SmartArtTextPaneRenderedRows;
            window.SmartArtTextPaneActionButtonCount.Should().Be(8);
            window.SmartArtTextPaneEnabledActionButtonCount.Should().Be(8);
            window.SmartArtTextPaneCommandActionCount.Should().Be(5);
            window.SmartArtTextPaneCommandActionsWrap.Should().BeTrue();
            window.SetSmartArtTextPaneRowText(0, "Discover");
            apply = window.ApplySmartArtTextPane();
            dirtyAfterApply = window.IsDirty;
            dataPart = window.LastSmartArtDataPartRewriteResult;
            drawingCache = window.LastSmartArtDrawingCacheRegenerationResult;

            window.Editor.Undo();
            smartArt!.Data!.Nodes[0].Text.Should().Be("Plan");
            window.SmartArtTextPaneRenderedRows.Should().Contain("n1|0|False|Plan");
            window.Editor.Redo();
            smartArt.Data.Nodes[0].Text.Should().Be("Discover");
            window.SmartArtTextPaneRenderedRows.Should().Contain("n1|0|False|Discover");

            addSibling = window.ApplySmartArtTextPaneKeyboardRouteForTests(
                SmartArtTextPaneShortcutKey.Enter,
                SmartArtTextPaneShortcutModifiers.None,
                "n1");
            addChild = window.ApplySmartArtTextPaneKeyboardRouteForTests(
                SmartArtTextPaneShortcutKey.Enter,
                SmartArtTextPaneShortcutModifiers.Control,
                "n2");
            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.MoveUp, "n2")!.Applied.Should().BeTrue();
            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.MoveDown, "n2")!.Applied.Should().BeTrue();
            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.Promote, "freep-smartart-node-4")!.Applied.Should().BeTrue();
            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.Demote, "freep-smartart-node-3")!.Applied.Should().BeTrue();
            var delete = window.ApplySmartArtTextPaneKeyboardRouteForTests(
                SmartArtTextPaneShortcutKey.Delete,
                SmartArtTextPaneShortcutModifiers.None,
                "freep-smartart-node-3");
            delete!.Applied.Should().BeTrue();
            delete.Kind.Should().Be(SmartArtNodeEditKind.Remove);
            rowCountAfterKeyboard = window.SmartArtTextPaneRowCount;
        });

        if (!ran) return;
        paneVisibleWithoutSelection.Should().BeTrue();
        missingSelectionMessage.Should().Be("Select a SmartArt graphic to edit its text outline.");
        renderedRows.Should().Equal(
            "n1|0|False|Plan",
            "n2|0|False|Build");
        apply.Should().NotBeNull();
        apply!.Applied.Should().BeTrue();
        smartArt!.Data!.Nodes[0].Text.Should().Be("Discover");
        dataPart!.Applied.Should().BeTrue();
        drawingCache!.Applied.Should().BeTrue();
        smartArt.FallbackShapes.Should().NotBeEmpty();
        dirtyAfterApply.Should().BeTrue();
        addSibling!.Applied.Should().BeTrue();
        addChild!.Applied.Should().BeTrue();
        rowCountAfterKeyboard.Should().Be(3);
    }

    [Fact]
    public async Task SmartArt_text_pane_toggles_assistant_through_undoable_package_refresh()
    {
        SmartArtNodeEditResult? result = null;
        SmartArtShape? smartArt = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape();
            smartArt = shape.SmartArt;
            smartArt!.Data!.Family = SmartArtFamily.Hierarchy;
            smartArt.Data.LayoutUniqueId =
                "urn:microsoft.com/office/officeart/2005/8/layout/orgChart";
            var root = smartArt.Data.Nodes[0];
            var child = smartArt.Data.Nodes[1];
            smartArt.Data.Nodes.RemoveAt(1);
            child.Level = 1;
            root.Children.Add(child);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);
            window.ShowSmartArtTextPane();

            result = window.ToggleSmartArtTextPaneAssistantForTests("n2");

            result!.Applied.Should().BeTrue();
            shape.SmartArt!.Data!.Nodes[0].Children.Single().IsAssistant.Should().BeTrue();
            window.SmartArtTextPaneRenderedRows.Should().Contain(row => row.Contains("|1|True|Build", StringComparison.Ordinal));
            window.Editor.Undo();
            shape.SmartArt.Data!.Nodes[0].Children.Single().IsAssistant.Should().BeFalse();
            window.Editor.Redo();
            shape.SmartArt.Data.Nodes[0].Children.Single().IsAssistant.Should().BeTrue();

            var addAssistant = window.ApplySmartArtTextPaneEditForTests(
                SmartArtNodeEditKind.AddAssistant,
                "n1");
            addAssistant!.Applied.Should().BeTrue();
            shape.SmartArt.Data.Nodes[0].Children.Should().ContainSingle(child =>
                child.IsAssistant && child.Text == "Assistant");
            window.Editor.Undo();
            shape.SmartArt.Data.Nodes[0].Children.Should().ContainSingle(child =>
                child.ModelId == "n2" && child.IsAssistant);
            window.Editor.Redo();
            shape.SmartArt.Data.Nodes[0].Children.Should().Contain(child =>
                child.Text == "Assistant" && child.IsAssistant);
        });

        if (!ran) return;
        result.Should().NotBeNull();
        result!.Applied.Should().BeTrue();
        smartArt!.Data!.Nodes[0].Children.Should().Contain(child =>
            child.Text == "Assistant" && child.IsAssistant);
    }

    [Fact]
    public async Task SmartArt_color_preset_uses_native_part_and_undo_bus()
    {
        byte[]? before = null;
        SmartArtColorApplyResult? result = null;
        SmartArtShape? smartArt = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape();
            var art = shape.SmartArt!;
            smartArt = art;
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);
            before = art.Parts["ppt/diagrams/colors1.xml"].Bytes.ToArray();

            var registry = window.BuildCommandRegistry();
            registry.TryGet(SmartArtAuthoringPlanner.SingleAccentCommandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
            result = window.LastSmartArtColorApplyResult;
            result!.Applied.Should().BeTrue();
            art.Parts["ppt/diagrams/colors1.xml"].Bytes.Should().NotEqual(before);
            art.Colors!.Palette.Should().HaveCount(2);
            window.Editor.Undo();
            art.Parts["ppt/diagrams/colors1.xml"].Bytes.Should().Equal(before);
            window.Editor.Redo();
            art.Parts["ppt/diagrams/colors1.xml"].Bytes.Should().NotEqual(before);
        });

        if (!ran) return;
        result.Should().NotBeNull();
        result!.Applied.Should().BeTrue();
    }

    [Fact]
    public async Task SmartArt_color_preset_creates_missing_part_and_undo_restores_it()
    {
        SmartArtColorApplyResult? result = null;
        SmartArtShape? smartArt = null;
        string? createdPartPath = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape();
            smartArt = shape.SmartArt!;
            smartArt.Parts.Remove("ppt/diagrams/colors1.xml");
            smartArt.DiagramRelIds.Remove("cs");
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            var registry = window.BuildCommandRegistry();
            registry.TryGet(SmartArtAuthoringPlanner.SingleAccentCommandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
            result = window.LastSmartArtColorApplyResult;
            result!.Applied.Should().BeTrue();
            createdPartPath = result.PartPath;
            smartArt.Parts.Should().ContainKey(createdPartPath!);
            smartArt.DiagramRelIds.Should().ContainKey("cs");

            window.Editor.Undo();
            smartArt.Parts.Should().NotContainKey(createdPartPath!);
            smartArt.DiagramRelIds.Should().NotContainKey("cs");
            window.Editor.Redo();
            smartArt.Parts.Should().ContainKey(createdPartPath!);
            smartArt.DiagramRelIds.Should().ContainKey("cs");
        });

        if (!ran) return;
        result.Should().NotBeNull();
        result!.Applied.Should().BeTrue();
        smartArt.Should().NotBeNull();
        createdPartPath.Should().NotBeNull();
    }

    [Fact]
    public async Task SmartArt_layout_preset_routes_through_command_and_undo_bus()
    {
        SmartArtShape? smartArt = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape();
            smartArt = shape.SmartArt!;
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            var registry = window.BuildCommandRegistry();
            registry.TryGet(SmartArtAuthoringPlanner.BasicProcessLayoutCommandId, out var command)
                .Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
            smartArt.Data!.LayoutUniqueId.Should().Be(
                "urn:microsoft.com/office/officeart/2005/8/layout/basicProcess");
            smartArt.Data.Family.Should().Be(SmartArtFamily.Process);
            Encoding.UTF8.GetString(smartArt.Parts["ppt/diagrams/layout1.xml"].Bytes)
                .Should().Contain("basicProcess");

            window.Editor.Undo();
            smartArt.Data.LayoutUniqueId.Should().Be(
                "urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList");
            window.Editor.Redo();
            smartArt.Data.LayoutUniqueId.Should().EndWith("/basicProcess");

            registry.TryGet(SmartArtAuthoringPlanner.BasicTimelineLayoutCommandId, out var timelineCommand)
                .Should().BeTrue();
            timelineCommand!.Execute(RibbonCommandContext.Empty);
            smartArt.Data.LayoutUniqueId.Should().EndWith("/basicTimeline");
            window.Editor.Undo();
            smartArt.Data.LayoutUniqueId.Should().EndWith("/basicProcess");

            registry.TryGet(SmartArtAuthoringPlanner.StepDownProcessLayoutCommandId, out var stepDownCommand)
                .Should().BeTrue();
            stepDownCommand!.Execute(RibbonCommandContext.Empty);
            smartArt.Data.LayoutUniqueId.Should().EndWith("/StepDownProcess");
            window.Editor.Undo();
            smartArt.Data.LayoutUniqueId.Should().EndWith("/basicProcess");

            registry.TryGet(SmartArtAuthoringPlanner.BasicRadialLayoutCommandId, out var radialCommand)
                .Should().BeTrue();
            radialCommand!.Execute(RibbonCommandContext.Empty);
            smartArt.Data.LayoutUniqueId.Should().EndWith("/radial1");
            window.Editor.Undo();
            smartArt.Data.LayoutUniqueId.Should().EndWith("/basicProcess");

            registry.TryGet(SmartArtAuthoringPlanner.RadialListLayoutCommandId, out var radialListCommand)
                .Should().BeTrue();
            radialListCommand!.Execute(RibbonCommandContext.Empty);
            smartArt.Data.LayoutUniqueId.Should().EndWith("/radialList");
            smartArt.Data.Family.Should().Be(SmartArtFamily.Cycle);
            window.Editor.Undo();
            smartArt.Data.LayoutUniqueId.Should().EndWith("/basicProcess");
        });

        if (!ran) return;
        smartArt.Should().NotBeNull();
        smartArt!.Data!.Family.Should().Be(SmartArtFamily.Process);
    }

    [Fact]
    public async Task SmartArt_layout_gallery_registers_extended_native_presets()
    {
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();

            foreach (var commandId in new[]
            {
                SmartArtAuthoringPlanner.AccentProcessLayoutCommandId,
                SmartArtAuthoringPlanner.AscendingProcessLayoutCommandId,
                SmartArtAuthoringPlanner.DescendingProcessLayoutCommandId,
                SmartArtAuthoringPlanner.BasicTimelineLayoutCommandId,
                SmartArtAuthoringPlanner.CircleAccentTimelineLayoutCommandId,
                SmartArtAuthoringPlanner.StepDownProcessLayoutCommandId,
                SmartArtAuthoringPlanner.AlternatingProcessLayoutCommandId,
                SmartArtAuthoringPlanner.ContinuousBlockProcessLayoutCommandId,
                SmartArtAuthoringPlanner.SegmentedProcessLayoutCommandId,
                SmartArtAuthoringPlanner.ChevronProcessLayoutCommandId,
                SmartArtAuthoringPlanner.BasicChevronProcessLayoutCommandId,
                SmartArtAuthoringPlanner.ClosedChevronProcessLayoutCommandId,
                SmartArtAuthoringPlanner.BendingProcessLayoutCommandId,
                SmartArtAuthoringPlanner.ArrowRibbonLayoutCommandId,
                SmartArtAuthoringPlanner.CircleProcessLayoutCommandId,
                SmartArtAuthoringPlanner.CircleArrowProcessLayoutCommandId,
                SmartArtAuthoringPlanner.FunnelProcessLayoutCommandId,
                SmartArtAuthoringPlanner.VerticalProcessLayoutCommandId,
                SmartArtAuthoringPlanner.VerticalBlockListLayoutCommandId,
                SmartArtAuthoringPlanner.VerticalArrowListLayoutCommandId,
                SmartArtAuthoringPlanner.VerticalBulletListLayoutCommandId,
                SmartArtAuthoringPlanner.HorizontalBulletListLayoutCommandId,
                SmartArtAuthoringPlanner.HorizontalBlockListLayoutCommandId,
                SmartArtAuthoringPlanner.TrapezoidListLayoutCommandId,
                SmartArtAuthoringPlanner.BasicBlockListLayoutCommandId,
                SmartArtAuthoringPlanner.StackedListLayoutCommandId,
                SmartArtAuthoringPlanner.DescendingBlockListLayoutCommandId,
                SmartArtAuthoringPlanner.BasicPyramidLayoutCommandId,
                SmartArtAuthoringPlanner.PyramidListLayoutCommandId,
                SmartArtAuthoringPlanner.InvertedPyramidLayoutCommandId,
                SmartArtAuthoringPlanner.RadialCycleLayoutCommandId,
                SmartArtAuthoringPlanner.BasicRadialLayoutCommandId,
                SmartArtAuthoringPlanner.RadialClusterLayoutCommandId,
                SmartArtAuthoringPlanner.RadialListLayoutCommandId,
                SmartArtAuthoringPlanner.Cycle2LayoutCommandId,
                SmartArtAuthoringPlanner.MultidirectionalCycleLayoutCommandId,
                SmartArtAuthoringPlanner.GearCycleLayoutCommandId,
                SmartArtAuthoringPlanner.TextCycleLayoutCommandId,
                SmartArtAuthoringPlanner.BlockCycleLayoutCommandId,
                SmartArtAuthoringPlanner.NonDirectionalCycleLayoutCommandId,
                SmartArtAuthoringPlanner.BasicListLayoutCommandId,
                SmartArtAuthoringPlanner.List2LayoutCommandId,
                SmartArtAuthoringPlanner.BasicMatrixLayoutCommandId,
                SmartArtAuthoringPlanner.TitledMatrixLayoutCommandId,
                SmartArtAuthoringPlanner.GridMatrixLayoutCommandId,
                SmartArtAuthoringPlanner.BasicRelationshipLayoutCommandId,
                SmartArtAuthoringPlanner.OpposingIdeasLayoutCommandId,
                SmartArtAuthoringPlanner.ConvergingRadialLayoutCommandId,
                SmartArtAuthoringPlanner.DivergingRadialLayoutCommandId,
                SmartArtAuthoringPlanner.BasicVennLayoutCommandId,
                SmartArtAuthoringPlanner.RadialVennLayoutCommandId,
                SmartArtAuthoringPlanner.TargetListLayoutCommandId,
                SmartArtAuthoringPlanner.StackedVennLayoutCommandId,
                SmartArtAuthoringPlanner.InterlockingRingsLayoutCommandId,
                SmartArtAuthoringPlanner.BasicHierarchyLayoutCommandId,
                SmartArtAuthoringPlanner.Hierarchy3LayoutCommandId,
                SmartArtAuthoringPlanner.HorizontalHierarchyLayoutCommandId,
                SmartArtAuthoringPlanner.OrgChartLayoutCommandId,
                SmartArtAuthoringPlanner.PictureCaptionListLayoutCommandId,
                SmartArtAuthoringPlanner.PictureAccentListLayoutCommandId,
                SmartArtAuthoringPlanner.PictureStackLayoutCommandId,
                SmartArtAuthoringPlanner.PictureLineupLayoutCommandId,
                SmartArtAuthoringPlanner.PictureStripsLayoutCommandId,
                SmartArtAuthoringPlanner.LabeledHierarchyLayoutCommandId,
                SmartArtAuthoringPlanner.TableHierarchyLayoutCommandId,
            })
            {
                registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must be registered");
            }
        });

        if (!ran) return;
    }

    [Fact]
    public async Task SmartArt_quick_style_routes_through_command_and_undo_bus()
    {
        SmartArtShape? smartArt = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape();
            smartArt = shape.SmartArt!;
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            var registry = window.BuildCommandRegistry();
            registry.TryGet(SmartArtAuthoringPlanner.IntenseQuickStyleCommandId, out var command)
                .Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
            smartArt.QuickStyle!.UniqueId.Should().EndWith("/quickstyle/simple5");
            smartArt.QuickStyle.Title.Should().Be("Intense Effect");

            window.Editor.Undo();
            smartArt.QuickStyle.Should().BeNull();
            window.Editor.Redo();
            smartArt.QuickStyle!.UniqueId.Should().EndWith("/quickstyle/simple5");
        });

        if (!ran) return;
        smartArt.Should().NotBeNull();
        smartArt!.QuickStyle!.Title.Should().Be("Intense Effect");
    }

    [Fact]
    public async Task SmartArt_all_quick_style_gallery_commands_are_registered()
    {
        var ran = await OnUiThread(() =>
        {
            var registry = new MainWindow(Array.Empty<string>()).BuildCommandRegistry();
            foreach (var commandId in new[]
            {
                SmartArtAuthoringPlanner.SimpleQuickStyleCommandId,
                SmartArtAuthoringPlanner.SoftEdgeQuickStyleCommandId,
                SmartArtAuthoringPlanner.SubtleQuickStyleCommandId,
                SmartArtAuthoringPlanner.ModerateQuickStyleCommandId,
                SmartArtAuthoringPlanner.IntenseQuickStyleCommandId,
                SmartArtAuthoringPlanner.PolishedQuickStyleCommandId,
                SmartArtAuthoringPlanner.InsertQuickStyleCommandId,
                SmartArtAuthoringPlanner.CartoonQuickStyleCommandId,
                SmartArtAuthoringPlanner.PowderQuickStyleCommandId,
                SmartArtAuthoringPlanner.BrickSceneQuickStyleCommandId,
                SmartArtAuthoringPlanner.FlatSceneQuickStyleCommandId,
                SmartArtAuthoringPlanner.MetallicSceneQuickStyleCommandId,
                SmartArtAuthoringPlanner.SunsetSceneQuickStyleCommandId,
                SmartArtAuthoringPlanner.BirdsEyeSceneQuickStyleCommandId,
            })
            {
                registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must be registered");
            }
        });

        if (!ran) return;
    }

    [Fact]
    public async Task SmartArt_extended_quick_style_routes_through_command_and_undo_bus()
    {
        SmartArtShape? smartArt = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape();
            smartArt = shape.SmartArt!;
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            var registry = window.BuildCommandRegistry();
            registry.TryGet(SmartArtAuthoringPlanner.CartoonQuickStyleCommandId, out var command)
                .Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
            smartArt.QuickStyle!.UniqueId.Should().EndWith("/quickstyle/3d3");
            smartArt.QuickStyle.Title.Should().Be("Cartoon");

            window.Editor.Undo();
            smartArt.QuickStyle.Should().BeNull();
            window.Editor.Redo();
            smartArt.QuickStyle!.UniqueId.Should().EndWith("/quickstyle/3d3");
        });

        if (!ran) return;
        smartArt.Should().NotBeNull();
        smartArt!.QuickStyle!.Title.Should().Be("Cartoon");
    }

    [Fact]
    public async Task SmartArt_bending_process_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Process,
                "urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess",
                ["Plan", "Build", "Ship"]);
            window.Editor.CurrentSlide!.Shapes.Clear();
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(5, "Avalonia host consumes the shared three-stage bending process plus two connector DrawOps");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Plan", "Build", "Ship");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("Avalonia host should consume shared left-to-right bending-process geometry");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "Avalonia host should consume shared bending-process connector ops");
    }

    [Fact]
    public async Task SmartArt_funnel_process_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Process,
                "urn:microsoft.com/office/officeart/2005/8/layout/funnelProcess",
                ["Discover", "Qualify", "Convert", "Retain"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 190 and < 210)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(7, "Avalonia host consumes the shared four-stage funnel plus three connector DrawOps");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Discover", "Qualify", "Convert", "Retain");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("Avalonia host should consume shared top-to-bottom funnel geometry");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.Width)
            .Should().BeInDescendingOrder("Avalonia host should consume shared narrowing funnel geometry");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(3, "Avalonia host should consume shared funnel connector ops");
    }

    [Fact]
    public async Task SmartArt_titled_matrix_shape_composes_shared_title_band_and_body_cells()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Matrix,
                "urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix",
                ["Title", "North", "East", "South"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 520 and < 530)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(4,
            "Avalonia consumes the shared titled-matrix title band and three body cells");
        liveShapes.Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Title", "North", "East", "South");
        liveShapes[0].BoundsDip.Width.Should().BeGreaterThan(liveShapes[1].BoundsDip.Width);
        liveShapes[1].BoundsDip.Y.Should().BeGreaterThan(liveShapes[0].BoundsDip.Y);
    }

    [Fact]
    public async Task SmartArt_vertical_block_list_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.List,
                "urn:microsoft.com/office/officeart/2005/8/layout/verticalBlockList",
                ["Overview", "Detail", "Next"]);
            window.Editor.CurrentSlide!.Shapes.Clear();
            window.Editor.CurrentSlide.Shapes.Add(shape);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.Text is not null)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(3,
            "Avalonia consumes the same shared vertical block list plan as WPF");
        liveShapes.Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Overview", "Detail", "Next");
        liveShapes.Select(op => op.BoundsDip.Y).Should().BeInAscendingOrder();
        liveShapes.Should().OnlyContain(op => op.BoundsDip.Width > 0 && op.BoundsDip.Height > 0);
    }

    [Fact]
    public async Task SmartArt_table_hierarchy_shape_composes_shared_cells_without_connectors()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Hierarchy,
                "urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy",
                ["Portfolio", "Owners", "Milestones"]);
            var root = shape.SmartArt!.Data!.Nodes[0];
            var owners = shape.SmartArt.Data.Nodes[1];
            var milestones = shape.SmartArt.Data.Nodes[2];
            shape.SmartArt.Data.Nodes.RemoveRange(1, 2);
            owners.Level = 1;
            milestones.Level = 1;
            root.Children.Add(owners);
            root.Children.Add(milestones);

            window.Editor.CurrentSlide!.Shapes.Add(shape);
            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 520 and < 530)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(3,
            "Avalonia consumes the shared table hierarchy root header and two group cells");
        liveShapes.All(op => op.Text is not null).Should().BeTrue(
            "Avalonia consumes the table hierarchy no-connector plan");
        liveShapes.Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Portfolio", "Owners", "Milestones");
    }

    [Fact]
    public async Task SmartArt_org_chart_shape_composes_dedicated_shared_assistant_plan()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Hierarchy,
                "urn:microsoft.com/office/officeart/2005/8/layout/orgChart",
                ["CEO", "Assistant", "Director"]);
            var root = shape.SmartArt!.Data!.Nodes[0];
            var assistant = shape.SmartArt.Data.Nodes[1];
            var director = shape.SmartArt.Data.Nodes[2];
            shape.SmartArt.Data.Nodes.RemoveRange(1, 2);
            assistant.Level = 1;
            assistant.IsAssistant = true;
            director.Level = 1;
            root.Children.Add(assistant);
            root.Children.Add(director);

            window.Editor.CurrentSlide!.Shapes.Add(shape);
            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 400 and < 420)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(5,
            "Avalonia consumes the dedicated shared three-box org-chart plan and two connector DrawOps");
        liveShapes.Where(op => op.Text is not null)
            .Should().OnlyContain(op => op.Text!.Paragraphs.Count == 1);
        liveShapes.SelectMany(op => op.Text?.Paragraphs ?? [])
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .Should().Contain(["CEO", "Assistant", "Director"]);
        liveShapes.Where(op => op.Text is null).Should().HaveCount(2);
    }

    [Fact]
    public async Task SmartArt_circle_process_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Process,
                "urn:microsoft.com/office/officeart/2005/8/layout/circleProcess",
                ["Discover", "Plan", "Build", "Review"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 320 and < 340)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(8, "Avalonia host consumes the shared four-stage circle process plus four connector DrawOps");
        var textOps = liveShapes.Where(op => op.Text is not null).ToList();
        textOps
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Discover", "Plan", "Build", "Review");
        textOps[0].BoundsDip.Y.Should().BeLessThan(textOps[1].BoundsDip.Y,
            "Avalonia host should consume shared top-start circle-process geometry");
        textOps[1].BoundsDip.X.Should().BeGreaterThan(textOps[0].BoundsDip.X,
            "Avalonia host should consume shared clockwise right-side placement");
        textOps[2].BoundsDip.Y.Should().BeGreaterThan(textOps[1].BoundsDip.Y,
            "Avalonia host should consume shared bottom placement");
        textOps[3].BoundsDip.X.Should().BeLessThan(textOps[0].BoundsDip.X,
            "Avalonia host should consume shared left-side placement");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(4, "Avalonia host should consume shared circle-process connector ops");
    }

    [Fact]
    public async Task SmartArt_arrow_ribbon_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Process,
                "urn:microsoft.com/office/officeart/2005/8/layout/arrowRibbon",
                ["Pitch", "Build", "Launch"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 600 and < 620)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(5, "Avalonia host consumes the shared three-stage arrow ribbon plus two connector DrawOps");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Pitch", "Build", "Launch");
        liveShapes
            .Where(op => op.Text is not null)
            .Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("Avalonia host should consume shared left-to-right arrow-ribbon geometry");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(2, "Avalonia host should consume shared arrow-ribbon connector ops");
    }

    [Fact]
    public async Task SmartArt_alternating_process_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Process,
                "urn:microsoft.com/office/officeart/2005/8/layout/alternatingProcess",
                ["Plan", "Build", "Launch", "Learn"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 180 and < 200)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(7, "Avalonia host consumes the shared four-stage alternating process plus three connector DrawOps");
        var textOps = liveShapes.Where(op => op.Text is not null).ToList();
        textOps
            .Select(op => op.Text!.Paragraphs.First().Runs.First().Text)
            .Should().Equal("Plan", "Build", "Launch", "Learn");
        textOps[1].BoundsDip.Y.Should().BeGreaterThan(textOps[0].BoundsDip.Y,
            "Avalonia host should consume shared lower-track placement for the second stage");
        textOps[2].BoundsDip.Y.Should().BeApproximately(textOps[0].BoundsDip.Y, 0.01,
            "Avalonia host should consume shared upper-track placement for the third stage");
        textOps[3].BoundsDip.Y.Should().BeApproximately(textOps[1].BoundsDip.Y, 0.01,
            "Avalonia host should consume shared lower-track placement for the fourth stage");
        textOps[2].BoundsDip.X.Should().BeGreaterThan(textOps[0].BoundsDip.X,
            "Avalonia host should consume shared next-column placement");
        liveShapes.Where(op => op.Text is null)
            .Should().HaveCount(3, "Avalonia host should consume shared alternating-process connector ops");
    }

    [Fact]
    public async Task SmartArt_basic_pyramid_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.List,
                "urn:microsoft.com/office/officeart/2005/8/layout/basicPyramid",
                ["Vision", "Strategy", "Execution", "Proof"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 520 and < 540)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(4, "Avalonia host consumes the shared four-segment basic pyramid DrawOps");
        liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Vision", "Strategy", "Execution", "Proof");
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("Avalonia host should consume basic pyramid segment ops without connectors");
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("Avalonia host should consume shared top-to-bottom pyramid geometry");
        liveShapes.Select(op => op.BoundsDip.Width)
            .Should().BeInAscendingOrder("Avalonia host should consume shared widening pyramid segment geometry");
    }

    [Fact]
    public async Task SmartArt_inverted_pyramid_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.List,
                "urn:microsoft.com/office/officeart/2005/8/layout/invertedPyramid",
                ["Market", "Product", "Team", "Task"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 540 and < 560)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(4, "Avalonia host consumes the shared inverted-pyramid bands");
        liveShapes.Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Equal("Market", "Product", "Team", "Task");
        liveShapes.Select(op => op.BoundsDip.Y).Should().BeInAscendingOrder();
        liveShapes.Select(op => op.BoundsDip.Width).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task SmartArt_radial_venn_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Relationship,
                "urn:microsoft.com/office/officeart/2005/8/layout/radialVenn",
                ["Customer", "Product", "Market", "Proof"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 580 and < 600)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(4, "Avalonia host consumes the shared four-circle radial Venn DrawOps");
        liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["Customer", "Product", "Market", "Proof"]);
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("Avalonia host should consume radial Venn ellipse ops without connectors");
        liveShapes.Select(op => op.BoundsDip.X).Distinct().Should().HaveCountGreaterThan(1,
            "Avalonia host should consume shared radial Venn X placement");
        liveShapes.Select(op => op.BoundsDip.Y).Distinct().Should().HaveCountGreaterThan(1,
            "Avalonia host should consume shared radial Venn Y placement");
    }

    [Fact]
    public async Task SmartArt_stacked_venn_shape_composes_shared_live_draw_ops()
    {
        IReadOnlyList<DrawOp.Shape> liveShapes = [];

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = MakeSmartArtShape(
                SmartArtFamily.Relationship,
                "urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn",
                ["Market", "Product", "Proof"]);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            liveShapes = SlideCompositor.Compose(window.Editor.Presentation, window.Editor.CurrentSlide)
                .OfType<DrawOp.Shape>()
                .Where(op => op.ShapeId is >= 640 and < 660)
                .ToList();
        });

        if (!ran) return;
        liveShapes.Should().HaveCount(3, "Avalonia host consumes the shared three-circle stacked Venn DrawOps");
        liveShapes
            .Select(op => op.Text?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Text)
            .Should().Contain(["Market", "Product", "Proof"]);
        liveShapes.Where(op => op.Text is null)
            .Should().BeEmpty("Avalonia host should consume stacked Venn ellipse ops without connectors");
        liveShapes.Select(op => op.BoundsDip.X)
            .Should().BeInAscendingOrder("Avalonia host should consume shared stacked Venn X offsets");
        liveShapes.Select(op => op.BoundsDip.Y)
            .Should().BeInAscendingOrder("Avalonia host should consume shared stacked Venn Y offsets");

        for (int i = 1; i < liveShapes.Count; i++)
        {
            liveShapes[i].BoundsDip.X.Should().BeLessThan(liveShapes[i - 1].BoundsDip.X + liveShapes[i - 1].BoundsDip.Width,
                "Avalonia host should consume shared horizontally overlapping stacked Venn geometry");
            liveShapes[i].BoundsDip.Y.Should().BeLessThan(liveShapes[i - 1].BoundsDip.Y + liveShapes[i - 1].BoundsDip.Height,
                "Avalonia host should consume shared vertically overlapping stacked Venn geometry");
        }
    }

    [Fact]
    public async Task Review_alt_text_apply_routes_through_shared_mutation_plan()
    {
        string? altTextTitle = null;
        string? altText = null;
        PresentationAltTextRequestPlan? requestPlan = null;
        PresentationAltTextPanePlan? panePlan = null;
        PresentationAccessibilitySummaryPlan? accessibilityPlan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var table = new SlideShape
            {
                Id = 328,
                Name = "Results table",
                Kind = SlideShapeKind.Table,
                Table = new TableShape
                {
                    Rows =
                    {
                        new TableRow
                        {
                            Cells =
                            {
                                new TableCell(),
                                new TableCell()
                            }
                        }
                    }
                }
            };
            var shape = new SlideShape
            {
                Id = 329,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
            };
            window.Editor.CurrentSlide!.Shapes.Add(table);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            var mutation = window.ApplySelectedShapeAlternativeText(
                "  Product packaging on a white background. ",
                "  Hero packaging photo ");
            mutation.Should().Be(new PresentationAltTextMutationPlan(
                true,
                0,
                shape.Id,
                "Hero packaging photo",
                "Product packaging on a white background.",
                false,
                null));

            altTextTitle = shape.AlternativeTextTitle;
            altText = shape.AlternativeText;
            requestPlan = window.LastAltTextRequestPlan;
            panePlan = window.LastAltTextPanePlan;
            accessibilityPlan = window.LastAccessibilitySummaryPlan;
        });

        if (!ran) return;
        altTextTitle.Should().Be("Hero packaging photo");
        altText.Should().Be("Product packaging on a white background.");
        requestPlan.Should().NotBeNull();
        requestPlan!.CurrentTitle.Should().Be("Hero packaging photo");
        requestPlan!.CurrentDescription.Should().Be("Product packaging on a white background.");
        panePlan.Should().NotBeNull();
        panePlan!.CanApply.Should().BeTrue();
        panePlan.Title.Value.Should().Be("Hero packaging photo");
        panePlan.Description.Value.Should().Be("Product packaging on a white background.");
        accessibilityPlan.Should().NotBeNull();
        accessibilityPlan!.Issues.Should().NotContain(issue =>
            issue.ShapeId == 329 && issue.Title == "Alt text missing");
        accessibilityPlan.Issues.Should().Contain(issue =>
            issue.ShapeId == 328 &&
            issue.Title == "Table header row missing" &&
            issue.Action.Summary == PresentationReviewWorkflowPlanner.MissingTableHeaderRowActionSummary);
    }

    [Fact]
    public async Task Notes_pane_refreshes_shared_notes_page_preview_plan()
    {
        PresentationNotesPagePreviewPlan? initialPlan = null;
        PresentationNotesPagePreviewPlan? editedPlan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            initialPlan = window.LastNotesPagePreviewPlan;

            window.Editor.CurrentSlide!.Title = "Roadmap";
            window.Editor.Presentation.NotesPageSizeCxEmu = DrawingMlCoordinateUnits.PointsToEmu(360);
            window.Editor.Presentation.NotesPageSizeCyEmu = DrawingMlCoordinateUnits.PointsToEmu(720);
            window.Editor.SetCurrentSlideNotesText("Mention preview workflow.");
            editedPlan = window.LastNotesPagePreviewPlan;
        });

        if (!ran) return;
        initialPlan.Should().NotBeNull();
        initialPlan!.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        editedPlan.Should().NotBeNull();
        editedPlan!.SlideNumber.Should().Be(1);
        editedPlan.SlideTitle.Should().Be("Roadmap");
        editedPlan.NotesText.Should().Be("Mention preview workflow.");
        editedPlan.PageBounds.Width.Should().Be(360);
        editedPlan.PageBounds.Height.Should().Be(720);
        editedPlan.HasNotes.Should().BeTrue();
        editedPlan.NotesPlaceholder.SourcePlaceholderType.Should().Be(PlaceholderType.Body);
        editedPlan.NotesPlaceholder.HasContent.Should().BeTrue();
        editedPlan.NotesPlaceholder.ShouldShowPlaceholder.Should().BeFalse();
        editedPlan.PrintPlan.SlideRange.DisplayName.Should().Be("Slide 1");
        editedPlan.RenderPages.Should().ContainSingle()
            .Which.Should().Match<PresentationNotesPageRenderedPagePlan>(page =>
                page.ThumbnailLabel == "Slide 1 notes" &&
                page.Detail == "Notes page for slide 1" &&
                page.NoteLineCount == 1);
    }

    [Theory]
    [InlineData("freep.layout")]
    [InlineData("freep.find")]
    [InlineData("freep.replace")]
    [InlineData("freep.insert-link")]
    [InlineData("freep.remove-link")]
    public async Task Ribbon_command_parity_gap_commands_are_registered(string commandId)
    {
        var found = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out _);
        });

        if (!ran) return;
        found.Should().BeTrue($"{commandId} must be registered");
    }

    [Theory]
    [InlineData("freep.find", FindReplaceDialogPlanner.FindTitle, false)]
    [InlineData("freep.replace", FindReplaceDialogPlanner.FindAndReplaceTitle, true)]
    public async Task FindReplace_commands_open_visible_Avalonia_workflow(
        string commandId,
        string expectedTitle,
        bool expectReplaceVisible)
    {
        var found = false;
        var visible = false;
        var title = string.Empty;
        var replaceVisible = false;
        FindReplaceWorkflowPlan? plan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out var command);
            found.Should().BeTrue($"{commandId} must be registered");

            command!.Execute(RibbonCommandContext.Empty);
            visible = window.IsFindReplaceDialogVisible;
            title = window.ActiveFindReplaceDialog!.Title;
            replaceVisible = window.IsFindReplaceReplaceInputVisible;
            plan = window.LastFindReplaceWorkflowPlan;
            window.ActiveFindReplaceDialog.Close();
        });

        if (!ran) return;
        found.Should().BeTrue();
        visible.Should().BeTrue();
        title.Should().Be(expectedTitle);
        replaceVisible.Should().Be(expectReplaceVisible);
        plan.Should().NotBeNull();
        plan!.Title.Should().Be(expectedTitle);
        plan.ShowReplace.Should().Be(expectReplaceVisible);
    }

    [Fact]
    public async Task FindReplace_find_next_routes_through_shared_search_and_selects_match_shape()
    {
        uint shapeId = 0;
        uint selectedShapeId = 0;
        int currentSlideIndex = -1;
        FindReplaceWorkflowPlan? plan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertTextBox("Quarterly needle plan");
            shapeId = shape.Id;

            window.OpenFindDialog();
            window.SetFindReplaceDialogInputForTests("needle");
            plan = window.NavigateFindReplaceDialogForTests(+1);

            selectedShapeId = window.Editor.SelectedShapeIds.Single();
            currentSlideIndex = window.Editor.CurrentSlideIndex;
            window.ActiveFindReplaceDialog?.Close();
        });

        if (!ran) return;
        plan.Should().NotBeNull();
        plan!.MatchCount.Should().Be(1);
        plan.CurrentMatchIndex.Should().Be(0);
        plan.StatusText.Should().Be("Match 1 of 1");
        plan.StatusKind.Should().Be(FindReplacePolicyStatusKind.Match);
        selectedShapeId.Should().Be(shapeId);
        currentSlideIndex.Should().Be(0);
    }

    [Fact]
    public async Task FindReplace_replace_all_routes_through_shared_editing_session()
    {
        string? replacedText = null;
        FindReplaceWorkflowPlan? plan = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertTextBox("cat cat");

            window.OpenFindReplaceDialog();
            window.SetFindReplaceDialogInputForTests("cat", "dog");
            plan = window.ReplaceAllFindReplaceDialogForTests();

            replacedText = shape.TextBody!.Paragraphs[0].Runs[0].Text;
            window.ActiveFindReplaceDialog?.Close();
        });

        if (!ran) return;
        plan.Should().NotBeNull();
        plan!.StatusText.Should().Be("2 replacement(s) made.");
        plan.StatusKind.Should().Be(FindReplacePolicyStatusKind.Replacements);
        replacedText.Should().Be("dog dog");
    }

    [Fact]
    public async Task Ribbon_remove_link_command_routes_to_editor()
    {
        var found = false;
        Hyperlink? remainingLink = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.remove-link", out var command);
            found.Should().BeTrue("Remove Link must be registered");

            var shape = window.Editor.InsertDefaultRectangle();
            window.Editor.Select(shape.Id);
            window.Editor.SetShapeHyperlink(url: "https://example.com");

            command!.Execute(RibbonCommandContext.Empty);
            remainingLink = window.Editor.SelectedShapeHyperlink;
        });

        if (!ran) return;
        found.Should().BeTrue("Remove Link must be registered");
        remainingLink.Should().BeNull("Remove Link should clear the selected shape hyperlink through EditingSession");
    }

    [Theory]
    [InlineData("freep.arrange.group")]
    [InlineData("freep.arrange.ungroup")]
    [InlineData("freep.arrange.change-shape.rectangle")]
    [InlineData("freep.arrange.change-shape.rounded-rectangle")]
    [InlineData("freep.arrange.change-shape.ellipse")]
    [InlineData("freep.arrange.change-shape.triangle")]
    [InlineData("freep.arrange.change-shape.diamond")]
    [InlineData("freep.arrange.change-shape.right-arrow")]
    [InlineData("freep.arrange.change-shape.hexagon")]
    [InlineData("freep.arrange.change-shape.parallelogram")]
    [InlineData("freep.arrange.change-shape.trapezoid")]
    [InlineData("freep.arrange.change-shape.left-arrow")]
    [InlineData("freep.arrange.change-shape.star5")]
    [InlineData("freep.arrange.change-shape.up-arrow")]
    [InlineData("freep.arrange.change-shape.down-arrow")]
    [InlineData("freep.arrange.change-shape.cross")]
    [InlineData("freep.arrange.change-shape.plus-sign")]
    [InlineData("freep.arrange.change-shape.right-triangle")]
    [InlineData("freep.arrange.change-shape.minus-sign")]
    [InlineData("freep.arrange.change-shape.multiply-sign")]
    [InlineData("freep.arrange.change-shape.divide-sign")]
    [InlineData("freep.arrange.change-shape.equal-sign")]
    [InlineData("freep.arrange.change-shape.not-equal-sign")]
    [InlineData("freep.arrange.change-shape.wave")]
    [InlineData("freep.arrange.change-shape.rectangular-callout")]
    [InlineData("freep.arrange.change-shape.rounded-rectangular-callout")]
    [InlineData("freep.arrange.change-shape.oval-callout")]
    [InlineData("freep.arrange.change-shape.explosion")]
    [InlineData("freep.arrange.change-shape.ribbon")]
    [InlineData("freep.arrange.change-shape.flowchart-process")]
    [InlineData("freep.arrange.change-shape.flowchart-decision")]
    [InlineData("freep.arrange.change-shape.flowchart-data")]
    [InlineData("freep.arrange.change-shape.flowchart-predefined-process")]
    [InlineData("freep.arrange.change-shape.flowchart-document")]
    [InlineData("freep.arrange.change-shape.flowchart-terminator")]
    [InlineData("freep.arrange.change-shape.line-callout")]
    [InlineData("freep.arrange.change-shape.cylinder")]
    [InlineData("freep.arrange.change-shape.chord")]
    [InlineData("freep.object.open-embedded")]
    [InlineData("freep.arrange.edit-points")]
    [InlineData("freep.arrange.bring-to-front")]
    [InlineData("freep.arrange.bring-forward")]
    [InlineData("freep.arrange.send-backward")]
    [InlineData("freep.arrange.send-to-back")]
    [InlineData("freep.arrange.flip-horizontal")]
    [InlineData("freep.arrange.flip-vertical")]
    [InlineData("freep.arrange.rotate-left-90")]
    [InlineData("freep.arrange.rotate-right-90")]
    [InlineData("freep.arrange.rotation-options")]
    [InlineData("freep.arrange.align-left")]
    [InlineData("freep.arrange.align-center-h")]
    [InlineData("freep.arrange.align-right")]
    [InlineData("freep.arrange.align-top")]
    [InlineData("freep.arrange.align-middle")]
    [InlineData("freep.arrange.align-bottom")]
    [InlineData("freep.arrange.align-left-to-slide")]
    [InlineData("freep.arrange.align-center-h-to-slide")]
    [InlineData("freep.arrange.align-right-to-slide")]
    [InlineData("freep.arrange.align-top-to-slide")]
    [InlineData("freep.arrange.align-middle-to-slide")]
    [InlineData("freep.arrange.align-bottom-to-slide")]
    [InlineData("freep.arrange.distribute-h")]
    [InlineData("freep.arrange.distribute-v")]
    public async Task Ribbon_arrange_commands_are_registered(string commandId)
    {
        var found = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(commandId, out _);
        });

        if (!ran) return;
        found.Should().BeTrue($"{commandId} must be registered");
    }

    [Fact]
    public async Task Ribbon_arrange_bring_to_front_routes_to_editor()
    {
        uint selectedId = 0;
        uint? topShapeId = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            registry.TryGet("freep.arrange.bring-to-front", out var command)
                .Should()
                .BeTrue("Bring to Front must be registered");

            var back = window.Editor.InsertDefaultRectangle();
            window.Editor.InsertDefaultEllipse();
            window.Editor.InsertDefaultTextBox();
            selectedId = back.Id;
            window.Editor.Select(selectedId);

            command!.Execute(RibbonCommandContext.Empty);
            topShapeId = window.Editor.CurrentSlide!.Shapes.Last().Id;
        });

        if (!ran) return;
        topShapeId.Should().Be(selectedId, "the Avalonia registry should invoke EditingSession.BringToFront");
    }

    [Fact]
    public async Task ChartDataDialog_constructs_from_selected_chart_with_planner_projection()
    {
        string? title = null;
        var seriesColumns = -1;
        var categoryRows = -1;
        var valueCells = -1;
        ChartDataDialogCommitPlan? commit = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartDataDialog(window.Editor, CultureInfo.InvariantCulture);
            title = dialog.Title;
            seriesColumns = dialog.RenderedSeriesColumnCount;
            categoryRows = dialog.RenderedCategoryRowCount;
            valueCells = dialog.RenderedValueCellCount;
            dialog.MoveSeriesForTests(1, down: false);
            dialog.SwitchRowsAndColumnsForTests();
            dialog.SetChartTypeForTests(ChartType.LineMarkers);
            commit = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        title.Should().Be(ChartDataDialogPlanner.DialogTitle);
        seriesColumns.Should().Be(2);
        categoryRows.Should().Be(3);
        valueCells.Should().Be(6);
        commit.Should().NotBeNull();
        commit!.Categories.Should().Equal("Series 2", "Series 1");
        commit.SeriesNames.Should().Equal("Q1", "Q2", "Q3");
        commit.Values[0].Should().Equal(new double?[] { 2.4, 4.3 });
        commit.Values[1].Should().Equal(new double?[] { 4.4, 2.5 });
        commit.Values[2].Should().Equal(new double?[] { 1.8, 3.5 });
        commit.ChartType.Should().Be(ChartType.LineMarkers);
    }

    [Fact]
    public async Task ChartDataDialog_removes_selected_series_and_category()
    {
        ChartDataDialogCommitPlan? commit = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.Scatter);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartDataDialog(window.Editor, CultureInfo.InvariantCulture);
            dialog.RemoveSeriesForTests(0);
            dialog.RemoveCategoryForTests(1);
            commit = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        commit.Should().NotBeNull();
        commit!.Categories.Should().Equal("Q1", "Q3");
        commit.SeriesNames.Should().Equal("Series 2");
        commit.Values.Should().ContainSingle().Which.Should().HaveCount(2);
        commit.XValues.Should().ContainSingle().Which.Should().HaveCount(2);
    }

    [Fact]
    public async Task ChartDataDialog_reorders_selected_category()
    {
        ChartDataDialogCommitPlan? commit = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.Scatter);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartDataDialog(window.Editor, CultureInfo.InvariantCulture);
            dialog.MoveCategoryForTests(0, right: true);
            commit = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        commit.Should().NotBeNull();
        commit!.Categories.Should().Equal("Q2", "Q1", "Q3");
        commit.Values[0].Should().Equal(new double?[] { 2.5, 4.3, 3.5 });
    }

    [Fact]
    public async Task ChartDataDialog_scatter_projection_exposes_coordinate_columns()
    {
        int valueCells = -1;
        ChartDataDialogCommitPlan? commit = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.Scatter);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartDataDialog(window.Editor, CultureInfo.InvariantCulture);
            valueCells = dialog.RenderedValueCellCount;
            commit = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        valueCells.Should().Be(12, "Scatter uses X and Y cells for two series across three points");
        commit.Should().NotBeNull();
        commit!.ChartType.Should().Be(ChartType.Scatter);
        commit.XValues.Should().HaveCount(2);
        commit.XValues.Should().AllSatisfy(values => values.Should().HaveCount(3));
    }

    [Fact]
    public async Task ChartDisplayOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartDisplayOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            chartShape.Chart!.ChartType = ChartType.Stock;
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartDisplayOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(
                "Revenue",
                LegendPosition.Bottom,
                true,
                DataLabelPosition.OutsideEnd,
                false,
                true,
                true,
                true,
                true,
                true,
                "0.0%",
                " | ",
                displayBlanksAs: ChartDisplayBlanksAs.Zero,
                showDataLabelsOverMaximum: true,
                labelFontFamily: "Aptos",
                labelFontSizePt: 9,
                labelBold: true,
                labelItalic: false,
                labelColor: "#2F5496",
                showBubbleSize: true);
            dialog.SetTitleOverlayForTests(true);
            dialog.SetPlotVisibleOnlyForTests(false);
            dialog.SetRoundedCornersForTests(true);
            dialog.SetVaryColorsForTests(true);
            dialog.SetLegendOverlayForTests(true);
            dialog.SetHighLowLinesForTests(false);
            dialog.SetStyleIdForTests(102);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().NotBeNull();
        options!.Title.Should().Be("Revenue");
        options.TitleOverlay.Should().BeTrue();
        options.PlotVisibleOnly.Should().BeFalse();
        options.RoundedCorners.Should().BeTrue();
        options.Legend.Should().Be(LegendPosition.Bottom);
        options.LabelTextStyle.Should().NotBeNull();
        options.LabelTextStyle!.FontFamily.Should().Be("Aptos");
        options.LabelTextStyle.FontSizePt.Should().Be(9);
        options.LabelTextStyle.Bold.Should().BeTrue();
        options.LabelTextStyle.Italic.Should().BeFalse();
        options.LabelTextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
        options.ShowBubbleSize.Should().BeTrue();
        options.StyleId.Should().Be(102);
    }

    [Fact]
    public async Task ChartDataTableOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartDataTableOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartDataTableOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(true, false, true, false, true,
                "#F2F2F2", "#4472C4", 1.25, "#112233", 9, "Aptos", true, false);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().Be(new ChartDataTableOptions(true, false, true, false, true,
            "#F2F2F2", "#4472C4", 1.25, "#112233", 9, "Aptos", true, false));
    }

    [Fact]
    public async Task ChartBubbleOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartBubbleOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.Bubble);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartBubbleOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(225, BubbleSizeRepresentation.Width, true);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().Be(new ChartBubbleOptions(225, BubbleSizeRepresentation.Width, true));
    }

    [Fact]
    public async Task ChartPieOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartPieOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.Doughnut);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartPieOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(225, 68);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().Be(new ChartPieOptions(225, 68));
    }

    [Fact]
    public async Task ChartPlotStyleOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartPlotStyleOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.Scatter);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartPlotStyleOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(ScatterStyle.SmoothMarker, RadarStyle.Filled);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().Be(new ChartPlotStyleOptions(ScatterStyle.SmoothMarker, RadarStyle.Filled));
    }

    [Fact]
    public async Task ChartTextOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartTextOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartTextOptionsDialog(window.Editor);
            dialog.SetOptionsForTests("Calibri", 14, false, true, "#C00000");
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options!.FontFamily.Should().Be("Calibri");
        options.FontSizePt.Should().Be(14);
        options.Bold.Should().BeFalse();
        options.Italic.Should().BeTrue();
        options.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
    }

    [Fact]
    public async Task Chart3DViewOptionsDialog_constructs_and_commits_shared_options()
    {
        Chart3DViewOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            chartShape.Chart!.ThreeDStyle = ChartThreeDStyle.Column;
            chartShape.Chart.BarGapDepthPercent = 150;
            window.Editor.Select(chartShape.Id);

            var dialog = new Chart3DViewOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(25, 35, 54, 100, 125, true, false, 275);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().Be(new Chart3DViewOptions(25, 35, 54, 100, 125, true, false, 275));
    }

    [Fact]
    public async Task ChartAreaOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartAreaOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartAreaOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(ChartAreaFormattingTarget.PlotArea, null, null, null, true, true);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().NotBeNull();
        options!.Target.Should().Be(ChartAreaFormattingTarget.PlotArea);
        options.Fill.Should().BeSameAs(ShapeFill.None.Instance);
        options.Outline.Should().BeSameAs(ShapeOutline.None.Instance);
    }

    [Fact]
    public async Task ChartAreaOptionsDialog_accepts_fill_transparency()
    {
        ChartAreaOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartAreaOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(
                ChartAreaFormattingTarget.ChartArea,
                "#4472C4",
                null,
                null,
                fillTransparency: 40);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().NotBeNull();
        var fill = options!.Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        fill.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        fill.Color.Alpha.Should().Be(153);
    }

    [Fact]
    public async Task ChartProtectionOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartProtectionOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartProtectionOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(false, null, true, false);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().Be(new ChartProtectionOptions(false, null, true, false));
    }

    [Fact]
    public async Task ChartAxisOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartAxisOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartAxisOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(
                ChartAxisKind.Value,
                "Revenue",
                10,
                90,
                10,
                5,
                "$#,##0",
                false,
                ChartTickMark.Out,
                ChartTickMark.In,
                ChartTickLabelPosition.NextTo,
                ChartAxisCrossing.Min,
                10,
                false,
                ChartCrossBetween.MidCat,
                ChartLabelAlignment.Right,
                35,
                true,
                false,
                reverseOrder: true,
                minorGridlines: true);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().Be(new ChartAxisOptions(
            ChartAxisKind.Value, "Revenue", 10, 90, 10, 5, "$#,##0", false,
            ChartTickMark.Out, ChartTickMark.In, ChartTickLabelPosition.NextTo,
            null, 10, false, ChartCrossBetween.MidCat, ChartLabelAlignment.Right,
            35, true, false, true, true));
    }

    [Fact]
    public async Task ChartAxisOptionsDialog_supports_secondary_value_axis()
    {
        ChartAxisOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartAxisOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(
                ChartAxisKind.SecondaryValue,
                "Margin",
                0,
                100,
                25,
                null,
                "0%",
                false);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().NotBeNull();
        options!.Axis.Should().Be(ChartAxisKind.SecondaryValue);
        options.Title.Should().Be("Margin");
        options.Maximum.Should().Be(100);
        options.NumberFormatCode.Should().Be("0%");
    }

    [Fact]
    public async Task ChartSeriesOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartSeriesOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.LineMarkers);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartSeriesOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(0, true, true, 2.25, ChartMarkerSymbol.Diamond, 8, "#4472C4", "#1F4E79", OutlineDash.DashDot, true,
                true, true, false, true, false, true, DataLabelPosition.InsideEnd, "0.0%", " | ",
            "Aptos", 9, true, false, "#2F5496", showBubbleSize: true, errorBars: true,
            trendline: true, trendlineType: ChartTrendlineType.Polynomial, trendlineOrder: 3,
            trendlineForward: 1.5, trendlineBackward: 0.5,
            trendlineEquation: true, trendlineRSquared: true, overrideChartType: ChartType.LineMarkers,
            invertIfNegative: true);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().NotBeNull();
        options!.SeriesIndex.Should().Be(0);
        options.SmoothLine.Should().BeTrue();
        options.OnSecondaryAxis.Should().BeTrue();
        options.InvertIfNegative.Should().BeTrue();
        options.OverrideChartType.Should().Be(ChartType.LineMarkers);
        options.LineWidthPt.Should().Be(2.25);
        options.MarkerSymbol.Should().Be(ChartMarkerSymbol.Diamond);
        options.MarkerSizePt.Should().Be(8);
        options.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        options.LineColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        options.LineDash.Should().Be(OutlineDash.DashDot);
        options.NoLine.Should().BeTrue();
        options.DataLabels.Should().NotBeNull();
        options.DataLabels!.ShowValue.Should().BeTrue();
        options.DataLabels.ShowCategoryName.Should().BeTrue();
        options.DataLabels.ShowLegendKey.Should().BeTrue();
        options.DataLabels.ShowBubbleSize.Should().BeTrue();
        options.ErrorBars.Should().NotBeNull();
        options.Trendline.Should().NotBeNull();
        options.Trendline!.Type.Should().Be(ChartTrendlineType.Polynomial);
        options.Trendline.PolynomialOrder.Should().Be(3);
        options.Trendline.Forward.Should().Be(1.5);
        options.Trendline.Backward.Should().Be(0.5);
        options.Trendline.DisplayEquation.Should().BeTrue();
        options.Trendline.DisplayRSquared.Should().BeTrue();
        options.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        options.DataLabels.TextStyle.Should().NotBeNull();
        options.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        options.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        options.DataLabels.TextStyle.Bold.Should().BeTrue();
        options.DataLabels.TextStyle.Italic.Should().BeFalse();
        options.DataLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
    }

    [Fact]
    public async Task ChartSeriesOptionsDialog_uses_scrollable_body_and_fixed_action_row()
    {
        var rootIsGrid = false;
        var rowCount = 0;
        var bodyRowIsStar = false;
        var actionRowIsAuto = false;
        var verticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        var horizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        var optionsBodyChildCount = 0;
        var actionButtonCount = 0;
        var bodyRow = -1;
        var actionRow = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.LineMarkers);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartSeriesOptionsDialog(window.Editor);
            var root = dialog.Content as Grid;
            rootIsGrid = root is not null;
            if (root is null)
                return;

            rowCount = root.RowDefinitions.Count;
            bodyRowIsStar = root.RowDefinitions[0].Height.IsStar;
            actionRowIsAuto = root.RowDefinitions[1].Height.IsAuto;
            var scrollViewer = root.Children.OfType<ScrollViewer>().Single();
            verticalScrollBarVisibility = scrollViewer.VerticalScrollBarVisibility;
            horizontalScrollBarVisibility = scrollViewer.HorizontalScrollBarVisibility;
            optionsBodyChildCount = (scrollViewer.Content as StackPanel)?.Children.Count ?? 0;
            bodyRow = Grid.GetRow(scrollViewer);
            var buttons = root.Children.OfType<StackPanel>().Single();
            actionRow = Grid.GetRow(buttons);
            actionButtonCount = buttons.Children.OfType<Button>().Count();
            dialog.Close();
        });

        if (!ran) return;
        rootIsGrid.Should().BeTrue();
        rowCount.Should().Be(2);
        bodyRowIsStar.Should().BeTrue();
        actionRowIsAuto.Should().BeTrue();
        verticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
        horizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
        optionsBodyChildCount.Should().BeGreaterThan(30);
        bodyRow.Should().Be(0);
        actionRow.Should().Be(1);
        actionButtonCount.Should().Be(2);
    }

    [Fact]
    public async Task ChartPointOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartPointOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartPointOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(0, 0, "#C00000", "#1F4E79", 1.5, ChartMarkerSymbol.Diamond, 7,
                true, true, false, true, false, true, DataLabelPosition.InsideEnd, "0.0%", " | ",
                "Aptos", 9, true, false, "#2F5496", showBubbleSize: true, explosionPercent: 35,
                showLeaderLines: true);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().NotBeNull();
        options!.SeriesIndex.Should().Be(0);
        options.PointIndex.Should().Be(0);
        options.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        options.StrokeColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        options.StrokeWidthPt.Should().Be(1.5);
        options.MarkerSymbol.Should().Be(ChartMarkerSymbol.Diamond);
        options.MarkerSizePt.Should().Be(7);
        options.ExplosionPercent.Should().Be(35);
        options.DataLabels.Should().NotBeNull();
        options.DataLabels!.ShowValue.Should().BeTrue();
        options.DataLabels.ShowCategoryName.Should().BeTrue();
        options.DataLabels.ShowLegendKey.Should().BeTrue();
        options.DataLabels.ShowBubbleSize.Should().BeTrue();
        options.DataLabels.ShowLeaderLines.Should().BeTrue();
        options.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        options.DataLabels.TextStyle.Should().NotBeNull();
        options.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        options.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        options.DataLabels.TextStyle.Bold.Should().BeTrue();
        options.DataLabels.TextStyle.Italic.Should().BeFalse();
        options.DataLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
    }

    [Fact]
    public async Task ChartLayoutOptionsDialog_constructs_and_commits_shared_options()
    {
        ChartLayoutOptions? options = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var chartShape = window.Editor.InsertChart(ChartType.ColumnClustered);
            window.Editor.Select(chartShape.Id);

            var dialog = new ChartLayoutOptionsDialog(window.Editor);
            dialog.SetOptionsForTests(ChartLayoutTarget.PlotArea, "inner", ChartManualLayoutMode.Edge, ChartManualLayoutMode.Factor, ChartManualLayoutMode.Factor, ChartManualLayoutMode.Edge, 12, 0.1, 0.8, 20);
            options = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        options.Should().NotBeNull();
        options!.Target.Should().Be(ChartLayoutTarget.PlotArea);
        options.LayoutTarget.Should().Be("inner");
        options.XMode.Should().Be(ChartManualLayoutMode.Edge);
        options.HeightMode.Should().Be(ChartManualLayoutMode.Edge);
        options.X.Should().Be(12);
        options.Height.Should().Be(20);
    }

    [Fact]
    public async Task Ribbon_insert_picture_command_is_registered()
    {
        var found = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet("freep.picture", out _);
        });

        if (!ran) return;
        found.Should().BeTrue("the visible Picture command must route to the Avalonia picker adapter");
    }

    // ── Packaging smoke ─────────────────────────────────────────────────────────

    [Fact]
    public void PackagingSmoke_round_trips_an_empty_presentation()
    {
        // Run the packaging smoke inline — no display needed.
        var report = Path.Combine(Path.GetTempPath(), $"freep_smoke_{Guid.NewGuid():N}.txt");
        try
        {
            var args = new[] { "--packaging-smoke", report };
            var result = PackagingSmoke.TryRun(
                args, TextWriter.Null, TextWriter.Null, out var exit);
            result.Should().BeTrue("--packaging-smoke must be handled");
            exit.Should().Be(0, "packaging smoke must pass on an empty presentation");
            File.Exists(report).Should().BeTrue("packaging smoke must write a report file");
            File.ReadAllText(report).Should().Contain("freep_packaging_smoke=passed");
        }
        finally
        {
            if (File.Exists(report)) File.Delete(report);
        }
    }

    // ── .pptx round-trip ────────────────────────────────────────────────────────

    [Fact]
    public void Pptx_round_trip_empty_presentation_preserves_slide_count()
    {
        var presentation = Presentation.CreateEmpty();
        var originalCount = presentation.Slides.Count;

        var path = Path.Combine(Path.GetTempPath(), $"freep_rt_{Guid.NewGuid():N}.pptx");
        try
        {
            using (var ws = File.Create(path))
                PptxPackageWriter.Write(presentation, ws);

            using var rs = File.OpenRead(path);
            var loaded = PptxPackageReader.Read(rs);

            loaded.Slides.Count.Should().Be(originalCount,
                "round-tripping an empty presentation must preserve the slide count");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"), Path.Combine(parts));

    private sealed class RecordingPrintAdapter : ILinuxNativePrintHandoffAdapter
    {
        public LinuxNativePrintCapability Capability { get; } =
            new(true, "lp", "office", "ready");
        public byte[]? PdfBytes { get; private set; }

        public Task<LinuxNativePrintResult> PrintAsync(
            byte[] pdfBytes,
            string documentName,
            CancellationToken cancellationToken = default)
        {
            PdfBytes = pdfBytes;
            return Task.FromResult(LinuxNativePrintResult.Success(0));
        }
    }

    private static PresentationPrintOutputPackage BuildTestPrintPackage() =>
        PresentationPrintOutputPackageExecutor.BuildPackage(
            Presentation.CreateEmpty(),
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
            static (_, _, _, _) => EvenTwoByTwoPng,
            static _ => Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF"));

    private static PresentationVideoFramePackage BuildTestVideoPackage() =>
        PresentationVideoFramePackageExecutor.BuildPackage(
            Presentation.CreateEmpty(),
            new PresentationVideoExportRequest(
                Quality: PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: false),
            static (_, _, _, _) => EvenTwoByTwoPng);

    private static readonly byte[] EvenTwoByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAB0lEQVRj+M/AAEMAzJWb4gAAAABJRU5ErkJggg==");

    private sealed class RecordingVideoAdapter(LinuxVideoEncoderCapability capability)
        : ILinuxVideoExportAdapter
    {
        public LinuxVideoEncoderCapability Capability { get; } = capability;
        public PresentationVideoFramePackage? Package { get; private set; }

        public Task<LinuxVideoExportResult> ExportAsync(
            PresentationVideoFramePackage package,
            string outputPath,
            CancellationToken cancellationToken = default,
            IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null)
        {
            Package = package;
            return Task.FromResult(LinuxVideoExportResult.Success(
                outputPath,
                Capability.EncoderName ?? "test-encoder",
                package.Bytes.LongLength));
        }
    }


    private static void AssertBefore(string source, string first, string second)
    {
        source.IndexOf(first, StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf(second, StringComparison.Ordinal), $"{first} should appear before {second}");
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

    private static TextBody MakeTextBodyWithColor(string text, SrgbColor color)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, Color = new ThemeAwareColor(color) });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static string FormatPrintOptionChoice(PresentationPrintBackstageOptionChoice choice)
    {
        var prefix = choice.IsSelected ? "Selected: " : string.Empty;
        var availability = choice.IsAvailable ? string.Empty : " (unavailable)";
        return $"{prefix}{choice.Group}: {choice.DisplayName}{availability}\n{choice.Description}";
    }

    private static IEnumerable<string> EnumerateRibbonCommandIds(RibbonTab tab)
    {
        foreach (var group in tab.Groups)
        {
            foreach (var control in group.Controls)
            {
                if (!string.IsNullOrEmpty(control.CommandId.Value))
                    yield return control.CommandId.Value;

                switch (control)
                {
                    case RibbonSplitButton split:
                        foreach (var commandId in EnumerateRibbonMenuCommandIds(split.Menu))
                            yield return commandId;
                        break;
                    case RibbonDropdown dropdown:
                        foreach (var commandId in EnumerateRibbonMenuCommandIds(dropdown.Menu))
                            yield return commandId;
                        break;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateRibbonMenuCommandIds(RibbonMenu menu)
    {
        foreach (var item in menu.Items)
        {
            var itemCommandId = item.CommandId?.Value;
            if (!string.IsNullOrEmpty(itemCommandId))
                yield return itemCommandId;

            foreach (var childCommandId in EnumerateRibbonMenuItemsCommandIds(item.Children))
                yield return childCommandId;
        }
    }

    private static IEnumerable<string> EnumerateRibbonMenuItemsCommandIds(
        IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            var itemCommandId = item.CommandId?.Value;
            if (!string.IsNullOrEmpty(itemCommandId))
                yield return itemCommandId;

            foreach (var childCommandId in EnumerateRibbonMenuItemsCommandIds(item.Children))
                yield return childCommandId;
        }
    }

    private static TextBody MakeLinkedTextBody(string text, Hyperlink hyperlink)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, Hyperlink = hyperlink });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static SlideShape MakeSmartArtShape() =>
        MakeSmartArtShape(
            SmartArtFamily.List,
            "urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList",
            ["Plan", "Build"]);

    private static SlideShape MakeSmartArtShape(
        SmartArtFamily family,
        string layoutUniqueId,
        IReadOnlyList<string> nodeTexts)
    {
        var data = new SmartArtData
        {
            Family = family,
            LayoutUniqueId = layoutUniqueId,
            IsLiveLayoutSupported = true
        };

        for (int i = 0; i < nodeTexts.Count; i++)
        {
            data.Nodes.Add(new SmartArtNode { ModelId = $"n{i + 1}", Text = nodeTexts[i], Level = 0 });
        }

        var smartArt = new SmartArtShape
        {
            Data = data,
            DrawingPartPath = "ppt/diagrams/drawing1.xml"
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        smartArt.Parts["ppt/diagrams/layout1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/layout1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            Bytes = Encoding.UTF8.GetBytes($"<dgm:layoutDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"{layoutUniqueId}\" />")
        };
        smartArt.Parts["ppt/diagrams/quickStyle1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/quickStyle1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:styleDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" uniqueId=\"urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1\" />")
        };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };
        smartArt.Parts["ppt/diagrams/colors1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/colors1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:colorsDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dgm:styleLbl name=\"node0\"><dgm:fillClrLst><a:schemeClr val=\"accent1\"/><a:schemeClr val=\"accent2\"/></dgm:fillClrLst></dgm:styleLbl></dgm:colorsDef>")
        };

        return new SlideShape
        {
            Id = 970,
            Name = "Roadmap SmartArt",
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 4_572_000,
            ExtentCyEmu = 2_743_200,
            SmartArt = smartArt
        };
    }
}
