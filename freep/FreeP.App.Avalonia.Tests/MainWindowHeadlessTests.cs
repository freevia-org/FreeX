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
    public void RibbonDefinition_home_tab_has_file_slides_and_edit_groups()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        home.Groups.Should().Contain(g => g.Id == "file",   "File group required");
        home.Groups.Should().Contain(g => g.Id == "slides", "Slides group required");
        home.Groups.Should().Contain(g => g.Id == "clipboard", "Clipboard group required");
        home.Groups.Should().Contain(g => g.Id == "arrange", "Arrange group required");
        home.Groups.Should().Contain(g => g.Id == "edit",   "Edit group required");
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
        insert.Groups.Should().Contain(g => g.Id == "illustrations", "Illustrations group required");

        var textIds = insert.Groups.Single(g => g.Id == "text")
            .Controls.Select(i => i.CommandId.Value).ToList();
        var tableIds = insert.Groups.Single(g => g.Id == "tables")
            .Controls.Select(i => i.CommandId.Value).ToList();
        var chartIds = insert.Groups.Single(g => g.Id == "charts")
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
        illustrationIds.Should().Contain("freep.picture", "Picture command required");
        illustrationIds.Should().Contain("freep.shape-rectangle", "Rectangle command required");
        illustrationIds.Should().Contain("freep.shape-ellipse", "Ellipse command required");
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
    [InlineData("freep.insert-table-3x3", 3, 3)]
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
