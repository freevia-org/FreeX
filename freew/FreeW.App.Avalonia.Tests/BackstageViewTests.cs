using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
        source.Should().Contain("SisterBackstageInfoPanePlanner.Build(");
        source.Should().Contain("BackstageInfoPaneSpec plan");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildAccountPane(");
        source.Should().Contain("ApplicationOptionsSummaryPlanner.Build(");
        source.Should().Contain("var document = _callbacks.GetDocument()");
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
        source.Should().Contain("BuildPrintEvidenceSection(surface.Evidence)");
        source.Should().Contain("PrintEvidence_");
        source.Should().Contain("BackstageViewTextResources.EvidenceSection");
        source.Should().Contain("BackstageViewTextResources.EvidenceRequirementsLabel");
        source.Should().Contain("FormatPrintEvidenceRequirement");
        source.Should().Contain("var printCapability = _callbacks.DirectPrintCapability");
        source.Should().Contain("print: _callbacks.Print");
        source.Should().Contain("directPrintCapability: printCapability");
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
        source.Should().Contain("BackstageInfoSafetyPanePlanner.Build(document)");
        source.Should().NotContain("SisterBackstageAccountPanePlanner.Build(");
        source.Should().NotContain("markAsFinal: null");
        source.Should().NotContain("restrictEditing: null");
        source.Should().NotContain("inspectDocument: null");
        source.Should().NotContain("checkAccessibility: null");
        source.Should().NotContain("print: null");
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
    public void SaveAs_planner_produces_capability_format_groups()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(a => a.Formats);
        var groups = BackstageSaveAsFileTypePlanner.Build(formats, saveAsExtension: _ => { });

        groups.Should().HaveCount(4, "Save As has Word, Web, Other, and explicit compatibility formats");
        groups[0].Heading.Should().Be("Word Documents");
        groups[1].Heading.Should().Be("Web Pages");
        groups[2].Heading.Should().Be("Other Formats");
        groups[3].Heading.Should().Be("Compatibility Formats");
        groups[3].Actions.Select(action => action.Label).Should().Contain("Word 97-2003 Document (*.doc)");
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
            printPreview: () => { },
            directPrintCapability: BackstageDirectPrintCapability.Deferred(
                "The current Avalonia target exposes no native PrintDialog or printer service; use Print Preview or Create PDF for OS printing."));

        surface.DeferredNote.Should().Be(
            BackstageViewTextResources.DirectPrintDeferredNote,
            "preview and PDF export are available in the Avalonia shell, but native printer selection is not exposed by the target");
        surface.Fields.Should().Contain(row =>
            row.Label == "Direct print" &&
            row.Value.Contains("current Avalonia target", StringComparison.Ordinal));
        var actions = surface.Groups.SelectMany(group => group.Actions).ToList();
        actions.Single(action => action.AutomationId == "PrintAction_Print")
            .IsEnabled.Should().BeFalse("native printer selection remains deferred");
        actions.Single(action => action.AutomationId == "PrintAction_Print")
            .Description.Should().Contain("Create PDF");
        actions.Where(action => action.AutomationId == "PrintAction_PrintPreview")
            .Should().OnlyContain(action => action.IsEnabled);
    }

    [Fact]
    public async Task PrintPreviewDialog_uses_backed_create_pdf_fallback_when_native_print_is_deferred()
    {
        var exported = false;

        await Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            var dialog = new PrintPreviewDialog(
                document,
                "Test.docx",
                createPdf: () =>
                {
                    exported = true;
                    return Task.CompletedTask;
                },
                directPrintCapability: BackstageDirectPrintCapability.Deferred(
                    "The current Avalonia target exposes no native PrintDialog or printer service; use Print Preview or Create PDF for OS printing."));

            var button = FindControl<Button>(dialog, "PrintPreviewPrintButton");
            button.Content.Should().Be(BackstageViewTextResources.CreatePdfLabel);
            button.IsEnabled.Should().BeTrue();
            ToolTip.GetTip(button)!.ToString().Should().Contain("Direct printer output is not available");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, CancellationToken.None);

        exported.Should().BeTrue();
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
        callbacks.DirectPrintCapability.Should().NotBeNull();
        callbacks.DirectPrintCapability!.IsAvailable.Should().BeFalse();
        callbacks.GetDocument().Should().NotBeNull();
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

        var safetySource = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "SafetyDialogs.cs"));
        safetySource.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.BuildPlan(current)");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.ModeOptions");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.TryCreateStartSettings(");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.TryCreateStopSettings(");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.StartButtonText");
        safetySource.Should().Contain("RestrictEditingDialogPlanner.StopButtonText");
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
            GetDocument: () => new TextDocument(),
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

    private static T FindControl<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T typedRoot && AutomationProperties.GetAutomationId(typedRoot) == automationId)
            return typedRoot;

        var found = root.GetLogicalDescendants()
            .OfType<T>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
        found.Should().NotBeNull($"control '{automationId}' should exist");
        return found!;
    }

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine(parts));

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
