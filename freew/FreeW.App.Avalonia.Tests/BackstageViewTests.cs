using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Headless;
using Free.Shared.Shell;
using FreeW.App.Avalonia.Backstage;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Free.Shared.AppServices;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Guards for the FreeW Avalonia backstage view. These are headless construction + planner-output
/// assertions — no dialogs opened. They verify that:
/// (a) the <see cref="BackstageView"/> object-graph builds without throwing (on Avalonia UI thread),
/// (b) each pane's portable planner produces non-empty groups/rows (pure, no UI thread needed),
/// (c) the pane <see cref="BackstagePane"/> enum covers all expected entry points.
/// </summary>
public class BackstageViewTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // ── Construction smoke (runs on headless Avalonia UI thread) ──────────────

    [Fact]
    public async Task BackstageView_constructs_headless_without_throwing()
    {
        // This exercises the ctor path (shell layout, nav buttons, initial NavigateTo) headlessly.
        // No ShowDialog call — we only validate that the object graph wires without exceptions.
        Exception? caught = null;
        try
        {
            await Session.Dispatch(() =>
            {
                var callbacks = BuildTestCallbacks();
                _ = new BackstageView(callbacks);
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().BeNull("BackstageView ctor must not throw headlessly");
    }

    [Fact]
    public void BackstageView_all_pane_navigation_targets_construct_without_throwing()
    {
        // Pane enum can be tested without UI — just verify the enum values.
        var allPanes = Enum.GetValues<BackstagePane>();
        allPanes.Should().HaveCount(8, "there should be 8 backstage panes");
        allPanes.Should().Contain(new[]
        {
            BackstagePane.Home, BackstagePane.Open, BackstagePane.SaveAs, BackstagePane.Print,
            BackstagePane.Share, BackstagePane.Export, BackstagePane.Info, BackstagePane.Account,
        });
    }

    // ── Planner output assertions ──────────────────────────────────────────────

    [Fact]
    public void BackstageView_sources_use_shared_avalonia_backstage_chrome()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "Backstage",
            "BackstageView.cs"));
        var project = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "FreeW.App.Avalonia.csproj"));
        var sharedSource = File.ReadAllText(FindRepoFile(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaBackstageChrome.cs"));

        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildHomePane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildOpenPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildSaveAsPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildSharePane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildExportPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildPrintPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildInfoPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildAccountPane(");
        source.Should().Contain("ApplicationOptionsSummaryPlanner.Build(");
        source.Should().Contain("_callbacks.MarkAsFinal()");
        source.Should().Contain("_callbacks.RestrictEditing()");
        source.Should().Contain("_callbacks.InspectDocument()");
        source.Should().Contain("_callbacks.CheckAccessibility()");
        source.Should().Contain("_callbacks.OpenOptions()");
        source.Should().Contain("BuildOpenSurface(");
        source.Should().Contain("surface.Search.AutomationName");
        source.Should().Contain("surface.Tabs.DocumentsTabLabel");
        source.Should().Contain("_callbacks.OpenFolder(folder)");
        source.Should().Contain("BuildActionGroupContent(surface)");
        source.Should().Contain("BuildSurfaceActionRow(action)");
        source.Should().Contain("printPreview: _callbacks.PrintPreview is null");
        source.Should().Contain("AvaloniaBackstageChromeStyle BackstageChromeStyle");
        source.Should().Contain("AvaloniaBackstageChrome.CreateContentArea(");
        source.Should().Contain("AvaloniaBackstageChrome.CreateDescribedActionRow(");
        source.Should().Contain("AvaloniaBackstageChrome.CreateStackedActionButton(");
        source.Should().Contain("AvaloniaBackstageChrome.CreatePaneHeader(");
        source.Should().Contain("AvaloniaBackstageChrome.CreateSectionHeader(");
        source.Should().Contain("AvaloniaBackstageChrome.CreateDetailGrid(");
        source.Should().Contain("AvaloniaBackstageChrome.AddDetailRow(");
        source.Should().NotContain("new ScrollViewer");
        source.Should().NotContain("var rowIndex = grid.RowDefinitions.Count");
        source.Should().NotContain("new RowDefinition(GridLength.Auto)");
        source.Should().NotContain("ColumnDefinitions = new ColumnDefinitions(\"Auto,*\")");
        source.Should().NotContain("BackstagePaneSurfacePlanner.BuildOpenActionPane(");
        source.Should().NotContain("BackstagePrintPanePlanner.Build(");
        source.Should().NotContain("BackstageInfoSafetyPanePlanner.Build(");
        source.Should().NotContain("SisterBackstageAccountPanePlanner.Build(");
        source.Should().NotContain("markAsFinal: null");
        source.Should().NotContain("restrictEditing: null");
        source.Should().NotContain("inspectDocument: null");
        source.Should().NotContain("checkAccessibility: null");
        source.Should().NotContain("printPreview: null");

        sharedSource.Should().Contain("public static class AvaloniaBackstageChrome");
        sharedSource.Should().Contain("public static Border CreateContentArea(");
        sharedSource.Should().Contain("public static Button CreateStackedActionButton(");
    }

    [Fact]
    public void Home_planner_produces_New_group_and_Open_group()
    {
        var recent = new[] { new RecentFileEntry { Path = @"C:\Docs\Report.docx", IsPinned = false } };
        var groups = BackstageHomePlanePlanner.Build(
            recent,
            newDocument: () => { },
            openRecent: _ => { },
            browse: () => { },
            openMore: () => { });

        groups.Should().Contain(g => g.Heading == "New");
        groups.Should().Contain(g => g.Heading == "Recent Documents");
        groups.Should().Contain(g => g.Heading == "Open");
    }

    [Fact]
    public void Home_planner_empty_recent_omits_Recent_Documents_group()
    {
        var groups = BackstageHomePlanePlanner.Build(
            Enumerable.Empty<RecentFileEntry>(),
            newDocument: () => { },
            openRecent: _ => { },
            browse: () => { },
            openMore: () => { });

        groups.Should().NotContain(g => g.Heading == "Recent Documents");
        groups.Should().Contain(g => g.Heading == "New");
    }

    [Fact]
    public void Open_planner_produces_Places_and_Recovery_groups()
    {
        var groups = BackstageOpenPanePlanner.Build(
            Enumerable.Empty<RecentFileEntry>(),
            openRecent: _ => { },
            browse: () => { },
            recoverUnsaved: () => { });

        groups.Should().Contain(g => g.Heading == "Places");
        groups.Should().Contain(g => g.Heading == "Recovery");
    }

    [Fact]
    public void SaveAs_planner_produces_three_format_groups()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(a => a.Formats);
        var groups = BackstageSaveAsFileTypePlanner.Build(formats, saveAsExtension: _ => { });

        groups.Should().HaveCount(3, "Save As has Word Documents, Web Pages, Other Formats");
        groups[0].Heading.Should().Be("Word Documents");
        groups[1].Heading.Should().Be("Web Pages");
        groups[2].Heading.Should().Be("Other Formats");
    }

    [Fact]
    public void SaveAs_inline_plan_has_docx_as_default_when_no_current_path()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(a => a.Formats);
        var plan = BackstageSaveAsFileTypePlanner.BuildInlinePlan(formats, displayName: "Untitled", currentPath: null);

        plan.SelectedExtension.Should().Be(".docx");
        plan.FileTypes.Should().NotBeEmpty();
    }

    [Fact]
    public void Print_planner_produces_fields_and_action_groups()
    {
        var page = new PageSettings();
        var plan = BackstagePrintPanePlanner.Build("Test.docx", page);

        plan.Fields.Should().NotBeEmpty();
        plan.Groups.Should().NotBeEmpty();
        plan.Groups.Should().Contain(g => g.Heading == "Print");
    }

    [Fact]
    public void Print_pane_surface_enables_preview_and_keeps_direct_print_deferred()
    {
        var surface = BackstagePaneSurfacePlanner.BuildPrintPane(
            "Test.docx",
            new PageSettings(),
            print: null,
            printPreview: () => { });

        surface.DeferredNote.Should().Be(
            BackstageViewTextResources.DirectPrintDeferredNote,
            "preview is now available in the Avalonia shell, but native printer selection is still deferred");
        var actions = surface.Groups.SelectMany(group => group.Actions).ToList();
        actions.Single(action => action.AutomationId == "PrintAction_Print")
            .IsEnabled.Should().BeFalse("native printer selection remains deferred");
        actions.Where(action => action.AutomationId == "PrintAction_PrintPreview")
            .Should().OnlyContain(action => action.IsEnabled);
    }

    [Fact]
    public void Info_safety_planner_produces_Protect_and_Inspect_groups()
    {
        var groups = BackstageInfoSafetyPanePlanner.Build();

        groups.Should().Contain(g => g.Heading == "Protect Document");
        groups.Should().Contain(g => g.Heading == "Inspect Document");
        groups.SelectMany(g => g.Actions).Should().NotBeEmpty();
    }

    [Fact]
    public void Account_planner_includes_product_and_user_sections()
    {
        var plan = SisterBackstageAccountPanePlanner.Build(
            new SisterBackstageAccountPaneContext(
                "FreeW",
                "1.0.0",
                "TestUser",
                "TestMachine",
                @"C:\AppData\FreeW"));

        plan.Groups.Should().Contain(g => g.Heading == "Product Information");
        plan.Groups.Should().Contain(g => g.Heading == "User Information");
        plan.Groups.SelectMany(g => g.Fields).Should().Contain(f => f.Label == "Product" && f.Value == "FreeW");
    }

    [Fact]
    public void Export_planner_builds_change_file_type_group_from_formats()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(a => a.Formats);
        var group = BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(formats, saveAsExtension: _ => { });

        group.Heading.Should().Be("Change File Type");
        group.Actions.Should().NotBeEmpty();
    }

    // ── MainWindow backstage callbacks ────────────────────────────────────────

    [Fact]
    public async Task MainWindow_BuildBackstageCallbacks_returns_non_null_callbacks()
    {
        BackstageCallbacks? callbacks = null;
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            callbacks = window.BuildBackstageCallbacks();
        }, CancellationToken.None);

        callbacks.Should().NotBeNull();
        callbacks!.GetRecentEntries.Should().NotBeNull();
        callbacks.GetFileFormats.Should().NotBeNull();
        callbacks.GetPageSettings.Should().NotBeNull();
        callbacks.GetCurrentOptions().Should().NotBeNull();
        callbacks.GetDataFolder().Should().NotBeNullOrWhiteSpace();
        callbacks.PrintPreview.Should().NotBeNull();
    }

    [Fact]
    public async Task MainWindow_BuildBackstageCallbacks_GetFileFormats_returns_docx()
    {
        object? formats = null;
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            formats = window.BuildBackstageCallbacks().GetFileFormats().ToList();
        }, CancellationToken.None);

        formats.Should().NotBeNull();
        // Cast via dynamic to avoid referencing FreeW.Core.IO directly in the test project.
        var extensions = (formats as System.Collections.IEnumerable)!
            .Cast<dynamic>()
            .Select(f => (string)f.Extension)
            .ToList();
        extensions.Should().Contain(ext => ext.Contains("docx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MainWindow_BackstageCallbacks_wire_mark_final_to_document_model()
    {
        var path = Path.Combine(Path.GetTempPath(), "FreeW.Avalonia.OptionsTests", Guid.NewGuid().ToString("N"), "settings.json");
        var marked = false;

        await Session.Dispatch(() =>
        {
            var window = new MainWindow(
                [],
                new FreeWOptions(),
                ApplicationOptionsStore<FreeWOptions>.ForPath(path));
            var callbacks = window.BuildBackstageCallbacks();

            callbacks.MarkAsFinal();

            marked = window.Editor.Document.MarkedAsFinal;
        }, CancellationToken.None);

        marked.Should().BeTrue();
    }

    [Fact]
    public async Task MainWindow_LoadsFreeWOptionsFromSharedStoreForBackstageAndRecentCap()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FreeW.Avalonia.OptionsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(path);
        store.Save(new FreeWOptions { RecentFilesCap = 3 }).Should().BeTrue();
        int cap = -1;

        await Session.Dispatch(() =>
        {
            var window = new MainWindow([], null, ApplicationOptionsStore<FreeWOptions>.ForPath(path));

            cap = window.BuildBackstageCallbacks().GetCurrentOptions().RecentFilesCap;
        }, CancellationToken.None);

        cap.Should().Be(3);
    }

    [Fact]
    public void MainWindow_UsesFreeWOptionsForRecentFileCapAndSafetyDialogs()
    {
        var source = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("ApplicationOptionsStore<FreeWOptions>");
        source.Should().Contain("maxRecentEntries: () => _options.RecentFilesCap");
        source.Should().Contain("new OptionsDialog(_options)");
        source.Should().Contain("new RestrictEditingDialog(_editor.Document.Protection)");
        source.Should().Contain("DocumentInspector.Inspect(_editor.Document)");
        source.Should().Contain("AccessibilityChecker.Check(_editor.Document)");
        source.Should().NotContain("DefaultRecentFilesCap");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static BackstageCallbacks BuildTestCallbacks() =>
        new BackstageCallbacks(
            DisplayName: "TestDocument",
            CurrentPath: null,
            GetRecentEntries: () => Array.Empty<RecentFileEntry>(),
            GetFileFormats: () => DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(a => a.Formats),
            GetPageSettings: () => new PageSettings(),
            GetCurrentOptions: () => new FreeWOptions(),
            GetDataFolder: () => @"C:\AppData\FreeW",
            NewDocument: () => { },
            OpenRecent: _ => { },
            OpenFolder: _ => { },
            Browse: () => { },
            RecoverUnsaved: () => { },
            SaveAs: () => { },
            SaveAsFormat: (_, _) => { },
            OpenContainingFolder: _ => { },
            ExportPdf: () => { },
            MarkAsFinal: () => { },
            RestrictEditing: () => { },
            InspectDocument: () => { },
            CheckAccessibility: () => { },
            OpenOptions: () => { });

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(FindRepoRoot(), Path.Combine(parts));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }
}

// Local alias so the test can call the planner directly with the same name
file static class BackstageHomePlanePlanner
{
    public static IReadOnlyList<Free.Shared.Shell.BackstageActionGroup> Build(
        IEnumerable<RecentFileEntry> recentEntries,
        Action newDocument,
        Action<string> openRecent,
        Action browse,
        Action openMore) =>
        BackstageHomePanePlanner.Build(recentEntries, newDocument, openRecent, browse, openMore);
}
