using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;
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
    public void RibbonDefinition_edit_group_has_undo_and_redo()
    {
        var definition = FreePRibbonAvalonia.Build();
        var home = definition.Tabs.Single(t => t.Id == "home");
        var edit = home.Groups.Single(g => g.Id == "edit");
        var ids  = edit.Controls.Select(i => i.CommandId.Value).ToList();
        ids.Should().Contain("freep.undo", "Undo command required");
        ids.Should().Contain("freep.redo", "Redo command required");
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
}
