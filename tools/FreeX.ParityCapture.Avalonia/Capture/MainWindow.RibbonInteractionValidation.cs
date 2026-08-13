using Avalonia.Controls;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.Ribbon;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const string RibbonCommandBehaviorCategory = "ribbon-command-behavior";
    private const string RibbonPlacementBehaviorCategory = "ribbon-placement-behavior";
    private const string RibbonValidationStatusSentinel = "__ribbon_validation_before__";
    internal static int InteractiveValidationRibbonCommandCount => AvaloniaRibbonComposition
        .EnumerateSurfaceRows(AvaloniaRibbonComposition.BuildDefinition())
        .Select(row => row.CommandId)
        .Distinct()
        .Count();

    private sealed record RibbonCommandBehaviorEvidence(
        RibbonCommandId CommandId,
        string Status,
        string EvidenceLevel,
        string Evidence,
        string Note);

    private sealed record RibbonLifecycleSnapshot(
        int DirtyGeneration,
        string Status,
        string WorkbookState,
        string ShellState,
        string BorderPickerState);

    /// <summary>
    /// Executes each visible runtime command once in a reusable production window with a fresh session.
    /// Placements refer to that command result, so duplicate split-button/menu placements do not repeat mutations.
    /// </summary>
    internal void AddRibbonInteractionExecutionResults(
        List<InteractionValidationResult> results,
        int commandStart = 0,
        int commandCount = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(results);

        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var placements = AvaloniaRibbonComposition.EnumerateSurfaceRows(definition).ToArray();
        var selectedCommandIds = placements
            .Select(row => row.CommandId)
            .Distinct()
            .Skip(Math.Max(0, commandStart))
            .Take(Math.Max(0, commandCount))
            .ToHashSet();
        var selectedPlacements = placements
            .Where(row => selectedCommandIds.Contains(row.CommandId))
            .ToArray();
        var evidenceByCommand = BuildRibbonCommandBehaviorEvidence(selectedPlacements);

        foreach (var evidence in evidenceByCommand.Values.OrderBy(item => item.CommandId.Value, StringComparer.Ordinal))
        {
            results.Add(new InteractionValidationResult(
                Id: $"ribbon-command-behavior/{EscapeResultId(evidence.CommandId.Value)}",
                Category: RibbonCommandBehaviorCategory,
                Status: evidence.Status,
                EvidenceLevel: evidence.EvidenceLevel,
                Evidence: evidence.Evidence,
                Note: evidence.Note));
        }

        foreach (var placement in selectedPlacements)
        {
            var evidence = evidenceByCommand[placement.CommandId];
            results.Add(new InteractionValidationResult(
                Id: $"ribbon-placement-behavior/{placement.RowId}",
                Category: RibbonPlacementBehaviorCategory,
                Status: evidence.Status,
                EvidenceLevel: "command-behavior-reference",
                Evidence: $"{placement.TabHeader} > {placement.GroupHeader} > {placement.Label} -> {evidence.EvidenceLevel}",
                Note: $"Command evidence: ribbon-command-behavior/{EscapeResultId(placement.CommandId.Value)}. {evidence.Note}"));
        }

    }

    private IReadOnlyDictionary<RibbonCommandId, RibbonCommandBehaviorEvidence> BuildRibbonCommandBehaviorEvidence(
        IReadOnlyList<AvaloniaRibbonComposition.SurfaceRow> placements)
    {
        var registry = _ribbonCommandRegistry;
        var firstPlacementByCommand = placements
            .GroupBy(row => row.CommandId)
            .ToDictionary(group => group.Key, group => group.First());
        var results = new Dictionary<RibbonCommandId, RibbonCommandBehaviorEvidence>();
        MainWindow? validationWindow = null;

        try
        {
            validationWindow = new MainWindow([]);
            validationWindow.Show();
            var processed = 0;

            foreach (var (commandId, firstPlacement) in firstPlacementByCommand)
            {
                if (registry is null || !registry.TryGet(commandId, out var command) || command is null)
                {
                    results[commandId] = Failure(
                        commandId,
                        "unregistered-command",
                        "No production command is registered for this runtime ribbon id.");
                    continue;
                }

                if (command is EmptyRibbonCommand)
                {
                    results[commandId] = Failure(
                        commandId,
                        "empty-command-gap",
                        "The production registry resolves this id to EmptyRibbonCommand.");
                    continue;
                }

                results[commandId] = IsExternalBoundaryCommand(commandId.Value)
                    ? Skipped(
                        commandId,
                        "native-external-unexercised",
                        command,
                        "The command crosses the OS, clipboard, network, process, print, or top-level window boundary. It remains uncredited until the physical X11 lane invokes and verifies that boundary.")
                    : validationWindow.ExecuteProductionCommandInReusableWindow(
                        commandId,
                        firstPlacement,
                        replaceSession: processed % 32 == 0);

                processed++;
                if (processed % 32 == 0)
                    ForceRibbonValidationCleanup();
            }
        }
        finally
        {
            if (validationWindow is not null)
                TearDownRibbonValidationWindow(validationWindow);
        }

        return results;
    }

    private RibbonCommandBehaviorEvidence ExecuteProductionCommandInReusableWindow(
        RibbonCommandId commandId,
        AvaloniaRibbonComposition.SurfaceRow placement,
        bool replaceSession)
    {
        var ownedBefore = new HashSet<Window>();
        try
        {
            ResetRibbonValidationWindow(replaceSession);

            if (!TryPrepareRibbonContextFixture(placement.ActivationKey, out var fixtureEvidence))
                return Failure(commandId, "context-fixture-failed", fixtureEvidence);

            PrepareRibbonBorderPickerFixture(commandId);

            var registry = _ribbonCommandRegistry;
            if (registry is null || !registry.TryGet(commandId, out var command) || command is null)
                return Failure(commandId, "validation-window-route-missing", "The reusable production window did not register this command.");
            if (command is EmptyRibbonCommand)
                return Failure(commandId, "validation-window-empty-command", "The reusable production window resolved this id to EmptyRibbonCommand.");

            if (command is IRibbonStatefulCommand stateful)
            {
                var state = stateful.GetState();
                if (!state.IsEnabled)
                {
                    return Passed(
                        commandId,
                        "disabled-state-verified",
                        $"{command.GetType().Name}: enabled=false, checked={state.IsChecked}, value={state.Value ?? "<null>"}; {fixtureEvidence}",
                        "The reusable production window and required context fixture resolved the command as disabled, so the ribbon correctly prevents dispatch.");
                }
            }

            _statusText.Text = RibbonValidationStatusSentinel;
            ownedBefore = OwnedWindows.ToHashSet();
            var before = CaptureRibbonLifecycleSnapshot();
            var context = command is ValueRibbonCommand
                ? RibbonCommandContext.ForSelectedValue(GetDeterministicSelectedValue(commandId))
                : RibbonCommandContext.Empty;

            command.Execute(context);

            var newlyOwned = OwnedWindows
                .Where(window => !ownedBefore.Contains(window))
                .ToArray();
            var after = CaptureRibbonLifecycleSnapshot();
            var observations = DescribeLifecycleChanges(before, after, newlyOwned);
            CloseOwnedWindows(newlyOwned);

            if (observations.Count == 0)
            {
                return Skipped(
                    commandId,
                    "executed-unverified-postcondition",
                    command,
                    $"The production command was invoked with a fresh session but returned without an observable workbook, shell, status, or owned-window lifecycle change; {fixtureEvidence}");
            }

            return Passed(
                commandId,
                "executed-production-lifecycle",
                $"{command.GetType().Name}: {string.Join("; ", observations)}; {fixtureEvidence}",
                "Invoked the production command in the reusable shown validation MainWindow with a fresh isolated session, observed a lifecycle postcondition, and closed every newly owned surface.");
        }
        catch (Exception ex)
        {
            return Failure(commandId, "production-execution-threw", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            CloseOwnedWindows(OwnedWindows.Where(window => !ownedBefore.Contains(window)).ToArray());
        }
    }

    private void ResetRibbonValidationWindow(bool replaceSession)
    {
        CloseOwnedWindows(OwnedWindows.ToArray());
        _sheetGridHost.Content = null;
        _sheetTabsHost.Content = null;
        _activeCellBorder = null;
        if (replaceSession || _session.Workbook.Sheets.Count == 0)
            ReplaceSession(CreateDisposableRibbonSession());
        _selectedDrawingObjectKind = null;
        _selectedDrawingObjectId = null;
        _ribbonContextSource.OnSelectionCleared();
        _ribbonContextSource.OnTableActive(false);
        _ribbonContextSource.OnPivotActive(false);
        HideBackstageOverlay();
        _pivotFieldPaneHost.IsVisible = false;
        _formulaBarExpanded = false;
        _isFormulaBarHidden = false;
        _formulaBarHost.IsVisible = true;
        WindowState = global::Avalonia.Controls.WindowState.Normal;
        _statusText.Text = "Ribbon validation fixture ready";
    }

    private static void TearDownRibbonValidationWindow(MainWindow validationWindow)
    {
        try
        {
            CloseOwnedWindows(validationWindow.OwnedWindows.ToArray());
            if (validationWindow.IsVisible)
                validationWindow.Close();
            validationWindow.Content = null;
            ForceRibbonValidationCleanup();
        }
        catch
        {
            // Best-effort teardown of the isolated reusable owner.
        }
    }

    private static void ForceRibbonValidationCleanup()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
    }

    private bool TryPrepareRibbonContextFixture(string? activationKey, out string evidence)
    {
        switch (activationKey)
        {
            case null:
                evidence = "fixture=worksheet";
                return true;

            case "chart.selected":
                if (TryEnsureRibbonValidationChart() is not { } chart)
                {
                    evidence = "fixture=chart.failed";
                    return false;
                }
                _selectedDrawingObjectKind = SelectionPaneObjectKind.Chart;
                _selectedDrawingObjectId = chart.Id;
                _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.Chart);
                evidence = $"fixture=chart:{chart.Id}";
                return true;

            case "picture.selected":
                var picture = EnsureRibbonValidationPicture();
                _selectedDrawingObjectKind = SelectionPaneObjectKind.Picture;
                _selectedDrawingObjectId = picture.Id;
                _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.Picture);
                evidence = $"fixture=picture:{picture.Id}";
                return true;

            case "shape.selected":
                if (TryEnsureRibbonValidationShape() is not { } shape)
                {
                    evidence = "fixture=shape.failed";
                    return false;
                }
                _selectedDrawingObjectKind = SelectionPaneObjectKind.Shape;
                _selectedDrawingObjectId = shape.Id;
                _ribbonContextSource.OnDrawingObjectSelected(SelectionPaneObjectKind.Shape);
                evidence = $"fixture=shape:{shape.Id}";
                return true;

            case "table.active":
                if (!TryEnsureRibbonValidationTable(out var table))
                {
                    evidence = "fixture=table.failed";
                    return false;
                }
                _session.SelectRange(new GridRange(table.Range.Start, table.Range.Start));
                _ribbonContextSource.OnTableActive(true);
                evidence = $"fixture=table:{table.DisplayName}";
                return true;

            case "pivot.active":
                if (TryEnsureRibbonValidationPivot() is not { } pivot)
                {
                    evidence = "fixture=pivot.failed";
                    return false;
                }
                _session.SelectRange(new GridRange(pivot.TargetRange.Start, pivot.TargetRange.Start));
                _ribbonContextSource.OnPivotActive(true);
                evidence = $"fixture=pivot:{pivot.Name}";
                return true;

            default:
                evidence = $"fixture=unsupported:{activationKey}";
                return false;
        }
    }

    private ChartModel? TryEnsureRibbonValidationChart()
    {
        var sheet = _session.ActiveSheet;
        if (sheet.Charts.FirstOrDefault(chart => chart.IsVisible) is { } existing)
            return existing;

        var command = new AddChartCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            ChartType.Column,
            title: "Ribbon validation chart",
            left: 260,
            top: 96,
            width: 360,
            height: 240);
        var result = _session.ExecuteReviewCommand(command);
        return result.Success
            ? sheet.Charts.FirstOrDefault(chart => chart.Id == command.ChartId)
            : null;
    }

    private PictureModel EnsureRibbonValidationPicture()
    {
        var sheet = _session.ActiveSheet;
        if (sheet.Pictures.FirstOrDefault(picture => picture.IsVisible) is { } existing)
            return existing;

        var picture = new PictureModel
        {
            Name = "Ribbon validation picture",
            Anchor = new CellAddress(sheet.Id, 6, 5),
            Kind = PictureKind.Image,
            ImageBytes = [0x89, 0x50, 0x4E, 0x47],
            ContentType = "image/png",
            Width = 180,
            Height = 110,
        };
        sheet.Pictures.Add(picture);
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id));
        return picture;
    }

    private DrawingShapeModel? TryEnsureRibbonValidationShape()
    {
        var sheet = _session.ActiveSheet;
        if (sheet.DrawingShapes.FirstOrDefault(shape => shape.IsVisible) is { } existing)
            return existing;

        var command = new AddDrawingShapeCommand(
            sheet.Id,
            new CellAddress(sheet.Id, 6, 2),
            DrawingShapeKind.Rectangle,
            width: 150,
            height: 90,
            fillColor: new CellColor(91, 155, 213),
            outlineColor: new CellColor(47, 84, 150));
        var result = _session.ExecuteReviewCommand(command);
        return result.Success
            ? sheet.DrawingShapes.FirstOrDefault(shape => shape.Id == command.ShapeId)
            : null;
    }

    private PivotTableModel? TryEnsureRibbonValidationPivot()
    {
        var sheet = _session.ActiveSheet;
        if (sheet.PivotTables.FirstOrDefault() is { } existing)
            return existing;

        SeedParityPivotSource(sheet);
        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 8, 5));
        var targetRange = new GridRange(
            new CellAddress(sheet.Id, 2, 7),
            new CellAddress(sheet.Id, 2, 7));
        var cacheId = _session.Workbook.PivotCaches.Count == 0
            ? 1
            : _session.Workbook.PivotCaches.Max(cache => cache.CacheId) + 1;
        var cache = new PivotCacheModel
        {
            CacheId = cacheId,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString(),
        };
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
            cache.Fields.Add(new PivotCacheFieldModel(ParityPivotHeader(sheet, sourceRange.Start.Row, col)));
        _session.Workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "RibbonValidationPivot",
            CacheId = cacheId,
            SourceRange = sourceRange,
            TargetRange = targetRange,
            LastRenderedRange = new GridRange(
                targetRange.Start,
                new CellAddress(sheet.Id, targetRange.Start.Row + 4, targetRange.Start.Col + 2)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Sum of Revenue", "sum"));
        sheet.PivotTables.Add(pivot);
        return pivot;
    }

    private bool TryEnsureRibbonValidationTable(out StructuredTableModel table)
    {
        var sheet = _session.ActiveSheet;
        if (sheet.StructuredTables.FirstOrDefault() is { } existing)
        {
            table = existing;
            return true;
        }

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 4));
        var command = new CreateStructuredTableCommand(sheet.Id, range);
        var result = _session.ExecuteReviewCommand(command);
        table = sheet.StructuredTables.FirstOrDefault(item => item.Id == command.CreatedTableId)
            ?? sheet.StructuredTables.FirstOrDefault()!;
        return result.Success && table is not null;
    }

    private RibbonLifecycleSnapshot CaptureRibbonLifecycleSnapshot()
    {
        var workbook = _session.Workbook;
        var sheets = workbook.Sheets.ToArray();
        var workbookState = $"{workbook.Sheets.Count}|{_session.ActiveSheet.Id}|{_session.ActiveCell}|" +
            $"{_session.SelectedRange}|{sheets.Sum(sheet => sheet.Charts.Count)}|" +
            $"{sheets.Sum(sheet => sheet.Pictures.Count)}|{sheets.Sum(sheet => sheet.DrawingShapes.Count)}|" +
            $"{sheets.Sum(sheet => sheet.StructuredTables.Count)}|{sheets.Sum(sheet => sheet.PivotTables.Count)}|" +
            $"{sheets.Sum(sheet => sheet.TextBoxes.Count)}|{sheets.Sum(sheet => sheet.MergedRegions.Count)}";
        var shellState = $"{_backstageOverlay.IsVisible}|{_pivotFieldPaneHost.IsVisible}|" +
            $"{_formulaBarHost.IsVisible}|{_formulaBarExpanded}|{_session.IsShowingGridlines}|" +
            $"{_session.IsShowingHeadings}|{_selectedDrawingObjectKind}|{_selectedDrawingObjectId}|" +
            $"{IsVisible}|{WindowState}";
        var borderPickerState = $"{_borderPickerStyle}|{_borderPickerColor.R},{_borderPickerColor.G},{_borderPickerColor.B}";
        return new RibbonLifecycleSnapshot(
            _session.DirtyGeneration,
            _statusText.Text ?? string.Empty,
            workbookState,
            shellState,
            borderPickerState);
    }

    private static List<string> DescribeLifecycleChanges(
        RibbonLifecycleSnapshot before,
        RibbonLifecycleSnapshot after,
        IReadOnlyList<Window> newlyOwned)
    {
        var observations = new List<string>();
        if (before.DirtyGeneration != after.DirtyGeneration)
            observations.Add($"dirty-generation={before.DirtyGeneration}->{after.DirtyGeneration}");
        if (!string.Equals(before.WorkbookState, after.WorkbookState, StringComparison.Ordinal))
            observations.Add("workbook-state-changed");
        if (!string.Equals(before.ShellState, after.ShellState, StringComparison.Ordinal))
            observations.Add("shell-state-changed");
        if (!string.Equals(before.BorderPickerState, after.BorderPickerState, StringComparison.Ordinal))
            observations.Add("border-picker-state-changed");
        if (!string.Equals(before.Status, after.Status, StringComparison.Ordinal))
            observations.Add($"status={after.Status}");
        if (newlyOwned.Count > 0)
        {
            observations.Add("owned-surface=" + string.Join(',', newlyOwned.Select(window =>
                string.IsNullOrWhiteSpace(window.Title) ? window.GetType().Name : window.Title)));
        }
        return observations;
    }

    private static void CloseOwnedWindows(IEnumerable<Window> windows)
    {
        foreach (var window in windows.Reverse())
        {
            try
            {
                CloseOwnedWindows(window.OwnedWindows.ToArray());
                window.Close();
            }
            catch
            {
                // Closing is best-effort; the isolated owner is discarded immediately afterward.
            }
        }
    }

    private static string GetDeterministicSelectedValue(RibbonCommandId commandId)
    {
        var id = commandId.Value;
        if (string.Equals(id, "Font", StringComparison.Ordinal))
            return "Arial";
        if (string.Equals(id, "Font Size", StringComparison.Ordinal))
            return "11";
        if (string.Equals(id, "Number Format", StringComparison.Ordinal))
            return HomeNumberFormatDropdownPlanner.Options[0].Label;
        return "1";
    }

    private void PrepareRibbonBorderPickerFixture(RibbonCommandId commandId)
    {
        switch (commandId.Value)
        {
            case "Black":
            case "Gray":
            case "Accent 1":
            case "Accent 2":
                _borderPickerColor = commandId.Value == "Black"
                    ? new CellColor(0, 112, 192)
                    : CellColor.Black;
                break;
            case "Thin":
            case "Medium":
            case "Thick":
            case "Dashed":
            case "Dotted":
            case "Double":
                _borderPickerStyle = commandId.Value == "Thin"
                    ? BorderStyle.Medium
                    : BorderStyle.Thin;
                break;
        }
    }

    private static WorkbookSession CreateDisposableRibbonSession() =>
        ParityCaptureWorkbookSessionFactory.Create(
            new WorkbookSessionFactory(),
            viewportHeight: 480,
            viewportWidth: 800,
            includeObjects: true);

    private static bool IsExternalBoundaryCommand(string commandId) =>
        ExternalBoundaryTokens.Any(token => commandId.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static RibbonCommandBehaviorEvidence Passed(
        RibbonCommandId commandId,
        string evidenceLevel,
        string evidence,
        string note) =>
        new(commandId, "passed", evidenceLevel, evidence, note);

    private static RibbonCommandBehaviorEvidence Skipped(
        RibbonCommandId commandId,
        string evidenceLevel,
        IRibbonCommand command,
        string note) =>
        new(commandId, "skipped", evidenceLevel, $"{commandId.Value} | {command.GetType().Name}", note);

    private static RibbonCommandBehaviorEvidence Failure(
        RibbonCommandId commandId,
        string evidenceLevel,
        string note) =>
        new(commandId, "failed", evidenceLevel, commandId.Value, note);

    private static string EscapeResultId(string value) => Uri.EscapeDataString(value);

    private static readonly string[] ExternalBoundaryTokens =
    [
        "Open", "Save", "Print", "Export", "Share", "Get Data", "Online", "Feedback", "Update",
        "Clipboard", "Copy", "Cut", "Paste", "Email", "Import", "Connection", "Insert Picture",
        "Pictures", "Insert Object", "New Window", "Arrange All", "Switch Windows", "Hide Window",
        "Side by Side", "Synchronous Scrolling", "Close Window", "Exit", "Quit",
    ];
}
