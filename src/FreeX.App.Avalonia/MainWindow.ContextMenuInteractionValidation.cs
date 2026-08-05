using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using FreeX.App.Avalonia.Pivot;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.Interactions;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

internal sealed record ContextMenuValidationDescriptor(
    string Id,
    string FamilyId,
    string VariantId,
    string ActionKey,
    string Label,
    bool IsEnabled,
    string ProductionRoute);

internal sealed record ContextMenuDispatchEvidence(
    string Status,
    string EvidenceLevel,
    string Evidence,
    string Note);

public sealed partial class MainWindow
{
    private const string WorksheetContextFamily = "context-menu.worksheet";
    private const string SheetTabContextFamily = "context-menu.sheet-tabs";
    private const string StatusBarContextFamily = "context-menu.status-bar";
    private const string PivotFieldContextFamily = "context-menu.pivot-field";
    private const string PivotHeaderContextFamily = "context-menu.pivot-header";
    private const string PivotChartContextFamily = "context-menu.pivot-chart";
    private const string RecentFilesContextFamily = "context-menu.recent-files";
    private const string QuickAccessContextFamily = "context-menu.quick-access-toolbar";
    private const string WaterfallContextFamily = "context-menu.waterfall-point";
    private const string AutoFilterContextFamily = "context-menu.auto-filter-criteria";
    private const string NativeMenuContextFamily = "context-menu.native-application";
    internal static int InteractiveValidationContextMenuDispatchCount => BuildContextMenuValidationInventory()
        .Select(ContextMenuExecutionKey)
        .Distinct(StringComparer.Ordinal)
        .Count();

