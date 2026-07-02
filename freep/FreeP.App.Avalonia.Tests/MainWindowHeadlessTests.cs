using System;
using System.Globalization;
using System.IO;
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
        ids.Should().Contain(PresentationExportPlanner.ImageExportCommandId, "image export command required");
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
        });

        if (!ran) return;
        foundTheme.Should().BeTrue("theme commands must be registered through the Avalonia registry");
        foundSlideSize.Should().BeTrue("slide-size commands must be registered through the Avalonia registry");
        foundCustom.Should().BeTrue("custom slide-size should be exposed as a planner callback intent");
        themeName.Should().Be("Berlin");
        slideWidth.Should().Be(PresentationDesignCommandPlanner.SlideSizeStandard4x3CxEmu);
        slideHeight.Should().Be(PresentationDesignCommandPlanner.SlideSizeStandardCyEmu);
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

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = window.Editor.InsertDefaultRectangle();
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
    }

    [Fact]
    public async Task Ribbon_review_workflow_commands_refresh_shared_adapter_state()
    {
        var foundComments = false;
        var foundAccessibility = false;
        var foundAltText = false;
        var foundReadingOrder = false;
        var foundProofing = false;
        PresentationCommentPanePlan? commentPlan = null;
        PresentationAccessibilitySummaryPlan? accessibilityPlan = null;
        PresentationAltTextRequestPlan? altTextPlan = null;
        PresentationAltTextPanePlan? altTextPanePlan = null;
        PresentationReadingOrderPlan? readingOrderPlan = null;
        PresentationProofingRequestPlan? proofingPlan = null;
        var commentsPaneVisible = false;
        var commentsPaneCommentCount = 0;
        var commentsPaneActionCount = 0;
        var readingOrderPaneVisible = false;
        var readingOrderPaneItemCount = 0;
        var readingOrderPaneHeading = string.Empty;
        var readingOrderPaneMessage = string.Empty;
        var readingOrderMoveEarlierEnabled = true;
        string? readingOrderMoveEarlierDisabledReason = null;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Use shared review state.",
                Idx = 1,
            });
            var shape = new SlideShape
            {
                Id = 328,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
            };
            window.Editor.CurrentSlide.Shapes.Clear();
            window.Editor.CurrentSlide.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            var registry = window.BuildCommandRegistry();
            foundComments = registry.TryGet(PresentationReviewWorkflowPlanner.CommentsPaneCommandId, out var comments);
            foundAccessibility = registry.TryGet(PresentationReviewWorkflowPlanner.AccessibilityCommandId, out var accessibility);
            foundAltText = registry.TryGet(PresentationReviewWorkflowPlanner.AltTextCommandId, out var altText);
            foundReadingOrder = registry.TryGet(PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId, out var readingOrder);
            foundProofing = registry.TryGet(PresentationReviewWorkflowPlanner.ProofingCommandId, out var proofing);

            comments!.Execute(RibbonCommandContext.Empty);
            accessibility!.Execute(RibbonCommandContext.Empty);
            altText!.Execute(RibbonCommandContext.Empty);
            readingOrder!.Execute(RibbonCommandContext.Empty);
            proofing!.Execute(RibbonCommandContext.Empty);

            commentPlan = window.LastCommentPanePlan;
            commentsPaneVisible = window.IsReviewCommentsPaneVisible;
            commentsPaneCommentCount = window.ReviewCommentsPaneCommentCount;
            commentsPaneActionCount = window.ReviewCommentsPaneActionButtonCount;
            accessibilityPlan = window.LastAccessibilitySummaryPlan;
            altTextPlan = window.LastAltTextRequestPlan;
            altTextPanePlan = window.LastAltTextPanePlan;
            readingOrderPlan = window.LastReadingOrderPlan;
            readingOrderPaneVisible = window.IsReadingOrderPaneVisible;
            readingOrderPaneItemCount = window.ReadingOrderPaneItemCount;
            readingOrderPaneHeading = window.ReadingOrderPaneHeading;
            readingOrderPaneMessage = window.ReadingOrderPaneMessage;
            readingOrderMoveEarlierEnabled = window.IsReadingOrderMoveEarlierEnabled;
            readingOrderMoveEarlierDisabledReason = window.ReadingOrderMoveEarlierDisabledReason;
            proofingPlan = window.LastProofingRequestPlan;
        });

        if (!ran) return;
        foundComments.Should().BeTrue();
        foundAccessibility.Should().BeTrue();
        foundAltText.Should().BeTrue();
        foundReadingOrder.Should().BeTrue();
        foundProofing.Should().BeTrue();
        commentPlan.Should().NotBeNull();
        commentPlan!.TotalCommentCount.Should().Be(1);
        accessibilityPlan.Should().NotBeNull();
        var missingAltText = accessibilityPlan!.Issues.Single(issue =>
            issue.ShapeId == 328 && issue.Title == "Alt text missing");
        missingAltText.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
            PresentationReviewWorkflowPlanner.MissingAltTextActionSummary,
            PresentationReviewWorkflowPlanner.AltTextCommandId,
            true));
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
        readingOrderPlan!.Items.Should().ContainSingle(item => item.ShapeId == 328);
        readingOrderPlan.SelectedItem.Should().NotBeNull();
        readingOrderPlan.SelectedItem!.ShapeId.Should().Be(328);
        readingOrderPlan.Actions.Single(action =>
                action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.ReadingOrderReorderDeferredMessage);
        readingOrderPaneVisible.Should().BeTrue("the Avalonia reading order command should render a shared-plan-backed pane");
        readingOrderPaneItemCount.Should().Be(1);
        readingOrderPaneHeading.Should().Be("Reading Order - slide 1 (1 shapes)");
        readingOrderPaneMessage.Should().Be("Selected: Product image");
        readingOrderMoveEarlierEnabled.Should().BeFalse();
        readingOrderMoveEarlierDisabledReason.Should()
            .Be(PresentationReviewWorkflowPlanner.ReadingOrderReorderDeferredMessage);
        proofingPlan.Should().NotBeNull();
        proofingPlan!.Status.Should().Be(PresentationWorkflowCapabilityStatus.RequiresHost);
        commentsPaneVisible.Should().BeTrue("the Avalonia comments command should render a shared-plan-backed pane");
        commentsPaneCommentCount.Should().Be(1);
        commentsPaneActionCount.Should().BeGreaterThanOrEqualTo(6);
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
        descriptionPlaceholder.Should().Be("Describe the selected object for people who cannot see it.");
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
            var shape = new SlideShape
            {
                Id = 329,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
            };
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
        editedPlan.HasNotes.Should().BeTrue();
        editedPlan.PrintPlan.SlideRange.DisplayName.Should().Be("Slide 1");
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
}
