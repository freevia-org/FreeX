using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.Interactions;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services.Ribbon;
using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class ContextMenuInteractionValidationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);
    private readonly ITestOutputHelper _output;

    public ContextMenuInteractionValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Inventory_CoversEveryAuthoritativeFamilyAndVariantExactly()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();
        var expectedFamilies = InteractionSurfaceCatalog.ContextMenus.Select(family => family.Id).ToHashSet(StringComparer.Ordinal);
        var actualFamilies = inventory.Select(row => row.FamilyId).ToHashSet(StringComparer.Ordinal);
        actualFamilies.Should().BeEquivalentTo(expectedFamilies);

        foreach (var family in InteractionSurfaceCatalog.ContextMenus)
        {
            var expectedVariants = family.Variants.Select(variant => variant.Id).ToHashSet(StringComparer.Ordinal);
            var actualVariants = inventory
                .Where(row => row.FamilyId == family.Id)
                .Select(row => row.VariantId)
                .ToHashSet(StringComparer.Ordinal);
            actualVariants.Should().BeEquivalentTo(expectedVariants, family.Id);
            actualVariants.Should().OnlyContain(variant =>
                inventory.Any(row => row.FamilyId == family.Id && row.VariantId == variant));
            _output.WriteLine($"{family.Id}: {inventory.Count(row => row.FamilyId == family.Id)} rows");
        }

        inventory.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        inventory.Should().OnlyContain(row => !string.IsNullOrWhiteSpace(row.ProductionRoute));

        var nativeRows = inventory.Count(row => row.FamilyId == "context-menu.native-application");
        var disabledRows = inventory.Count(row => !row.IsEnabled);
        var managedDispatches = inventory
            .Where(row => row.IsEnabled && row.FamilyId != "context-menu.native-application")
            .Select(row => $"{row.FamilyId}|{row.VariantId}|{row.ActionKey}")
            .Distinct(StringComparer.Ordinal)
            .Count();
        _output.WriteLine($"total inventory rows: {inventory.Count}");
        _output.WriteLine($"managed production dispatch probes: {managedDispatches}");
        _output.WriteLine($"explicit-disabled rows: {disabledRows}");
        _output.WriteLine($"native-boundary skipped rows: {nativeRows}");
    }

    [Fact]
    public async Task BoundedValidation_EmitsHonestFamilyAndVariantAggregates()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            using var temporaryDirectory = new TestTemporaryDirectory("freex-context-validation-");
            var outputDirectory = temporaryDirectory.Path;
            window.Show();
            try
            {
                var results = await window.RunInteractionValidationAsync(
                    outputDirectory,
                    dialogStart: 0,
                    dialogCount: 0,
                    includeCoreResults: true,
                    ribbonCommandStart: 0,
                    ribbonCommandCount: 0,
                    ribbonOnly: false,
                    coreSection: "context-menus",
                    contextMenuDispatchStart: 0,
                    contextMenuDispatchCount: 1);

                var familyResults = results.Where(result => result.Category == "context-menu-family").ToArray();
                var variantResults = results.Where(result => result.Category == "context-menu-variant").ToArray();
                familyResults.Should().HaveCount(InteractionSurfaceCatalog.ContextMenus.Count);
                variantResults.Should().HaveCount(
                    InteractionSurfaceCatalog.ContextMenus.Sum(family => family.Variants.Count));
                familyResults.Concat(variantResults).Should().OnlyContain(result =>
                    result.Evidence.Contains("coverage=", StringComparison.Ordinal) &&
                    result.Evidence.Contains("batch-status=", StringComparison.Ordinal));
                familyResults.Concat(variantResults).Should().NotContain(result =>
                    result.Status == "failed");
                familyResults.Concat(variantResults).Should().Contain(result =>
                    result.Status == "skipped" &&
                    result.EvidenceLevel == "bounded-batch-aggregate");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Inventory_CoversEveryPlannerActionIncludingWorksheetShowNotes()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();

        AssertActionSet<WorksheetContextMenuAction>(inventory, "context-menu.worksheet", WorksheetContextMenuAction.None);
        inventory.Should().Contain(row =>
            row.FamilyId == "context-menu.worksheet" &&
            row.ActionKey == nameof(WorksheetContextMenuAction.ShowNotes));
        AssertActionSet<SheetTabContextMenuAction>(inventory, "context-menu.sheet-tabs", SheetTabContextMenuAction.None);
        AssertActionSet<PivotFieldContextMenuAction>(inventory, "context-menu.pivot-field", PivotFieldContextMenuAction.None);
        AssertActionSet<PivotChartFieldContextMenuAction>(
            inventory,
            "context-menu.pivot-chart",
            PivotChartFieldContextMenuAction.None,
            PivotChartFieldContextMenuAction.Summary);
        AssertActionSet<PivotHeaderMenuAction>(inventory, "context-menu.pivot-header", PivotHeaderMenuAction.Separator);

        inventory.Where(row => row.FamilyId == "context-menu.recent-files")
            .Select(row => row.ActionKey)
            .Distinct()
            .Should().BeEquivalentTo(Enum.GetNames<BackstageRecentFileMenuAction>());
        inventory.Where(row => row.FamilyId == "context-menu.quick-access-toolbar")
            .Select(row => row.ActionKey.Split(':')[0])
            .Distinct()
            .Should().BeEquivalentTo(
                nameof(QuickAccessToolbarMenuAction.Add),
                nameof(QuickAccessToolbarMenuAction.Remove),
                nameof(QuickAccessToolbarMenuAction.ExecuteHistory));
    }

    [Fact]
    public void Inventory_CoversEveryAutoFilterCriterionAndNativeMenuEntry()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();
        var expectedCriteria = Enum.GetValues<AutoFilterMenuFilterKind>()
            .Sum(kind => AutoFilterMenuCatalog.GetCriteriaDescriptors(kind).Count);
        inventory.Count(row => row.FamilyId == "context-menu.auto-filter-criteria")
            .Should().Be(expectedCriteria);

        var expectedNative = NativeMenuCatalog.FileMenuEntries.Count(entry => entry.Kind == NativeMenuEntryKind.Item) +
            NativeMenuCatalog.TopLevelMenus
                .Where(menu => menu.Id != NativeMenuTopLevelId.File)
                .Sum(menu => NativeMenuCatalog.GetMenuEntries(menu.Id).Count(entry => entry.Kind == NativeMenuEntryKind.Item));
        var nativeRows = inventory.Where(row => row.FamilyId == "context-menu.native-application").ToArray();
        nativeRows.Should().HaveCount(expectedNative);
        nativeRows.Select(row => row.ActionKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void OwnedNativeFileRoutes_UseTheOwnedDialogWaitContract()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();
        var nativeRows = inventory.Where(row => row.FamilyId == "context-menu.native-application").ToArray();
        var ownedIds = new HashSet<NativeFileMenuItemId>
        {
            NativeFileMenuItemId.BackstageInfo,
            NativeFileMenuItemId.BackstageExport,
            NativeFileMenuItemId.BackstageAccount,
            NativeFileMenuItemId.Options,
            NativeFileMenuItemId.WorkbookStatistics,
            NativeFileMenuItemId.PageSetup,
            NativeFileMenuItemId.PrintPreview,
        };

        var ownedRows = nativeRows.Where(row =>
            row.ActionKey.StartsWith("file:", StringComparison.Ordinal) &&
            Enum.TryParse<NativeFileMenuItemId>(row.ActionKey["file:".Length..], out var id) &&
            ownedIds.Contains(id)).ToArray();

        ownedRows.Should().HaveCount(ownedIds.Count);
        ownedRows.Should().OnlyContain(row => MainWindow.MayOpenOwnedContextDialog(row));
        nativeRows.Except(ownedRows).Should().OnlyContain(row => !MainWindow.MayOpenOwnedContextDialog(row));
    }

    [Fact]
    public async Task DirectStructuralWorksheetRoutes_UseMutationEvidenceInsteadOfDialogWaits()
    {
        var directActions = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(WorksheetContextMenuAction.InsertRowAbove),
            nameof(WorksheetContextMenuAction.InsertRowBelow),
            nameof(WorksheetContextMenuAction.InsertColumnLeft),
            nameof(WorksheetContextMenuAction.InsertColumnRight),
            nameof(WorksheetContextMenuAction.DeleteRows),
            nameof(WorksheetContextMenuAction.DeleteColumns),
        };
        var rows = MainWindow.BuildContextMenuValidationInventory()
            .Where(row =>
                row.FamilyId == "context-menu.worksheet" &&
                row.VariantId == "context-menu.worksheet.target.worksheet" &&
                row.IsEnabled &&
                directActions.Contains(row.ActionKey))
            .GroupBy(row => row.ActionKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        rows.Should().HaveCount(directActions.Count);
        rows.Should().OnlyContain(row => !MainWindow.MayOpenOwnedContextDialog(row));

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var results = new List<InteractionValidationResult>();
                foreach (var row in rows)
                    results.Add(await window.RunContextMenuInteractionValidationForTestAsync(row.Id));

                results.Should().OnlyContain(result => result.Status == "passed");
                results.Should().OnlyContain(result =>
                    result.EvidenceLevel == "production-mutation-undone");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Inventory_HasNoPlannerOnlyActionableRoute()
    {
        var inventory = MainWindow.BuildContextMenuValidationInventory();
        inventory.Where(row => row.IsEnabled).Should().OnlyContain(row =>
            !row.ProductionRoute.Contains("planner", StringComparison.OrdinalIgnoreCase) &&
            !row.ProductionRoute.Contains("catalog", StringComparison.OrdinalIgnoreCase));

        var source = File.ReadAllText(RepoFile(
            "tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ContextMenuInteractionValidation.cs"));
        source.Should().NotContain("planned-enabled");
        source.Should().NotContain("planned-disabled");
        source.Should().NotContain("neutral-planner-backed");
    }

    [Fact]
    public async Task ProductionDispatch_ObservesQuickAnalysisAndDrawingFixtures()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var inventory = MainWindow.BuildContextMenuValidationInventory();
                var ids = inventory
                    .Where(row =>
                        row.ActionKey == nameof(WorksheetContextMenuAction.QuickAnalysis) ||
                        row.VariantId == "context-menu.worksheet.target.text-box" ||
                        row.FamilyId == "context-menu.quick-access-toolbar" &&
                            row.ActionKey == nameof(QuickAccessToolbarMenuAction.Remove) ||
                        row.VariantId == "variant.total-point" ||
                        row.FamilyId == "context-menu.auto-filter-criteria" &&
                            row.ActionKey.EndsWith(":=", StringComparison.Ordinal))
                    .GroupBy(row => row.ActionKey, StringComparer.Ordinal)
                    .Select(group => group.First().Id)
                    .ToArray();

                var results = new List<InteractionValidationResult>();
                foreach (var id in ids)
                    results.Add(await window.RunContextMenuInteractionValidationForTestAsync(id));

                results.Should().HaveCount(13);
                results.Should().OnlyContain(result => result.Status == "passed", because:
                    string.Join(Environment.NewLine, results.Select(result =>
                        $"{result.Id}: {result.EvidenceLevel} | {result.Note}")));

                var chartSize = inventory.Single(row =>
                    row.VariantId == "context-menu.worksheet.target.chart" &&
                    row.ActionKey == nameof(WorksheetContextMenuAction.ChartSizeAndProperties));
                var chartSizeResult = await window.RunContextMenuInteractionValidationForTestAsync(chartSize.Id);
                chartSizeResult.Status.Should().Be("passed");
                chartSizeResult.EvidenceLevel.Should().Be("production-dialog-opened-cancelled");
                chartSizeResult.Evidence.Should().Contain("DispatchDrawingObjectContextMenuCommand");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionDispatch_ExercisesShowNotesAndEveryAutoFilterCriterion()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var inventory = MainWindow.BuildContextMenuValidationInventory();
                var rows = inventory
                    .Where(row =>
                        row.ActionKey == nameof(WorksheetContextMenuAction.ShowNotes) ||
                        row.FamilyId == "context-menu.auto-filter-criteria")
                    .ToArray();

                rows.Should().HaveCount(33);
                rows.Should().OnlyContain(row => !MainWindow.MayOpenOwnedContextDialog(row));

                var results = new List<InteractionValidationResult>();
                foreach (var row in rows)
                    results.Add(await window.RunContextMenuInteractionValidationForTestAsync(row.Id));

                results.Should().OnlyContain(result => result.Status == "passed", because:
                    string.Join(Environment.NewLine, results.Select(result =>
                        $"{result.Id}: {result.EvidenceLevel} | {result.Note}")));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionDispatch_ObservesWorksheetInlineEditorsAndPictureCropMode()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var inventory = MainWindow.BuildContextMenuValidationInventory();
                var rows = inventory
                    .Where(row => row.IsEnabled &&
                        row.FamilyId == "context-menu.worksheet" &&
                        ((row.VariantId == "context-menu.worksheet.target.worksheet" &&
                            (row.ActionKey is nameof(WorksheetContextMenuAction.NewComment) or
                                nameof(WorksheetContextMenuAction.EditComment) or
                                nameof(WorksheetContextMenuAction.NewNote) or
                                nameof(WorksheetContextMenuAction.EditNote))) ||
                         (row.VariantId.Contains(".picture", StringComparison.Ordinal) &&
                            row.ActionKey == nameof(WorksheetContextMenuAction.CropPicture))))
                    .GroupBy(row => row.ActionKey, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(row => row.ActionKey, StringComparer.Ordinal)
                    .ToArray();

                rows.Should().HaveCount(5);
                rows.Should().OnlyContain(row => !MainWindow.MayOpenOwnedContextDialog(row));

                var results = new List<InteractionValidationResult>();
                foreach (var row in rows)
                    results.Add(await window.RunContextMenuInteractionValidationForTestAsync(row.Id));

                results.Should().OnlyContain(result => result.Status == "passed", because:
                    string.Join(Environment.NewLine, results.Select(result =>
                        $"{result.Id}: {result.EvidenceLevel} | {result.Note}")));
                results.Where(result => result.Id.EndsWith(":NewComment", StringComparison.Ordinal) ||
                        result.Id.EndsWith(":EditComment", StringComparison.Ordinal) ||
                        result.Id.EndsWith(":NewNote", StringComparison.Ordinal) ||
                        result.Id.EndsWith(":EditNote", StringComparison.Ordinal))
                    .Should().OnlyContain(result => result.EvidenceLevel == "production-inline-editor-opened-cancelled");
                results.Single(result => result.Id.EndsWith(":CropPicture", StringComparison.Ordinal))
                    .EvidenceLevel.Should().Be("production-mode-entered-exited");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionDispatch_ExercisesOwnedFileMenuRoutes()
    {
        string[] ids =
        [
            "context-menu.native-application.file:BackstageInfo",
            "context-menu.native-application.file:BackstageExport",
            "context-menu.native-application.file:BackstageAccount",
            "context-menu.native-application.file:Options",
            "context-menu.native-application.file:WorkbookStatistics",
            "context-menu.native-application.file:PageSetup",
            "context-menu.native-application.file:PrintPreview",
        ];

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var results = new List<InteractionValidationResult>();
                foreach (var id in ids)
                    results.Add(await window.RunContextMenuInteractionValidationForTestAsync(id));

                results.Should().OnlyContain(result => result.Status == "passed", because:
                    string.Join(Environment.NewLine, results.Select(result =>
                        $"{result.Id}: {result.EvidenceLevel} | {result.Note}")));
                results.Should().OnlyContain(result =>
                    result.EvidenceLevel == "production-dialog-opened-cancelled" ||
                    result.EvidenceLevel == "production-dispatch-completed");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionDispatch_OpensAndCancelsPivotHeaderFieldSettings()
    {
        const string id = "context-menu.pivot-header.area.row:FieldSettings";

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var result = await window.RunContextMenuInteractionValidationForTestAsync(id);

                result.Status.Should().Be("passed", $"{result.EvidenceLevel}: {result.Note}");
                result.EvidenceLevel.Should().Be("production-dialog-opened-cancelled");
                result.Evidence.Should().Contain("InvokePivotHeaderAction");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionDispatch_ClosesAllEightExhaustiveContextMenuResiduals()
    {
        string[] ids =
        [
            "variant.available-fields:ValueFieldSettings",
            "variant.filters-bucket:ValueFieldSettings",
            "variant.columns-bucket:ValueFieldSettings",
            "variant.rows-bucket:ValueFieldSettings",
            "variant.filter-state:ValueFieldSettings",
            "variant.no-filter-state:ValueFieldSettings",
            "variant.customization:Add",
            "variant.customization:Remove",
        ];

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var results = new List<InteractionValidationResult>();
                foreach (var id in ids)
                    results.Add(await window.RunContextMenuInteractionValidationForTestAsync(id));

                results.Select(result => result.Id).Should().Equal(ids);
                results.Should().OnlyContain(result => result.Status == "passed", because:
                    string.Join(Environment.NewLine, results.Select(result =>
                        $"{result.Id}: {result.EvidenceLevel} | {result.Note}")));
                results.Take(6).Should().OnlyContain(result =>
                    result.EvidenceLevel == "production-dialog-opened-cancelled");
                results.Skip(6).Should().OnlyContain(result =>
                    result.EvidenceLevel == "production-options-effect-isolated");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionDispatch_SmallPivotBatch_KeepsVisualTreeBounded()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var rows = MainWindow.BuildContextMenuValidationInventory()
                    .Where(row =>
                        row.IsEnabled &&
                        row.FamilyId == "context-menu.pivot-field" &&
                        row.ActionKey is nameof(PivotFieldContextMenuAction.SortAscending) or
                            nameof(PivotFieldContextMenuAction.SortDescending) or
                            nameof(PivotFieldContextMenuAction.ClearFilter))
                    .GroupBy(row => row.ActionKey, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray();

                rows.Should().HaveCount(3);
                var gridBuildsBefore = window.SheetGridBuildCountForTest;
                var paneBuildsBefore = window.PivotFieldPaneBuildCountForTest;
                var visualCounts = new List<int>();
                long managedBytesAfterWarmup = 0;
                for (var index = 0; index < rows.Length; index++)
                {
                    var row = rows[index];
                    var result = await window.RunContextMenuInteractionValidationForTestAsync(row.Id);
                    result.Status.Should().Be("passed", $"{row.Id}: {result.EvidenceLevel} | {result.Note}");

                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    visualCounts.Add(window.GetVisualDescendants().Count());
                    if (index == 0)
                        managedBytesAfterWarmup = GC.GetTotalMemory(forceFullCollection: true);
                }

                var finalManagedBytes = GC.GetTotalMemory(forceFullCollection: true);

                visualCounts.Should().OnlyContain(count => count < 10_000);
                (visualCounts.Max() - visualCounts.Min()).Should().BeLessThan(500,
                    "replacing the pivot grid/pane must not retain prior attached visual trees");
                (window.SheetGridBuildCountForTest - gridBuildsBefore).Should().BeLessThan(24,
                    "three production pivot routes and their validation undo must not start a layout/refresh feedback loop");
                (window.PivotFieldPaneBuildCountForTest - paneBuildsBefore).Should().BeLessThan(12,
                    "an attached pivot search box must not rebuild its replacement pane on unchanged TextChanged notifications");
                (finalManagedBytes - managedBytesAfterWarmup).Should().BeLessThan(64L * 1024 * 1024,
                    "the production pivot batch must not retain replacement pane/context-menu trees");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void AssertActionSet<TAction>(
        IReadOnlyList<ContextMenuValidationDescriptor> inventory,
        string familyId,
        params TAction[] excluded)
        where TAction : struct, Enum
    {
        var expected = Enum.GetNames<TAction>()
            .Except(excluded.Select(value => value.ToString()), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var actual = inventory.Where(row => row.FamilyId == familyId)
            .Select(row => row.ActionKey)
            .ToHashSet(StringComparer.Ordinal);
        actual.Should().BeEquivalentTo(expected);
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