    internal static IReadOnlyList<string> InteractiveValidationContextMenuDispatchIds =>
        BuildContextMenuValidationInventory()
            .Select(ContextMenuExecutionKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyList<string> InteractiveValidationContextMenuFamilyIds =>
        InteractionSurfaceCatalog.ContextMenus
            .Select(family => family.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyList<string> InteractiveValidationContextMenuVariantIds =>
        InteractionSurfaceCatalog.ContextMenus
            .SelectMany(family => family.Variants.Select(variant => variant.Id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyList<ContextMenuValidationDescriptor> BuildContextMenuValidationInventory()
    {
        var rows = new List<ContextMenuValidationDescriptor>();
        AddWorksheetContextInventory(rows);
        AddSheetTabContextInventory(rows);
        AddStatusBarContextInventory(rows);
        AddPivotFieldContextInventory(rows);
        AddPivotHeaderContextInventory(rows);
        AddPivotChartContextInventory(rows);
        AddRecentFilesContextInventory(rows);
        AddQuickAccessContextInventory(rows);
        AddWaterfallContextInventory(rows);
        AddAutoFilterContextInventory(rows);
        AddNativeMenuContextInventory(rows);
        return rows;
    }

    internal async Task<InteractionValidationResult> RunContextMenuInteractionValidationForTestAsync(string id)
    {
        var row = BuildContextMenuValidationInventory().Single(candidate => candidate.Id == id);
        var evidence = row.IsEnabled
            ? await ExerciseContextMenuProductionRouteAsync(row)
            : new ContextMenuDispatchEvidence(
                "passed",
                "explicitly-disabled",
                row.ProductionRoute,
                "The production menu rendered this command disabled; disabled commands are not invoked.");
        return new InteractionValidationResult(
            row.Id,
            "context-menu-command",
            evidence.Status,
            evidence.EvidenceLevel,
            $"{row.Label} | {evidence.Evidence}",
            evidence.Note);
    }

    private async Task AddContextMenuInteractionResultsAsync(
        List<InteractionValidationResult> results,
        int dispatchStart = 0,
        int dispatchCount = int.MaxValue)
    {
        var inventory = BuildContextMenuValidationInventory();
        var allExecutionKeys = inventory
            .Select(ContextMenuExecutionKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var selectedExecutionKeys = allExecutionKeys
            .Skip(Math.Max(0, dispatchStart))
            .Take(Math.Max(0, dispatchCount))
            .ToHashSet(StringComparer.Ordinal);
        var selectedInventory = inventory
            .Where(row => selectedExecutionKeys.Contains(ContextMenuExecutionKey(row)))
            .ToArray();
        var observed = new Dictionary<string, ContextMenuDispatchEvidence>(StringComparer.Ordinal);

        foreach (var row in selectedInventory)
        {
            var executionKey = ContextMenuExecutionKey(row);
            if (!observed.TryGetValue(executionKey, out var evidence))
            {
                evidence = row.IsEnabled
                    ? await ExerciseContextMenuProductionRouteAsync(row)
                    : new ContextMenuDispatchEvidence(
                        "passed",
                        "explicitly-disabled",
                        row.ProductionRoute,
                        "The production menu rendered this command disabled; disabled commands are not invoked.");
                observed.Add(executionKey, evidence);
            }

            results.Add(new InteractionValidationResult(
                row.Id,
                "context-menu-command",
                evidence.Status,
                evidence.EvidenceLevel,
                $"{row.Label} | {evidence.Evidence}",
                evidence.Note));
        }

        foreach (var family in InteractionSurfaceCatalog.ContextMenus)
        {
            var familyRows = inventory.Where(row => row.FamilyId == family.Id).ToArray();
            AddContextMenuAggregateResult(
                results,
                family.Id,
                "context-menu-family",
                $"{familyRows.Length} command/variant rows",
                familyRows,
                selectedExecutionKeys,
                observed);

            foreach (var variant in family.Variants)
            {
                var variantRows = familyRows.Where(row => row.VariantId == variant.Id).ToArray();
                AddContextMenuAggregateResult(
                    results,
                    variant.Id,
                    "context-menu-variant",
                    $"{variantRows.Length} command rows",
                    variantRows,
                    selectedExecutionKeys,
                    observed,
                    family.Source.CatalogOrPlanner);
            }
        }
    }

    private static void AddContextMenuAggregateResult(
        List<InteractionValidationResult> results,
        string id,
        string category,
        string rowDescription,
        IReadOnlyList<ContextMenuValidationDescriptor> rows,
        IReadOnlySet<string> selectedExecutionKeys,
        IReadOnlyDictionary<string, ContextMenuDispatchEvidence> observed,
        string? completeNote = null)
    {
        var executionKeys = rows
            .Select(ContextMenuExecutionKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var selectedKeys = executionKeys
            .Where(selectedExecutionKeys.Contains)
            .ToHashSet(StringComparer.Ordinal);
        var selectedRows = rows
            .Where(row => selectedKeys.Contains(ContextMenuExecutionKey(row)))
            .ToArray();
        var batchStatus = AggregateContextMenuStatus(selectedRows, observed);
        var observedStatuses = selectedKeys
            .Where(observed.ContainsKey)
            .Select(key => observed[key].Status)
            .ToArray();
        var status = selectedKeys.Count == 0
            ? "skipped"
            : batchStatus == "failed"
            ? "failed"
            : selectedKeys.Count == executionKeys.Length
                ? batchStatus
                : "skipped";
        var evidenceLevel = selectedKeys.Count == executionKeys.Length
            ? "executable-command-inventory"
            : "bounded-batch-aggregate";
        var note = selectedKeys.Count == executionKeys.Length
            ? completeNote ?? "Every execution key in this aggregate was dispatched through its Avalonia production route."
            : "This bounded batch covers only part of the aggregate; the runner must merge every batch before crediting it.";

        results.Add(new InteractionValidationResult(
            id,
            category,
            status,
            evidenceLevel,
            $"{rowDescription}; coverage={selectedKeys.Count}/{executionKeys.Length}; " +
            $"batch-status={batchStatus}; observed-passed={observedStatuses.Count(value => value == "passed")}; " +
            $"observed-failed={observedStatuses.Count(value => value == "failed")}; " +
            $"observed-skipped={observedStatuses.Count(value => value == "skipped")}",
            note));
    }

    private static string ContextMenuExecutionKey(ContextMenuValidationDescriptor row) =>
        $"{row.FamilyId}|{row.VariantId}|{row.ActionKey}|{row.IsEnabled}";

    private static string AggregateContextMenuStatus(
        IReadOnlyList<ContextMenuValidationDescriptor> rows,
        IReadOnlyDictionary<string, ContextMenuDispatchEvidence> observed)
    {
        if (rows.Count == 0)
            return "failed";
        var statuses = rows.Where(row => row.IsEnabled)
            .Select(row => observed[ContextMenuExecutionKey(row)].Status)
            .ToArray();
        if (statuses.Contains("failed", StringComparer.Ordinal))
            return "failed";
        return statuses.Contains("passed", StringComparer.Ordinal) ? "passed" : "skipped";
    }

    private async Task<ContextMenuDispatchEvidence> ExerciseContextMenuProductionRouteAsync(
        ContextMenuValidationDescriptor row)
    {
        try
        {
            return row.FamilyId switch
            {
                WorksheetContextFamily => await ExerciseWorksheetContextRouteAsync(row),
                SheetTabContextFamily => await ExerciseSheetTabContextRouteAsync(row),
                StatusBarContextFamily => ExerciseStatusBarContextRoute(row),
                PivotFieldContextFamily => await ExercisePivotFieldContextRouteAsync(row),
                PivotHeaderContextFamily => await ExercisePivotHeaderContextRouteAsync(row),
                PivotChartContextFamily => await ExercisePivotChartContextRouteAsync(row),
                RecentFilesContextFamily => ExerciseRecentFilesContextRoute(row),
                QuickAccessContextFamily => ExerciseQuickAccessContextRoute(row),
                WaterfallContextFamily => ExerciseWaterfallContextRoute(row),
                AutoFilterContextFamily => ExerciseAutoFilterContextRoute(row),
                NativeMenuContextFamily => await ExerciseNativeMenuContextRouteAsync(row),
                _ => Failed(row, "No production dispatch probe is registered for this context-menu family."),
            };
        }
        catch (Exception ex)
        {
            return Failed(row, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<ContextMenuDispatchEvidence> ExerciseWorksheetContextRouteAsync(
        ContextMenuValidationDescriptor row)
    {
        if (!Enum.TryParse<WorksheetContextMenuAction>(row.ActionKey, out var action))
            return Failed(row, "Worksheet action could not be parsed.");

        PrepareWorksheetContextFixture(row.VariantId, action);
        var route = row.VariantId.Contains(".picture", StringComparison.Ordinal) ||
            row.VariantId.Contains(".shape", StringComparison.Ordinal) ||
            row.VariantId.Contains(".text-box", StringComparison.Ordinal) ||
            row.VariantId.Contains(".chart", StringComparison.Ordinal)
                ? (Action)(() => DispatchDrawingObjectContextMenuCommand(new Free.Shared.Ribbon.RibbonCommandId(action.ToString())))
                : () => DispatchWorksheetContextMenuCommand(new Free.Shared.Ribbon.RibbonCommandId(action.ToString()));
        return await InvokeProductionContextRouteAsync(row, route);
    }

    private async Task<ContextMenuDispatchEvidence> ExerciseSheetTabContextRouteAsync(
        ContextMenuValidationDescriptor row)
    {
        if (!Enum.TryParse<SheetTabContextMenuAction>(row.ActionKey, out var action))
            return Failed(row, "Sheet-tab action could not be parsed.");

        if (_session.SheetTabs.Count < 2)
        {
            var addSheet = _session.AddSheet();
            if (!addSheet.Success)
                return Failed(row, addSheet.ErrorMessage ?? "Could not create the sheet-tab validation fixture.");
        }

        Action dispatch = action switch
        {
            SheetTabContextMenuAction.InsertSheet => AddNewSheet,
            SheetTabContextMenuAction.DeleteSheet => DeleteActiveSheet,
            SheetTabContextMenuAction.Rename => () => _ = RenameActiveSheetAsync(),
            SheetTabContextMenuAction.MoveOrCopy => ShowMoveOrCopySheetDialog,
            SheetTabContextMenuAction.ProtectSheet => () => _ = ShowProtectSheetDialogAsync(),
            SheetTabContextMenuAction.TabColor => () => _session.SetActiveSheetTabColor(null),
            SheetTabContextMenuAction.Hide => HideActiveSheet,
            SheetTabContextMenuAction.Unhide => () => _ = UnhideSheetAsync(),
            SheetTabContextMenuAction.SelectAllSheets => SelectAllVisibleSheets,
            SheetTabContextMenuAction.UngroupSheets => UngroupSheets,
            _ => () => { },
        };
        return await InvokeProductionContextRouteAsync(row, dispatch);
    }

    private ContextMenuDispatchEvidence ExerciseStatusBarContextRoute(ContextMenuValidationDescriptor row)
    {
        var before = GetStatusBarOption(row.ActionKey);
        OnStatusBarCustomizeToggled(row.ActionKey, !before);
        var changed = GetStatusBarOption(row.ActionKey) != before;
        OnStatusBarCustomizeToggled(row.ActionKey, before);
        return changed
            ? Passed(row, "production-toggle-effect-restored", "OnStatusBarCustomizeToggled", "Visibility changed and was restored.")
            : Failed(row, "Status-bar visibility did not change.");
    }

    private async Task<ContextMenuDispatchEvidence> ExercisePivotFieldContextRouteAsync(
        ContextMenuValidationDescriptor row)
    {
        if (!Enum.TryParse<PivotFieldContextMenuAction>(row.ActionKey, out var action))
            return Failed(row, "Pivot-field action could not be parsed.");
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return Failed(row, "Pivot fixture could not be created.");
        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var field = PreparePivotFieldContextFixture(pivot, headers, row.VariantId);
        if (field is null)
            return Failed(row, $"Pivot fixture did not expose the {row.VariantId} field bucket.");
        return await InvokeProductionContextRouteAsync(
            row,
            () => DispatchPivotFieldContextMenuAction(pivot, headers, field, action));
    }

    private static PivotFieldListItemModel? PreparePivotFieldContextFixture(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        string variantId)
    {
        EnsurePivotAxisBucketHasField(pivot, headers, PivotFieldBucket.Filters);
        EnsurePivotAxisBucketHasField(pivot, headers, PivotFieldBucket.Columns);

        var bucket = variantId switch
        {
            "variant.available-fields" => PivotFieldBucket.Available,
            "variant.filters-bucket" => PivotFieldBucket.Filters,
            "variant.columns-bucket" => PivotFieldBucket.Columns,
            "variant.rows-bucket" => PivotFieldBucket.Rows,
            "variant.values-bucket" => PivotFieldBucket.Values,
            _ => (PivotFieldBucket?)null,
        };
        return bucket is { } targetBucket
            ? PivotFieldListPaneBuilder.Build(pivot, headers).Bucket(targetBucket).Fields.FirstOrDefault()
            : null;
    }

    private static void EnsurePivotAxisBucketHasField(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldBucket bucket)
    {
        var target = bucket switch
        {
            PivotFieldBucket.Filters => pivot.PageFields,
            PivotFieldBucket.Columns => pivot.ColumnFields,
            _ => throw new ArgumentOutOfRangeException(nameof(bucket)),
        };
        if (target.Count > 0)
            return;

        var available = PivotFieldListPaneBuilder.Build(pivot, headers).Available.Fields.FirstOrDefault();
        if (available is not null)
            target.Add(new PivotFieldModel(available.SourceFieldIndex));
    }

    private async Task<ContextMenuDispatchEvidence> ExercisePivotHeaderContextRouteAsync(
        ContextMenuValidationDescriptor row)
    {
        if (!Enum.TryParse<PivotHeaderMenuAction>(row.ActionKey, out var action) ||
            !Enum.TryParse<PivotHeaderArea>(row.VariantId.Split('.').Last(), true, out var area))
            return Failed(row, "Pivot-header route could not be parsed.");
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return Failed(row, "Pivot fixture could not be created.");
        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var sourceIndex = area == PivotHeaderArea.Value && pivot.DataFields.Count > 0
            ? pivot.DataFields[0].SourceFieldIndex
            : pivot.RowFields.FirstOrDefault()?.SourceFieldIndex ?? 0;
        var target = new PivotHeaderDropdownTargetModel(
            pivot.Name,
            headers[sourceIndex],
            sourceIndex,
            area,
            IsActive: false,
            area == PivotHeaderArea.Value ? 0 : null);
        return await InvokeProductionContextRouteAsync(
            row,
            () => InvokePivotHeaderAction(pivot, headers, target, action, BuildPivotDragValidator(pivot)));
    }

    private async Task<ContextMenuDispatchEvidence> ExercisePivotChartContextRouteAsync(
        ContextMenuValidationDescriptor row)
    {
        if (!Enum.TryParse<PivotChartFieldContextMenuAction>(row.ActionKey, out var action))
            return Failed(row, "PivotChart action could not be parsed.");
        var pivot = EnsureParityPivot();
        if (pivot is null)
            return Failed(row, "Pivot fixture could not be created.");
        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var sourceIndex = pivot.RowFields.FirstOrDefault()?.SourceFieldIndex ?? 0;
        PreparePivotChartContextFixture(pivot, sourceIndex, row.VariantId);
        var target = new PivotHeaderDropdownTargetModel(
            pivot.Name, headers[sourceIndex], sourceIndex, PivotHeaderArea.Row, IsActive: false);
        return await InvokeProductionContextRouteAsync(
            row,
            () => DispatchPivotChartFieldContextMenuAction(pivot, headers, target, action));
    }

    private static void PreparePivotChartContextFixture(
        PivotTableModel pivot,
        int sourceIndex,
        string variantId)
    {
        var rowIndex = pivot.RowFields.FindIndex(field => field.SourceFieldIndex == sourceIndex);
        if (rowIndex < 0)
            return;

        var selectedItem = variantId == "variant.filter-state" ? "North" : null;
        pivot.RowFields[rowIndex] = pivot.RowFields[rowIndex] with
        {
            SelectedItem = selectedItem,
            SelectedItems = null,
        };
        pivot.LabelFilters.RemoveAll(filter => filter.SourceFieldIndex == sourceIndex);
        pivot.ValueFilters.RemoveAll(filter => filter.SourceFieldIndex == sourceIndex);
    }

    private ContextMenuDispatchEvidence ExerciseRecentFilesContextRoute(ContextMenuValidationDescriptor row)
    {
        if (!Enum.TryParse<BackstageRecentFileMenuAction>(row.ActionKey, out var action))
            return Failed(row, "Recent-file action could not be parsed.");
        var path = Path.Combine(Path.GetTempPath(), $"freex-context-validation-{Guid.NewGuid():N}.xlsx");
        _recentFiles.AddOrUpdate(path);
        if (action == BackstageRecentFileMenuAction.Unpin)
            _recentFiles.Pin(path);
        var entry = _recentFiles.Snapshot().Single(candidate => candidate.Path == path);
        ApplyBackstageRecentFileAction(entry, action);
        var after = _recentFiles.Snapshot().FirstOrDefault(candidate => candidate.Path == path);
        var observed = action switch
        {
            BackstageRecentFileMenuAction.Pin => after?.IsPinned == true,
            BackstageRecentFileMenuAction.Unpin => after is { IsPinned: false },
            BackstageRecentFileMenuAction.Remove => after is null,
            _ => false,
        };
        _recentFiles.Remove(path);
        return observed
            ? Passed(row, "production-store-effect-cleaned", "ApplyBackstageRecentFileAction", "Temporary recent-file state was removed.")
            : Failed(row, "Recent-file store did not reflect the dispatched action.");
    }

    private ContextMenuDispatchEvidence ExerciseQuickAccessContextRoute(ContextMenuValidationDescriptor row)
    {
        if (!Enum.TryParse<QuickAccessToolbarMenuAction>(row.ActionKey.Split(':')[0], out var action))
            return Failed(row, "QAT action could not be parsed.");

        if (action == QuickAccessToolbarMenuAction.ExecuteHistory)
        {
            ExecuteAvaloniaQuickAccessHistory(
                row.VariantId.EndsWith("redo-history", StringComparison.Ordinal)
                    ? QuickAccessToolbarCommandIds.Redo
                    : QuickAccessToolbarCommandIds.Undo,
                1);
            return Passed(row, "production-history-dispatch", "ExecuteAvaloniaQuickAccessHistory", "History dispatch completed in the disposable validation workbook.");
        }

        var previousOptionsPath = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        var fixtureDirectory = Path.Combine(Path.GetTempPath(), $"freex-qat-context-{Guid.NewGuid():N}");
        var fixturePath = Path.Combine(fixtureDirectory, "options.json");
        Directory.CreateDirectory(fixtureDirectory);
        try
        {
            var commandId = action == QuickAccessToolbarMenuAction.Add
                ? QuickAccessToolbarCommandIds.Bold
                : QuickAccessToolbarCommandIds.Redo;
            var options = new AppOptions
            {
                QuickAccessToolbarCommands = action == QuickAccessToolbarMenuAction.Add
                    ? [QuickAccessToolbarCommandIds.Save, QuickAccessToolbarCommandIds.Undo]
                    : [QuickAccessToolbarCommandIds.Save, commandId],
            };
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, fixturePath);
            if (!AppOptionsStore.Save(options))
                return Failed(row, options.LastPersistenceError ?? "Could not create the isolated QAT options fixture.");

            ApplyAvaloniaQuickAccessCustomization(new QuickAccessToolbarMenuCommand(
                "",
                action,
                CommandId: commandId));
            var changed = AppOptionsStore.Load().QuickAccessToolbarCommands.Contains(
                commandId,
                StringComparer.OrdinalIgnoreCase) == (action == QuickAccessToolbarMenuAction.Add);
            return changed
                ? Passed(row, "production-options-effect-isolated", "ApplyAvaloniaQuickAccessCustomization", "QAT options changed through the production handler in an isolated options store.")
                : Failed(row, "QAT options did not reflect the dispatched action.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousOptionsPath);
            _avaloniaQuickAccessOptions = AppOptionsStore.Load();
            RebuildAvaloniaQuickAccessToolbar();
            try { Directory.Delete(fixtureDirectory, recursive: true); } catch { /* best-effort fixture cleanup */ }
        }
    }

    private ContextMenuDispatchEvidence ExerciseWaterfallContextRoute(ContextMenuValidationDescriptor row)
    {
        var sheet = _session.ActiveSheet;
        var chart = new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
        };
        sheet.Charts.Add(chart);
        var pointIndex = row.VariantId.EndsWith("total-point", StringComparison.Ordinal) ? 3 : 0;
        if (pointIndex == 3)
            chart.WaterfallTotalPointIndices ??= [];
        if (pointIndex == 3)
            chart.WaterfallTotalPointIndices!.Add(pointIndex);
        var before = WaterfallChartContextMenuPlanner.IsPointTotal(chart, pointIndex);
        ToggleWaterfallTotalPoint(chart, pointIndex);
        var changed = WaterfallChartContextMenuPlanner.IsPointTotal(chart, pointIndex) != before;
        if (changed)
            ToggleWaterfallTotalPoint(chart, pointIndex);
        sheet.Charts.Remove(chart);
        return changed
            ? Passed(row, "production-command-effect-reversed", "ToggleWaterfallTotalPoint", "Total-point state changed and was restored.")
            : Failed(row, "Waterfall total-point state did not change.");
    }

    private ContextMenuDispatchEvidence ExerciseAutoFilterContextRoute(ContextMenuValidationDescriptor row)
    {
        var parts = row.ActionKey.Split(':', 2);
        if (parts.Length != 2 || !Enum.TryParse<AutoFilterMenuFilterKind>(parts[0], out var kind))
            return Failed(row, "AutoFilter criterion could not be parsed.");
        var model = AutoFilterMenuPlanner.Build(new AutoFilterMenuPlan(
            "Value",
            kind,
            [AutoFilterMenuCatalog.CreateFilterFamilyEntry(kind, InvariantAutoFilterMenuTextProvider.Instance)]));
        var optionIndex = model.CriteriaOptions.ToList().FindIndex(option => option.CriteriaPrefix == parts[1]);
        if (optionIndex < 0)
            return Failed(row, "AutoFilter criterion is absent from the production menu model.");
        var criteriaBox = new TextBox();
        var panel = CreateAutoFilterCriteriaPanel(model, criteriaBox);
        var selector = panel.GetVisualDescendants()
            .OfType<ComboBox>()
            .Single(combo => AutomationProperties.GetAutomationId(combo) == "AutoFilterCriteriaOperatorBox");
        selector.SelectedIndex = optionIndex;
        var option = model.CriteriaOptions[optionIndex];
        string? sampleValue = null;
        string? sampleSecondValue = null;
        var valueBox = panel.GetVisualDescendants()
            .OfType<TextBox>()
            .Single(textBox => !ReferenceEquals(textBox, criteriaBox) && textBox.PlaceholderText != "Maximum");
        if (option.RequiresValue)
        {
            sampleValue = AutoFilterMenuPlanner.RequiresCountCriteriaValue(option) ? "1" : "Value";
            valueBox.Text = sampleValue;
            if (AutoFilterMenuPlanner.RequiresSecondCriteriaValue(option))
            {
                var secondValueBox = panel.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Single(textBox => textBox.PlaceholderText == "Maximum");
                sampleSecondValue = "2";
                secondValueBox.Text = sampleSecondValue;
            }
        }
        // The panel is intentionally created off-tree for this bounded route probe. On the Linux
        // Skia host an unattached TextBox does not raise TextChanged for programmatic fixture input,
        // so apply the same production completion rule explicitly after selecting and seeding it.
        criteriaBox.Text = AutoFilterMenuPlanner.BuildCompletedCriteriaText(option, sampleValue, sampleSecondValue);
        var prefixObserved = criteriaBox.Text?.StartsWith(parts[1], StringComparison.OrdinalIgnoreCase) == true ||
            string.Equals(criteriaBox.Text, parts[1], StringComparison.OrdinalIgnoreCase);
        return prefixObserved
            ? Passed(row, "production-control-selection-observed", "CreateAutoFilterCriteriaPanel", $"Criteria field became '{criteriaBox.Text}'.")
            : Failed(row, $"Selecting the production criteria control produced '{criteriaBox.Text}'.");
    }

    private async Task<ContextMenuDispatchEvidence> ExerciseNativeMenuContextRouteAsync(ContextMenuValidationDescriptor row)
    {
        var parts = row.ActionKey.Split(':', 2);
        if (parts.Length != 2)
            return Failed(row, "Native menu action could not be parsed.");

        if (parts[0] != "file" || !Enum.TryParse<NativeFileMenuItemId>(parts[1], out var fileId))
        {
            return GetNativeMenuItemByActionKey(parts) is not null
                ? SkippedNativeMenuRoute(row)
                : Failed(row, "Native menu item did not resolve to the production Avalonia menu object.");
        }

        if (GetNativeFileMenuItem(fileId) is null)
            return Failed(row, "Native file menu item did not resolve to the production Avalonia menu object.");

        if (!IsOwnedNativeFileMenuValidationRoute(fileId))
            return SkippedNativeMenuRoute(row);

        return await InvokeProductionContextRouteAsync(
            row,
            () => _ = ExecuteOwnedNativeFileMenuItemAsync(fileId));
    }

    private static bool IsOwnedNativeFileMenuValidationRoute(NativeFileMenuItemId item) =>
        item is NativeFileMenuItemId.BackstageInfo or
            NativeFileMenuItemId.BackstageExport or
            NativeFileMenuItemId.BackstageAccount or
            NativeFileMenuItemId.Options or
            NativeFileMenuItemId.WorkbookStatistics or
            NativeFileMenuItemId.PageSetup or
            NativeFileMenuItemId.PrintPreview;

    private static bool IsOwnedNativeFileDialogRoute(ContextMenuValidationDescriptor row)
    {
        if (row.FamilyId != NativeMenuContextFamily)
            return false;

        var parts = row.ActionKey.Split(':', 2);
        return parts.Length == 2 &&
            parts[0] == "file" &&
            Enum.TryParse<NativeFileMenuItemId>(parts[1], out var fileId) &&
            IsOwnedNativeFileMenuValidationRoute(fileId);
    }

    private NativeMenuItem? GetNativeMenuItemByActionKey(string[] parts) =>
        parts.Length == 2 &&
        parts[0] == "menu" &&
        Enum.TryParse<NativeMenuItemId>(parts[1], out var itemId)
            ? GetNativeMenuItem(itemId)
            : null;

    private static ContextMenuDispatchEvidence SkippedNativeMenuRoute(ContextMenuValidationDescriptor row) =>
        new(
            "skipped",
            "native-menu-physical-boundary",
            row.ProductionRoute,
            "The real Avalonia NativeMenuItem is bound; activation remains a platform-native boundary outside this managed lane.");

    private async Task<ContextMenuDispatchEvidence> InvokeProductionContextRouteAsync(
        ContextMenuValidationDescriptor row,
        Action dispatch)
    {
        var preexisting = OwnedWindows.ToHashSet();
        var preexistingQuickAnalysisFlyout = _quickAnalysisFlyout;
        var statusBefore = _statusText.Text;
        var canUndoBefore = _session.CanUndo;
        dispatch();

        if (OpensQuickAnalysisFlyout(row))
        {
            var flyout = await WaitForQuickAnalysisFlyoutAsync(preexistingQuickAnalysisFlyout);
            if (flyout is null)
                return Failed(row, "The production route was expected to open the Quick Analysis flyout, but no flyout opened.");

            flyout.Hide();
            await Task.Delay(25);
            return Passed(
                row,
                "production-flyout-opened-dismissed",
                row.ProductionRoute,
                "The Quick Analysis flyout opened for the supported multi-cell selection and was dismissed without applying an action.");
        }

        if (OpensContextInlineEditor(row))
        {
            var inlineEditor = await WaitForContextInlineEditorAsync(row);
            if (!inlineEditor)
                return Failed(row, "The production route was expected to mount the WPF-parity inline editor, but no editor controls appeared.");

            CloseContextInlineEditor(row);
            await Task.Delay(25);
            return Passed(
                row,
                "production-inline-editor-opened-cancelled",
                row.ProductionRoute,
                "The production route mounted the worksheet inline editor and validation cancelled it without applying changes.");
        }

        if (EntersPictureCropMode(row))
        {
            if (!await WaitForPictureCropModeAsync())
                return Failed(row, "The production route was expected to enter picture crop mode, but crop mode was not observed.");

            ExitPictureCropMode();
            await Task.Delay(25);
            return Passed(
                row,
                "production-mode-entered-exited",
                row.ProductionRoute,
                "The production route entered picture crop mode and validation exited it without applying changes.");
        }

        Window? dialog;
        if (MayOpenOwnedContextDialog(row))
        {
            dialog = await WaitForContextDialogAsync(preexisting);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(25);
            dialog = OwnedWindows.FirstOrDefault(window => !preexisting.Contains(window));
        }
        if (dialog is not null)
        {
            try { dialog.Close(); } catch { /* best-effort validation cleanup */ }
            await Task.Delay(25);
            return Passed(row, "production-dialog-opened-cancelled", row.ProductionRoute, "The owned dialog opened and was closed without submitting changes.");
        }

        if (MayOpenOwnedContextDialog(row))
            return Failed(row, "The production route was expected to open an owned dialog, but no dialog appeared.");

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var createdUndo = _session.CanUndo && !canUndoBefore;
        if (createdUndo)
            UndoLastEdit();
        var statusChanged = !string.Equals(statusBefore, _statusText.Text, StringComparison.Ordinal);
        return Passed(
            row,
            createdUndo ? "production-mutation-undone" : statusChanged ? "production-status-observed" : "production-dispatch-completed",
            row.ProductionRoute,
            createdUndo ? "The command created undo state and validation immediately undid it." :
                statusChanged ? "The production handler updated shell status." : "The production handler returned without throwing or opening a dialog.");
    }

    private async Task<Window?> WaitForContextDialogAsync(HashSet<Window> preexisting)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var dialog = OwnedWindows.FirstOrDefault(window => !preexisting.Contains(window));
            if (dialog is not null)
                return dialog;
            await Task.Delay(25);
        }
        return OwnedWindows.FirstOrDefault(window => !preexisting.Contains(window));
    }

    private async Task<Flyout?> WaitForQuickAnalysisFlyoutAsync(Flyout? preexisting)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_quickAnalysisFlyout is { IsOpen: true } flyout && !ReferenceEquals(flyout, preexisting))
                return flyout;
            await Task.Delay(25);
        }

        return _quickAnalysisFlyout is { IsOpen: true } candidate && !ReferenceEquals(candidate, preexisting)
            ? candidate
            : null;
    }

    private async Task<bool> WaitForContextInlineEditorAsync(ContextMenuValidationDescriptor row)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (ContextInlineEditorIsMounted(row))
                return true;
            await Task.Delay(25);
        }

        return ContextInlineEditorIsMounted(row);
    }

    private async Task<bool> WaitForPictureCropModeAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_isPictureCropMode)
                return true;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(25);
        }

        return _isPictureCropMode;
    }

    private bool ContextInlineEditorIsMounted(ContextMenuValidationDescriptor row) =>
        (row.ActionKey is nameof(WorksheetContextMenuAction.NewComment) or
            nameof(WorksheetContextMenuAction.EditComment))
            ? _inlineThreadedCommentEditAddress is not null && _inlineThreadedCommentRootBox is not null
            : _inlineNoteEditAddress is not null && _inlineNoteEditBox is not null;

    private void CloseContextInlineEditor(ContextMenuValidationDescriptor row)
    {
        if (row.ActionKey is nameof(WorksheetContextMenuAction.NewComment) or
            nameof(WorksheetContextMenuAction.EditComment))
            CancelThreadedCommentInlineEdit();
        else
            CancelNoteInlineEdit();
    }

    private static bool OpensContextInlineEditor(ContextMenuValidationDescriptor row) =>
        row.FamilyId == WorksheetContextFamily &&
        (row.ActionKey is nameof(WorksheetContextMenuAction.NewComment) or
            nameof(WorksheetContextMenuAction.EditComment) or
            nameof(WorksheetContextMenuAction.NewNote) or
            nameof(WorksheetContextMenuAction.EditNote));

    private static bool EntersPictureCropMode(ContextMenuValidationDescriptor row) =>
        row.FamilyId == WorksheetContextFamily &&
        row.VariantId.Contains(".picture", StringComparison.Ordinal) &&
        row.ActionKey == nameof(WorksheetContextMenuAction.CropPicture);

    private static bool OpensQuickAnalysisFlyout(ContextMenuValidationDescriptor row) =>
        row.FamilyId == WorksheetContextFamily &&
        row.ActionKey == nameof(WorksheetContextMenuAction.QuickAnalysis);

    internal static bool MayOpenOwnedContextDialog(ContextMenuValidationDescriptor row)
    {
        if (row.FamilyId == NativeMenuContextFamily)
            return IsOwnedNativeFileDialogRoute(row);

        if (row.FamilyId == SheetTabContextFamily)
        {
            return row.ActionKey is nameof(SheetTabContextMenuAction.Rename) or
                nameof(SheetTabContextMenuAction.MoveOrCopy) or
                nameof(SheetTabContextMenuAction.ProtectSheet) or
                nameof(SheetTabContextMenuAction.Unhide);
        }
        if (row.FamilyId is PivotFieldContextFamily or PivotHeaderContextFamily or PivotChartContextFamily)
        {
            return row.ActionKey is "SelectItems" or "LabelFilter" or "ValueFilter" or
                "FieldSettings" or "ValueFieldSettings" or "MoreSortOptions";
        }
        if (row.FamilyId != WorksheetContextFamily)
            return false;

        return row.ActionKey is nameof(WorksheetContextMenuAction.PasteSpecial) or
            nameof(WorksheetContextMenuAction.InsertCopiedCells) or
            nameof(WorksheetContextMenuAction.InsertCells) or
            nameof(WorksheetContextMenuAction.DeleteCells) or
            nameof(WorksheetContextMenuAction.CustomSort) or
            nameof(WorksheetContextMenuAction.DefineName) or
            nameof(WorksheetContextMenuAction.CreateTable) or
            nameof(WorksheetContextMenuAction.FormatAsTable) or
            nameof(WorksheetContextMenuAction.TextToColumns) or
            nameof(WorksheetContextMenuAction.RemoveDuplicates) or
            nameof(WorksheetContextMenuAction.DataValidation) or
            nameof(WorksheetContextMenuAction.RowHeight) or
            nameof(WorksheetContextMenuAction.ColumnWidth) or
            nameof(WorksheetContextMenuAction.Hyperlink) or
            nameof(WorksheetContextMenuAction.PivotTableOptions) or
            nameof(WorksheetContextMenuAction.FormatCells) or
            nameof(WorksheetContextMenuAction.FormatPicture) or
            nameof(WorksheetContextMenuAction.FormatDrawingObject) or
            nameof(WorksheetContextMenuAction.ResizeDrawingObject) or
            nameof(WorksheetContextMenuAction.RotateDrawingObject) or
            nameof(WorksheetContextMenuAction.ShapeFill) or
            nameof(WorksheetContextMenuAction.ShapeOutline) or
            nameof(WorksheetContextMenuAction.FormatChartArea) or
            nameof(WorksheetContextMenuAction.SelectChartData) or
            nameof(WorksheetContextMenuAction.ChangeChartType) or
            nameof(WorksheetContextMenuAction.ChartTitles) or
            nameof(WorksheetContextMenuAction.ChartSizeAndProperties) or
            nameof(WorksheetContextMenuAction.MoveChart) or
            nameof(WorksheetContextMenuAction.EditAltText) or
            nameof(WorksheetContextMenuAction.SelectionPane);
    }

    private void PrepareWorksheetContextFixture(string variantId, WorksheetContextMenuAction action)
    {
        var sheet = _session.ActiveSheet;
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("Context validation"));
        _session.SelectCell(address);

        if (action == WorksheetContextMenuAction.QuickAnalysis)
        {
            var end = new CellAddress(sheet.Id, 3, 3);
            sheet.SetCell(address, new NumberValue(10));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(20));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
            sheet.SetCell(end, new NumberValue(40));
            _session.SelectRange(new GridRange(address, end));
        }

        if (action is WorksheetContextMenuAction.EditComment or
            WorksheetContextMenuAction.ResolveComment or
            WorksheetContextMenuAction.UnresolveComment or
            WorksheetContextMenuAction.DeleteComment)
        {
            _session.ExecuteReviewCommand(new SetThreadedCommentCommand(
                sheet.Id,
                address,
                "Context validation comment",
                "Validator"));
        }
        if (action is WorksheetContextMenuAction.EditNote or
            WorksheetContextMenuAction.DeleteNote or
            WorksheetContextMenuAction.ShowNotes or
            WorksheetContextMenuAction.ShowHideNote or
            WorksheetContextMenuAction.ShowAllNotes)
        {
            _session.ExecuteReviewCommand(new SetCommentCommand(
                sheet.Id,
                address,
                "Context validation note"));
        }

        if (variantId.Contains(".picture", StringComparison.Ordinal))
        {
            var picture = EnsureParityPicture();
            _selectedDrawingObjectKind = SelectionPaneObjectKind.Picture;
            _selectedDrawingObjectId = picture.Id;
        }
        else if (variantId.Contains(".shape", StringComparison.Ordinal))
        {
            var shape = EnsureParityShape();
            _selectedDrawingObjectKind = SelectionPaneObjectKind.Shape;
            _selectedDrawingObjectId = shape?.Id;
        }
        else if (variantId.Contains(".text-box", StringComparison.Ordinal))
        {
            var textBox = sheet.TextBoxes.FirstOrDefault(item => item.IsVisible);
            if (textBox is null)
            {
                textBox = new TextBoxModel
                {
                    Anchor = address,
                    Text = "Context validation text box",
                };
                sheet.TextBoxes.Add(textBox);
            }
            _selectedDrawingObjectKind = SelectionPaneObjectKind.TextBox;
            _selectedDrawingObjectId = textBox.Id;
        }
        else if (variantId.Contains(".chart", StringComparison.Ordinal))
        {
            var chart = EnsureParityChart();
            _selectedDrawingObjectKind = SelectionPaneObjectKind.Chart;
            _selectedDrawingObjectId = chart?.Id;
        }

        if (action == WorksheetContextMenuAction.PivotTableOptions)
        {
            var pivot = EnsureParityPivot();
            if (pivot is not null)
                _session.SelectCell(pivot.TargetRange.Start);
        }
    }

    private static ContextMenuDispatchEvidence Passed(
        ContextMenuValidationDescriptor row,
        string level,
        string evidence,
        string note) => new("passed", level, evidence, note);

    private static ContextMenuDispatchEvidence Failed(ContextMenuValidationDescriptor row, string note) =>
        new("failed", "production-dispatch-failed", row.ProductionRoute, note);

    private static void AddWorksheetContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        foreach (var target in Enum.GetValues<WorksheetContextMenuTargetKind>())
        {
            var states = target == WorksheetContextMenuTargetKind.Worksheet
                ? WorksheetContextValidationStates()
                : [("default", WorksheetContextMenuState.Default)];
            foreach (var (stateName, state) in states)
            foreach (var command in FlattenContextCommands(WorksheetContextMenuPlanner.BuildCommands(target, state)))
            {
                var variant = WorksheetContextVariantId(target, stateName);
                rows.Add(new ContextMenuValidationDescriptor(
                    $"{WorksheetContextFamily}:{target}:{stateName}:{variant}:{command.Action}",
                    WorksheetContextFamily,
                    variant,
                    command.Action.ToString(),
                    command.Header,
                    command.IsEnabled,
                    target is WorksheetContextMenuTargetKind.Picture or WorksheetContextMenuTargetKind.Shape or
                        WorksheetContextMenuTargetKind.TextBox or WorksheetContextMenuTargetKind.Chart
                        ? "DispatchDrawingObjectContextMenuCommand"
                        : "DispatchWorksheetContextMenuCommand"));
            }
        }

        // Show Notes has a live Avalonia production dispatcher but is not currently emitted by the
        // neutral planner. Keep it in the executable denominator so that omission cannot disappear
        // behind planner bookkeeping again.
        foreach (var variant in new[]
        {
            "context-menu.worksheet.target.worksheet",
            "context-menu.worksheet.state.note",
        })
        {
            rows.Add(new ContextMenuValidationDescriptor(
                $"{WorksheetContextFamily}:Worksheet:show-notes:{variant}:{WorksheetContextMenuAction.ShowNotes}",
                WorksheetContextFamily,
                variant,
                WorksheetContextMenuAction.ShowNotes.ToString(),
                "Show Notes",
                true,
                "DispatchWorksheetContextMenuCommand"));
        }
    }

    private static string WorksheetContextVariantId(WorksheetContextMenuTargetKind target, string stateName)
    {
        if (target != WorksheetContextMenuTargetKind.Worksheet)
        {
            return target switch
            {
                WorksheetContextMenuTargetKind.Picture => "context-menu.worksheet.target.picture",
                WorksheetContextMenuTargetKind.Shape => "context-menu.worksheet.target.shape",
                WorksheetContextMenuTargetKind.TextBox => "context-menu.worksheet.target.text-box",
                WorksheetContextMenuTargetKind.Chart => "context-menu.worksheet.target.chart",
                WorksheetContextMenuTargetKind.RowSelection => "context-menu.worksheet.target.row-selection",
                WorksheetContextMenuTargetKind.ColumnSelection => "context-menu.worksheet.target.column-selection",
                _ => "context-menu.worksheet.target.worksheet",
            };
        }

        if (!stateName.StartsWith("state-", StringComparison.Ordinal) ||
            !int.TryParse(stateName.AsSpan("state-".Length), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var bits) ||
            bits == 0 || (bits & (bits - 1)) != 0)
        {
            return "context-menu.worksheet.target.worksheet";
        }

        return bits switch
        {
            1 => "context-menu.worksheet.state.threaded-comment",
            1 << 1 => "context-menu.worksheet.state.resolved-comment",
            1 << 2 => "context-menu.worksheet.state.note",
            1 << 3 => "context-menu.worksheet.state.hyperlink",
            1 << 4 => "context-menu.worksheet.state.auto-filter-header",
            1 << 5 => "context-menu.worksheet.state.dropdown",
            1 << 6 => "context-menu.worksheet.state.pivot-table",
            1 << 7 => "context-menu.worksheet.state.note-shown",
            _ => "context-menu.worksheet.target.worksheet",
        };
    }

    private static void AddSheetTabContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        var variants = new[]
        {
            ("variant.default", SheetTabContextMenuState.Default),
            ("variant.restricted-state", new SheetTabContextMenuState(false, false, false, false, false)),
        };
        foreach (var (variant, state) in variants)
        foreach (var command in SheetTabContextMenuPlanner.BuildSheetTabCommands(state).Where(command => !command.IsSeparator))
            rows.Add(new($"{variant}:{command.Action}", SheetTabContextFamily, variant, command.Action.ToString(), command.CommandName, command.IsEnabled, "sheet-tab production handler"));
    }

    private static void AddStatusBarContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        foreach (var command in StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands()
                     .Where(command => !command.IsSeparator && command.OptionTag.Length > 0))
        {
            var variant = $"context-menu.status-bar.option.{command.OptionTag}";
            rows.Add(new(variant, StatusBarContextFamily, variant, command.OptionTag, command.OptionTag, command.IsEnabled, "OnStatusBarCustomizeToggled"));
        }
    }

    private static void AddPivotFieldContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        foreach (var variantName in new[] { "available-fields", "filters-bucket", "columns-bucket", "rows-bucket", "values-bucket" })
        {
            var variant = $"variant.{variantName}";
            var includeRemove = variantName != "available-fields";
            foreach (var command in PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove).Where(command => !command.IsSeparator))
                rows.Add(new($"{variant}:{command.Action}", PivotFieldContextFamily, variant, command.Action.ToString(), command.CommandName, command.IsEnabled, "DispatchPivotFieldContextMenuAction"));
        }
    }

    private static void AddPivotHeaderContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        var pivot = new PivotTableModel { Name = "ValidationPivot", ShowExpandCollapseButtons = true };
        foreach (var area in Enum.GetValues<PivotHeaderArea>())
        {
            var variant = $"context-menu.pivot-header.area.{area.ToString().ToLowerInvariant()}";
            var target = new PivotHeaderDropdownTargetModel(pivot.Name, "Field", 0, area, false, area == PivotHeaderArea.Value ? 0 : null);
            foreach (var item in PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target).Items.Where(item => !item.IsSeparator))
                rows.Add(new($"{variant}:{item.Action}", PivotHeaderContextFamily, variant, item.Action.ToString(), item.Label, item.IsEnabled, "InvokePivotHeaderAction"));
        }
    }

    private static void AddPivotChartContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        foreach (var (name, state) in new[]
        {
            ("filter-state", new PivotChartFieldContextMenuState(true, "Field: Filtered", "Select Items...", "Label Filter...", "Value Filter...", "Clear Filter", true, true, true)),
            ("no-filter-state", new PivotChartFieldContextMenuState(false, "Field: (All)", "Select Items...", "Label Filter...", "Value Filter...", "Clear Filter", false, false, true)),
        })
        {
            var variant = $"variant.{name}";
            foreach (var command in PivotChartFieldContextMenuPlanner.BuildCommands(state)
                         .Where(command => !command.IsSeparator && command.Action != PivotChartFieldContextMenuAction.Summary))
                rows.Add(new($"{variant}:{command.Action}", PivotChartContextFamily, variant, command.Action.ToString(), command.Header, command.IsEnabled, "DispatchPivotChartFieldContextMenuAction"));
        }
    }

    private static void AddRecentFilesContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        foreach (var (name, commands) in new[]
        {
            ("recent", BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands()),
            ("pinned", BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands()),
        })
        {
            var variant = $"variant.{name}";
            foreach (var command in commands)
                rows.Add(new($"{variant}:{command.Action}", RecentFilesContextFamily, variant, command.Action.ToString(), command.CommandName, true, "ApplyBackstageRecentFileAction"));
        }
    }

    private static void AddQuickAccessContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        var customization = new[]
        {
            QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(new("Redo", ["Save", "Undo"])).Single(),
            QuickAccessToolbarContextMenuPlanner.BuildCustomizationCommands(new("Undo", ["Save", "Undo"])).Single(),
        };
        var customizationVariant = "variant.customization";
        foreach (var command in customization)
            rows.Add(new($"{customizationVariant}:{command.Action}", QuickAccessContextFamily, customizationVariant, command.Action.ToString(), command.ResourceKey, command.IsEnabled, "ApplyAvaloniaQuickAccessCustomization"));

        foreach (var (name, redo) in new[] { ("undo-history", false), ("redo-history", true) })
        {
            var variant = $"variant.{name}";
            foreach (var command in QuickAccessToolbarContextMenuPlanner.BuildHistoryCommands(new(redo, ["Validation action"])))
                rows.Add(new($"{variant}:{command.Action}:{command.ActionCount}", QuickAccessContextFamily, variant, $"{command.Action}:{command.ActionCount}", command.Header, command.IsEnabled, "ExecuteAvaloniaQuickAccessHistory"));
        }
    }

    private static void AddWaterfallContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        foreach (var name in new[] { "regular-point", "total-point", "invalid-point" })
        {
            var variant = $"variant.{name}";
            rows.Add(new($"{variant}:ToggleTotal", WaterfallContextFamily, variant, "ToggleTotal", "Set as Total", name != "invalid-point", "ToggleWaterfallTotalPoint"));
        }
    }

    private static void AddAutoFilterContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        foreach (var kind in Enum.GetValues<AutoFilterMenuFilterKind>())
        foreach (var descriptor in AutoFilterMenuCatalog.GetCriteriaDescriptors(kind))
        {
            var variant = $"context-menu.auto-filter.{kind.ToString().ToLowerInvariant()}.{descriptor.ResourceKey}";
            rows.Add(new(variant, AutoFilterContextFamily, variant, $"{kind}:{descriptor.CriteriaPrefix}", descriptor.ResourceKey, true, "CreateAutoFilterCriteriaPanel"));
        }
    }

    private static void AddNativeMenuContextInventory(List<ContextMenuValidationDescriptor> rows)
    {
        foreach (var menu in NativeMenuCatalog.TopLevelMenus)
        {
            var variant = $"context-menu.native-application.{menu.Id.ToString().ToLowerInvariant()}";
            if (menu.Id == NativeMenuTopLevelId.File)
            {
                foreach (var entry in NativeMenuCatalog.FileMenuEntries.Where(entry => entry.Kind == NativeMenuEntryKind.Item))
                {
                    var item = entry.Item!;
                    rows.Add(new($"{variant}:{item.Id}", NativeMenuContextFamily, variant, $"file:{item.Id}", item.Label, true, "GetNativeFileMenuItem"));
                }
                continue;
            }

            foreach (var entry in NativeMenuCatalog.GetMenuEntries(menu.Id).Where(entry => entry.Kind == NativeMenuEntryKind.Item))
            {
                var id = entry.ItemId!.Value;
                rows.Add(new($"{variant}:{id}", NativeMenuContextFamily, variant, $"menu:{id}", NativeMenuCatalog.GetMenuItem(id).Label, true, "GetNativeMenuItem"));
            }
        }
    }
}
