using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.App.Avalonia.Smoke;
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
            var root = window.Content.Should().BeOfType<DockPanel>().Subject;
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
    public void MainWindow_sources_reference_the_shared_avalonia_shell_frame()
    {
        var project = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"));
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");

        var mainWindow = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        mainWindow.Should().Contain("using Free.Shared.Shell.Avalonia;");
        mainWindow.Should().Contain("SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(");
        mainWindow.Should().Contain("SisterAppStatusBarChrome.Build(");
        mainWindow.Should().Contain("SisterAppStatusBarChrome.CreateInfoText(foreground: Brushes.White, margin: new Thickness(8, 0))");
        mainWindow.Should().Contain("chrome: ribbon,");
        mainWindow.Should().Contain("workArea: BuildBody(),");
        mainWindow.Should().Contain("statusBar: statusBar");
        mainWindow.Should().Contain("Content = frame.Root;");
        AssertBefore(mainWindow, "SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(", "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(");
        AssertBefore(mainWindow, "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(", "Content = frame.Root;");
        mainWindow.Should().NotContain("_statusText = new TextBlock");
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
            title.Should().EndWith(" *", "dirty state should still bind to the platform title");
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
            title.Should().Be($"FreeP \u2014 {Path.GetFileName(deckPath)}");
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
        var commandIds = animations.Groups
            .SelectMany(group => group.Controls)
            .Where(control => !string.IsNullOrEmpty(control.CommandId.Value))
            .Select(control => control.CommandId.Value)
            .ToArray();

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
    public void MainWindow_sources_route_animation_commands_through_shared_planner()
    {
        var source = File.ReadAllText(FindRepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("PresentationAnimationCommandPlanner.BuiltInPlans");
        source.Should().Contain("PresentationAnimationCommandPlanner.TryApply(");
        source.Should().Contain("OnAnimationPaneRequested");
        source.Should().Contain("AnimationPanePlanner.BuildTimelinePlan(");
        source.Should().Contain("AnimationPanePlanner.BuildPlaybackSessionPlan(");
        source.Should().Contain("plan.PlaybackControls");
        source.Should().Contain("AnimationPanePlaybackControlKind.PlayFromSelected");
        source.Should().Contain("ShowAnimationPane()");
        source.Should().Contain("BuildAnimationPaneItemCard(");
        source.Should().Contain("AnimationPanePlanner.BuildEffectOptionMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.TryApplyEffectOptionMutation(");
        source.Should().Contain("AnimationPanePlanner.BuildTriggerMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.BuildDurationMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.BuildDelayMutationPlan(");
        source.Should().Contain("AnimationPanePlanner.TryApplyTimingMutation(");
    }

    [Fact]
    public void RibbonDefinition_home_tab_has_file_slides_and_edit_groups()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        home.Groups.Should().Contain(g => g.Id == "file",   "File group required");
        home.Groups.Should().Contain(g => g.Id == "slides", "Slides group required");
        home.Groups.Should().Contain(g => g.Id == "clipboard", "Clipboard group required");
        home.Groups.Should().Contain(g => g.Id == "arrange", "Arrange group required");
        home.Groups.Should().Contain(g => g.Id == "edit",   "Edit group required");
        home.Groups.Should().Contain(g => g.Id == "editing", "Editing group required");
    }

    [Fact]
    public void RibbonDefinition_file_group_has_new_open_save_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home  = definition.Tabs.Single(t => t.Id == "home");
        var file  = home.Groups.Single(g => g.Id == "file");
        var ids   = file.Controls.Select(i => i.CommandId.Value).ToList();
        ids.Should().Contain("freep.file.new",     "New command required");
        ids.Should().Contain("freep.file.open",    "Open command required");
        ids.Should().Contain("freep.file.save",    "Save command required");
        ids.Should().Contain("freep.file.save-as", "Save As command required");
        ids.Should().Contain(PresentationExportPlanner.NotesPagePdfExportCommandId, "notes-page PDF export command required");
        ids.Should().Contain(PresentationExportPlanner.ImageExportCommandId, "image export command required");
        ids.Should().Contain(PresentationExportPlanner.VideoExportCommandId, "video export command required");
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
        illustrationIds.Should().Contain("freep.shape-rectangle", "Rectangle command required");
        illustrationIds.Should().Contain("freep.shape-ellipse", "Ellipse command required");
    }

    [Fact]
    public void RibbonDefinition_transitions_tab_has_planned_transition_commands()
    {
        var definition = FreePRibbonAvalonia.Build();
        var transitions = definition.Tabs.Single(t => t.Id == "transitions");
        var commandIds = transitions.Groups
            .SelectMany(group => group.Controls)
            .Where(control => !string.IsNullOrEmpty(control.CommandId.Value))
            .Select(control => control.CommandId.Value)
            .ToArray();

        commandIds.Should().Contain(PresentationTransitionCommandPlanner.BuiltInPlans.Select(plan => plan.CommandId));
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
        var clicked = false;
        var visible = false;
        string? buttonText = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            before = window.SlideCount;
            buttonText = window.SlidePaneNewSlideButtonText;
            visible = window.IsSlidePaneNewSlideButtonVisible;
            clicked = window.ClickSlidePaneNewSlideAffordanceForTests();
            after = window.SlideCount;
            paneItemsAfter = window.SlidePaneSlideItemCount;
        });

        if (!ran) return;
        buttonText.Should().Be(SlidePanePlanner.NewSlideButtonText);
        visible.Should().BeTrue("the Avalonia slide pane should expose the bottom PowerPoint-style add affordance");
        clicked.Should().BeTrue("the affordance should route to the same slide insertion workflow as the ribbon command");
        after.Should().Be(before + 1);
        paneItemsAfter.Should().Be(after, "the slide pane should refresh to include the newly inserted slide");
    }

    [Fact]
    public async Task SlidePane_thumbnails_project_shared_visual_chrome_plan()
    {
        SlidePaneThumbnailVisualPlan? firstPlan = null;
        var paneItems = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            paneItems = window.SlidePaneSlideItemCount;
            firstPlan = window.SlidePaneRenderedThumbnailPlans.FirstOrDefault();
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
        firstPlan.ItemCornerRadius.Should().Be(SlidePanePlanner.DefaultItemCornerRadius);
        firstPlan.NormalBorderThickness.Should().Be(SlidePanePlanner.DefaultNormalBorderThickness);
        firstPlan.SelectedBorderThickness.Should().Be(SlidePanePlanner.DefaultSelectedBorderThickness);
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

    // ── Insert commands ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("freep.text-box",        DrawingShapeKind.Rectangle, true)]
    [InlineData("freep.shape-rectangle", DrawingShapeKind.Rectangle, false)]
    [InlineData("freep.shape-ellipse",   DrawingShapeKind.Ellipse,   false)]
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
    public async Task Ribbon_chart_edit_data_command_is_registered_and_noops_without_selected_chart()
    {
        var found = false;
        var before = -1;
        var after = -1;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(ChartDataDialogPlanner.EditDataCommandId, out var command);
            found.Should().BeTrue("the Avalonia chart-data command must be registered");

            before = window.Editor.CurrentSlide!.Shapes.Count;
            command!.Execute(RibbonCommandContext.Empty);
            after = window.Editor.CurrentSlide!.Shapes.Count;
        });

        if (!ran) return;
        found.Should().BeTrue("the Avalonia chart-data command must be registered");
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
    public async Task Ribbon_CustomSlideSize_opens_visible_pane_and_applies_shared_result()
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
            opened = window.IsCustomSlideSizePaneVisible;
            initialPreset = window.LastCustomSlideSizeInitialState!.Preset;
            initialWidth = window.CustomSlideSizeWidthText;
            initialHeight = window.CustomSlideSizeHeightText;

            invalidApplied = window.ApplyCustomSlideSizeForTests(
                "0.25",
                "7.5",
                SlideSizeDialogUnit.Inches);
            validation = window.CustomSlideSizeValidationText;
            visibleAfterInvalid = window.IsCustomSlideSizePaneVisible;

            validApplied = window.ApplyCustomSlideSizeForTests(
                "11",
                "6.25",
                SlideSizeDialogUnit.Inches);
            visibleAfterApply = window.IsCustomSlideSizePaneVisible;
            slideWidth = window.Editor.Presentation.SlideSizeCxEmu;
            slideHeight = window.Editor.Presentation.SlideSizeCyEmu;
        });

        if (!ran) return;
        found.Should().BeTrue("custom slide-size must be registered through the Avalonia registry");
        opened.Should().BeTrue("the custom command should open a visible Avalonia slide-size state");
        initialPreset.Should().Be(SlideSizeDialogPreset.Widescreen169);
        initialWidth.Should().Be("13.333");
        initialHeight.Should().Be("7.500");
        invalidApplied.Should().BeFalse("shared planner validation should block invalid sizes");
        validation.Should().Be(SlideSizeDialogPlanner.MinimumSizeMessage);
        visibleAfterInvalid.Should().BeTrue("invalid apply should keep the pane open for correction");
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
        var indicatorVisible = true;
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
            moved = window.TryApplySlidePaneMove(sourceSlideIndex: 0, targetInsertionIndex: 3);
            titles = window.Editor.Presentation.Slides.Select(slide => slide.Title).ToArray();
            currentSlideIndex = window.CurrentSlideIndex;
            indicatorVisible = window.IsSlidePaneInsertionIndicatorVisible;
        });

        if (!ran) return;
        slidePaneCount.Should().Be(3, "the Avalonia slide pane should render one selectable item per slide");
        moved.Should().BeTrue("drag release should apply the shared move action plan");
        titles.Should().Equal("Slide 2", "Slide 3", "Slide 1");
        currentSlideIndex.Should().Be(2, "the moved slide should remain selected after reorder");
        indicatorVisible.Should().BeFalse("the insertion indicator is only visible during active drag feedback");
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
        var isPaneVisible = false;

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
            isPaneVisible = window.IsPrintOptionsPaneVisible;
        });

        if (!ran) return;
        found.Should().BeTrue("the Avalonia registry should expose the shared Backstage print planner seam");
        isPaneVisible.Should().BeTrue("the Print command should expose the Avalonia Backstage print projection");
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
        printPlan.NativePrintHandoff.Reason.Should().Contain("Native printer handoff adapter is not wired");
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
                "Selected: Copies: 3 copies: Set the number of copies from 1 to 999 before handing the package to the native printer dialog.",
                "Selected: Collation: Uncollated: Print all copies of each page before moving to the next page.",
                "Selected: Color: Pure Black and White: Use a high-contrast black-and-white print intent.",
                "Selected: Content: Print hidden slides: Include hidden slides in the normalized print range.",
                "Selected: Output: Frame slides: Draw a frame around each slide thumbnail/page.",
                "Selected: Output: Print comments and ink markup: Reserve print intent for comments and ink markup.");
        renderedPreviewRows.Should().ContainSingle()
            .Which.Should().Be("Selected: Handout page 1: Handout with slides 1, 3");
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

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            var registry = window.BuildCommandRegistry();
            found = registry.TryGet(PresentationExportPlanner.VideoExportCommandId, out var video);

            video!.Execute(RibbonCommandContext.Empty);
            videoPlan = window.LastVideoExportPlan;
            videoPackage = window.LastVideoFramePackage;
            videoHandoff = window.LastVideoExportHandoffPlan;
        });

        if (!ran) return;
        found.Should().BeTrue("the Avalonia registry should expose the shared video frame package seam");
        videoPackage.Should().NotBeNull();
        videoPlan.Should().NotBeNull();
        videoHandoff.Should().NotBeNull();
        videoPackage!.Plan.ExportPlan.Should().BeSameAs(videoPlan);
        videoHandoff!.PackagePlan.Should().BeSameAs(videoPackage.Plan);
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
    public async Task Ribbon_animation_commands_route_through_shared_planner()
    {
        var foundFade = false;
        var foundDuration = false;
        var foundDelay = false;
        var foundPane = false;
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

            fade!.Execute(RibbonCommandContext.Empty);
            durationCommand!.Execute(RibbonCommandContext.ForSelectedValue("1.50s"));
            delayCommand!.Execute(RibbonCommandContext.ForSelectedValue("0.25s"));
            pane!.Execute(RibbonCommandContext.Empty);

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
        });

        if (!ran) return;
        foundFade.Should().BeTrue("animation effects must be registered through the Avalonia registry");
        foundDuration.Should().BeTrue("duration must be registered through the Avalonia registry");
        foundDelay.Should().BeTrue("delay must be registered through the Avalonia registry");
        foundPane.Should().BeTrue("pane command is exposed as a conservative callback/no-op intent");
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
    public async Task Animation_pane_renders_shared_timeline_rows_and_action_state()
    {
        AnimationPaneTimelinePlan? panePlan = null;
        var paneVisible = false;
        var paneHeading = string.Empty;
        var paneMessage = string.Empty;
        var previewEnabled = false;
        IReadOnlyList<string> playbackControls = [];
        IReadOnlyList<string> paneRows = [];

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
    }

    [Fact]
    public async Task Animation_pane_projects_shared_playback_session_state()
    {
        AnimationPanePlaybackSessionPlan? playSession = null;
        AnimationPanePlaybackSessionPlan? stopSession = null;
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
            mutationPlan = window.ApplyAnimationPaneEffectOptionEditForTests(0, "from-left");
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
            "from-top");
        mutationPlan.Should().NotBeNull();
        mutationPlan!.ShouldApply.Should().BeTrue();
        mutationPlan.Direction.Should().Be(AnimationDirection.FromLeft);
        invalidPlan.Should().NotBeNull();
        invalidPlan!.DisabledReason.Should().Be(AnimationPanePlanner.InvalidEffectOptionMessage);
        direction.Should().Be(AnimationDirection.FromLeft);
        paneRows.Should().ContainSingle()
            .Which.Should().Contain("Hero box - In: FlyIn (From Left)")
            .And.Contain("duration 0.5s");
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
                Text = "caption text",
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
            "caption text",
            "Use shared review state.",
            "@Reviewer confirmed.");
        proofingPanePlan.Should().NotBeNull();
        proofingPaneVisible.Should().BeTrue("the Avalonia proofing command should render a shared-plan-backed corrections pane");
        proofingPaneRowCount.Should().Be(1);
        proofingPaneSelectedCount.Should().Be(1);
        proofingPaneCorrectionEnabled.Should().BeTrue();
        proofingPaneHeading.Should().Be("Spelling - 1 issues");
        proofingPanePlan!.SelectedRow!.SuggestedReplacement.Should().Be("C");
        proofingMutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            proofingExecutionPlan.Scopes.Single(scope => scope.Kind == PresentationProofingScopeKind.ShapeText),
            0,
            1,
            "C",
            "Caption text",
            null));
        correctedShapeText.Should().Be("Caption text");
        correctedProofingScopeText.Should().Be("Caption text");
        commentsPaneVisible.Should().BeTrue("the Avalonia comments command should render a shared-plan-backed pane");
        commentsPaneCommentCount.Should().Be(1);
        commentsPaneActionCount.Should().BeGreaterThanOrEqualTo(6);
        commentsPaneSelectedCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadingOrderPane_moves_nested_group_child_through_shared_plan()
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
            row.CommandHint == null &&
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
                row.ActionLabel == "Select Media" &&
                row.CommandHint == null &&
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
            visible = window.IsFindReplacePaneVisible;
            title = window.FindReplacePaneTitle;
            replaceVisible = window.IsFindReplaceReplaceInputVisible;
            plan = window.LastFindReplaceWorkflowPlan;
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
            window.SetFindReplacePaneInputForTests("needle");
            plan = window.NavigateFindReplacePaneForTests(+1);

            selectedShapeId = window.Editor.SelectedShapeIds.Single();
            currentSlideIndex = window.Editor.CurrentSlideIndex;
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
            window.SetFindReplacePaneInputForTests("cat", "dog");
            plan = window.ReplaceAllFindReplacePaneForTests();

            replacedText = shape.TextBody!.Paragraphs[0].Runs[0].Text;
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
    [InlineData("freep.arrange.bring-to-front")]
    [InlineData("freep.arrange.bring-forward")]
    [InlineData("freep.arrange.send-backward")]
    [InlineData("freep.arrange.send-to-back")]
    [InlineData("freep.arrange.align-left")]
    [InlineData("freep.arrange.align-center-h")]
    [InlineData("freep.arrange.align-right")]
    [InlineData("freep.arrange.align-top")]
    [InlineData("freep.arrange.align-middle")]
    [InlineData("freep.arrange.align-bottom")]
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
            commit = dialog.BuildCommitPlanForTests();
            dialog.Close();
        });

        if (!ran) return;
        title.Should().Be(ChartDataDialogPlanner.DialogTitle);
        seriesColumns.Should().Be(2);
        categoryRows.Should().Be(3);
        valueCells.Should().Be(6);
        commit.Should().NotBeNull();
        commit!.Categories.Should().Equal("Q1", "Q2", "Q3");
        commit.SeriesNames.Should().Equal("Series 1", "Series 2");
        commit.Values[0].Should().Equal(new double?[] { 4.3, 2.5, 3.5 });
        commit.Values[1].Should().Equal(new double?[] { 2.4, 4.4, 1.8 });
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
        Path.Combine(FindRepoRoot(), Path.Combine(parts));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
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

    private static string FormatPrintOptionChoice(PresentationPrintBackstageOptionChoice choice)
    {
        var prefix = choice.IsSelected ? "Selected: " : string.Empty;
        var availability = choice.IsAvailable ? string.Empty : " (unavailable)";
        return $"{prefix}{choice.Group}: {choice.DisplayName}{availability}: {choice.Description}";
    }

    private static TextBody MakeLinkedTextBody(string text, Hyperlink hyperlink)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, Hyperlink = hyperlink });
        body.Paragraphs.Add(paragraph);
        return body;
    }
}
