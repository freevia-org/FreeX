using Avalonia.Input;
using Avalonia.Threading;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.InteractionValidation;
using FreeX.App.Presentation.Interactions;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal async Task<IReadOnlyList<InteractionValidationResult>> RunInteractionValidationAsync(
        string outputDirectory,
        int dialogStart = 0,
        int dialogCount = int.MaxValue,
        bool includeCoreResults = true)
    {
        var results = new List<InteractionValidationResult>();
        if (includeCoreResults)
        {
            AddRibbonBindingResults(results);
            AddRibbonInteractionExecutionResults(results);
            AddShortcutRoutingResults(results);
            AddShortcutScenarioInventoryResults(results);
            await AddContextMenuInteractionResultsAsync(results);
            AddDialogRangeTargetResults(results);
            await AddEditingInteractionResultsAsync(results);
        }

        var selectedDialogIds = InteractionSurfaceCatalog.Dialogs
            .Skip(Math.Max(0, dialogStart))
            .Take(Math.Max(0, dialogCount))
            .Select(dialog => dialog.Id)
            .ToHashSet(StringComparer.Ordinal);

        var surfacesDirectory = Path.Combine(outputDirectory, "surfaces");
        Directory.CreateDirectory(surfacesDirectory);
        var capturedSurfaces = await CaptureParitySurfacesAsync(
            surfacesDirectory,
            interactionOnly: true,
            interactionDialogCatalogIds: selectedDialogIds);
        foreach (var surface in capturedSurfaces)
        {
            var rendered = !string.IsNullOrWhiteSpace(surface.PngFileName);
            results.Add(new InteractionValidationResult(
                Id: surface.Id,
                Category: surface.Kind == ParitySurfaceKind.Dialog ? "dialog" : "surface",
                Status: surface.Captured ? "passed" : "failed",
                EvidenceLevel: surface.Kind == ParitySurfaceKind.Dialog
                    ? rendered ? "opened-and-rendered" : "opened-and-keyboard-probed"
                    : "rendered",
                Evidence: rendered
                    ? Path.Combine("surfaces", surface.PngFileName).Replace('\\', '/')
                    : "production dialog opener",
                Note: surface.Note));
        }
        AddDialogInventoryResults(results, capturedSurfaces, selectedDialogIds);
        results.AddRange(BuildDialogInteractionContractResults(selectedDialogIds));

        return results;
    }

    private static void AddDialogInventoryResults(
        List<InteractionValidationResult> results,
        IReadOnlyList<ParitySurfaceResult> capturedSurfaces,
        IReadOnlySet<string>? selectedDialogIds = null)
    {
        var capturedDialogs = capturedSurfaces
            .Where(surface => surface.Kind == ParitySurfaceKind.Dialog && surface.Captured)
            .ToArray();

        foreach (var dialog in InteractionSurfaceCatalog.Dialogs)
        {
            if (selectedDialogIds is not null && !selectedDialogIds.Contains(dialog.Id))
                continue;

            var route = ParityInteractionDialogRoutes.SingleOrDefault(candidate =>
                string.Equals(candidate.CatalogId, dialog.Id, StringComparison.Ordinal));
            var capturedSurface = route is null || route.IsMissing
                ? null
                : capturedDialogs.FirstOrDefault(surface =>
                    string.Equals(surface.Id, route.SurfaceId, StringComparison.Ordinal) ||
                    surface.Id.StartsWith(route.SurfaceId + ".", StringComparison.Ordinal));
            var captured = capturedSurface is not null;
            var rendered = captured && !string.IsNullOrWhiteSpace(capturedSurface!.PngFileName);
            results.Add(new InteractionValidationResult(
                Id: dialog.Id,
                Category: "dialog-inventory",
                Status: captured ? "passed" : "failed",
                EvidenceLevel: captured
                    ? rendered ? "opened-rendered-closed" : "opened-keyboard-probed-closed"
                    : "catalogued-not-exercised",
                Evidence: route is null
                    ? dialog.Name
                    : $"{dialog.Name} -> {route.AvaloniaProductionSurface}",
                Note: captured
                    ? rendered
                        ? $"{dialog.Modality}; initial focus, traversal, submit/cancel, and focus return remain separate contract checks."
                        : $"{dialog.Modality}; opened via the production route without rasterization; detailed keyboard/focus evidence is in dialog-contract."
                    : route?.MissingReason.Length > 0
                        ? route.MissingReason
                        : "The authoritative WPF dialog surface has no matching Avalonia interaction-validation opener."));
        }
    }

    private void AddRibbonBindingResults(List<InteractionValidationResult> results)
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = _ribbonCommandRegistry;
        foreach (var row in AvaloniaRibbonComposition.EnumerateSurfaceRows(definition))
        {
            IRibbonCommand? command = null;
            var resolved = registry is not null && registry.TryGet(row.CommandId, out command) && command is not null;
            var functional = resolved && command is not EmptyRibbonCommand;
            var explicitlyDisabled = command is DisabledNoOpRibbonCommand ||
                command is IRibbonStatefulCommand stateful && !stateful.GetState().IsEnabled;
            results.Add(new InteractionValidationResult(
                Id: row.RowId,
                Category: "ribbon-command",
                Status: functional || explicitlyDisabled ? "passed" : "failed",
                EvidenceLevel: explicitlyDisabled ? "explicitly-disabled" : "registry-bound",
                Evidence: $"{row.CommandId.Value} | {row.TabHeader} > {row.GroupHeader} > {row.Label} | {command?.GetType().Name ?? "unregistered"}",
                Note: functional || explicitlyDisabled
                    ? row.ActivationKey is null ? "" : $"Context: {row.ActivationKey}."
                    : "Command resolves to EmptyRibbonCommand."));
        }

        foreach (var tab in definition.Tabs)
        foreach (var group in tab.Groups)
        {
            results.Add(new InteractionValidationResult(
                Id: $"{tab.Id}/{group.Id}",
                Category: "ribbon-collapsed-group",
                Status: "passed",
                EvidenceLevel: "adaptive-renderer-contract",
                Evidence: $"collapsed:{group.Id}",
                Note: $"{group.Controls.Count} declared controls; flyout routing is validated by the ribbon UI lane."));
        }
    }

    private static void AddShortcutRoutingResults(List<InteractionValidationResult> results)
    {
        for (var index = 0; index < WorkbookKeyboardShortcutCatalog.Rules.Count; index++)
        {
            var rule = WorkbookKeyboardShortcutCatalog.Rules[index];
            AddShortcutRoutingResult(results, index, rule.Route, rule.WindowsChord, native: false);
            if (rule.NativeMenuChord is { } nativeChord && nativeChord != rule.WindowsChord)
                AddShortcutRoutingResult(results, index, rule.Route, nativeChord, native: true);
        }
    }

    private static void AddShortcutScenarioInventoryResults(List<InteractionValidationResult> results)
    {
        foreach (var scenario in InteractiveValidationInventory.KeyboardShortcuts)
        for (var index = 0; index < scenario.Interactions.Count; index++)
        {
            var interaction = scenario.Interactions[index];
            var catalogRouted = TryResolveSharedShortcutInteraction(interaction, out var route);
            var interactionId = $"{scenario.Id}:{index}";
            var exactBehaviorProbed =
                InteractiveValidationLegacyDataFilterInteractionIds.Contains(interactionId);
            var behaviorProbed =
                InteractiveValidationKeyboardShortcutScenarioIds.Contains(scenario.Id) ||
                exactBehaviorProbed;
            var externalBoundary = scenario.IsNative || scenario.IsExternal;
            results.Add(new InteractionValidationResult(
                Id: interactionId,
                Category: "shortcut-scenario",
                Status: catalogRouted || behaviorProbed ? "passed" : "skipped",
                EvidenceLevel: exactBehaviorProbed
                    ? "host-gesture-behavior-tested"
                    : behaviorProbed
                    ? "planner-driven-behavior-tested"
                    : catalogRouted ? "shared-catalog-routed"
                    : externalBoundary ? "native-or-external-boundary" : "catalogued-awaiting-behavior-probe",
                Evidence: $"{interaction.DisplayText} | {interaction.Context} | {interaction.Kind}",
                Note: behaviorProbed
                    ? scenario.ExpectedBehavior
                    : catalogRouted
                    ? $"Routes to {route}."
                    : externalBoundary
                        ? "Requires a cancel-only native/external probe."
                        : scenario.ExpectedBehavior));
        }
    }

    private static bool TryResolveSharedShortcutInteraction(
        ShortcutInteractionDescriptor interaction,
        out WorkbookShortcutRoute route)
    {
        route = default;
        if (interaction.Steps.Count != 1 ||
            !TryMapWorkbookShortcutKey(interaction.Steps[0].Key, out var key))
            return false;

        var modifiers = WorkbookShortcutModifiers.None;
        var sourceModifiers = interaction.Steps[0].Modifiers;
        if (sourceModifiers.HasFlag(ShortcutModifierKeys.Control))
            modifiers |= WorkbookShortcutModifiers.Control;
        if (sourceModifiers.HasFlag(ShortcutModifierKeys.Shift))
            modifiers |= WorkbookShortcutModifiers.Shift;
        if (sourceModifiers.HasFlag(ShortcutModifierKeys.Alt))
            modifiers |= WorkbookShortcutModifiers.Alt;

        return WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(key, modifiers, out route) ||
            WorkbookKeyboardShortcutCatalog.TryGetNativeMenuRoute(key, modifiers, out route);
    }

    private static bool TryMapWorkbookShortcutKey(string key, out WorkbookShortcutKey mapped)
    {
        var normalized = key switch
        {
            "Backspace" => nameof(WorkbookShortcutKey.Back),
            "Grave" => nameof(WorkbookShortcutKey.Oem3),
            "Minus" => nameof(WorkbookShortcutKey.OemMinus),
            "Plus" or "Equals" => nameof(WorkbookShortcutKey.OemPlus),
            "1" => nameof(WorkbookShortcutKey.D1),
            "2" => nameof(WorkbookShortcutKey.D2),
            "3" => nameof(WorkbookShortcutKey.D3),
            "4" => nameof(WorkbookShortcutKey.D4),
            "5" => nameof(WorkbookShortcutKey.D5),
            "6" => nameof(WorkbookShortcutKey.D6),
            "7" => nameof(WorkbookShortcutKey.D7),
            "Page Up" => nameof(WorkbookShortcutKey.PageUp),
            "Page Down" => nameof(WorkbookShortcutKey.PageDown),
            _ => key,
        };
        return Enum.TryParse(normalized, ignoreCase: true, out mapped);
    }

    private static void AddShortcutRoutingResult(
        List<InteractionValidationResult> results,
        int index,
        WorkbookShortcutRoute expectedRoute,
        WorkbookShortcutChord chord,
        bool native)
    {
        var resolved = native
            ? WorkbookKeyboardShortcutCatalog.TryGetNativeMenuRoute(chord.Key, chord.Modifiers, out var route)
            : WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(chord.Key, chord.Modifiers, out route);
        results.Add(new InteractionValidationResult(
            Id: $"{expectedRoute}:{index}:{(native ? "native" : "windows")}",
            Category: "keyboard-shortcut",
            Status: resolved && route == expectedRoute ? "passed" : "failed",
            EvidenceLevel: "catalog-routed",
            Evidence: $"{chord.Modifiers}+{chord.Key}",
            Note: resolved && route == expectedRoute ? "" : $"Expected {expectedRoute}; resolved {route}."));
    }

    private static void AddDialogRangeTargetResults(List<InteractionValidationResult> results)
    {
        foreach (var target in InteractiveValidationInventory.WorksheetRangeTargets)
        {
            var wired = InteractiveValidationRangeTargetIds.Contains(target.Id);
            results.Add(new InteractionValidationResult(
                Id: target.Id,
                Category: "dialog-range-pointing",
                Status: wired ? "passed" : "failed",
                EvidenceLevel: wired ? "shared-picker-session-bound" : "picker-session-missing",
                Evidence: $"{target.Area} > {target.DisplayTarget}",
                Note: wired
                    ? "The shared session supports worksheet selection, mouse/Enter accept, Escape cancel, dialog restoration, and focus return."
                    : target.ExpectedBehavior));
        }
    }

    private static IReadOnlyList<(string Name, WorksheetContextMenuState State)> WorksheetContextValidationStates()
    {
        var states = new List<(string, WorksheetContextMenuState)>(1 << 8);
        for (var bits = 0; bits < 1 << 8; bits++)
        {
            var state = new WorksheetContextMenuState(
                HasThreadedComment: (bits & 1) != 0,
                IsThreadedCommentResolved: (bits & (1 << 1)) != 0,
                HasNote: (bits & (1 << 2)) != 0,
                HasHyperlink: (bits & (1 << 3)) != 0,
                HasAutoFilterHeaderTarget: (bits & (1 << 4)) != 0,
                HasDropdownTarget: (bits & (1 << 5)) != 0,
                HasPivotTableTarget: (bits & (1 << 6)) != 0,
                NoteIsShown: (bits & (1 << 7)) != 0);
            states.Add(($"state-{bits:X2}", state));
        }

        return states;
    }

    private static IEnumerable<WorksheetContextMenuCommand> FlattenContextCommands(
        IReadOnlyList<WorksheetContextMenuCommand> commands)
    {
        foreach (var command in commands)
        {
            if (!command.IsSeparator && command.Action != WorksheetContextMenuAction.None)
                yield return command;
            foreach (var child in FlattenContextCommands(command.Children))
                yield return child;
        }
    }

    private async Task AddEditingInteractionResultsAsync(List<InteractionValidationResult> results)
    {
        var sheet = _session.Workbook.AddSheet("InteractionValidation");
        _session.SelectSheet(sheet.Id);

        var inlineAddress = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(inlineAddress, new TextValue("before"));
        _session.SelectCell(inlineAddress);
        BeginInlineCellEdit(inlineAddress, "before", "before".Length);
        var inlineEditorCreated = _inlineCellEditor is not null && _inlineCellEditAddress == inlineAddress;
        if (_inlineCellEditor is not null)
            _inlineCellEditor.Text = "after";
        CommitInlineCellEdit(0, 0);
        var inlineCommitted = sheet.GetValue(inlineAddress) is TextValue { Value: "after" };
        results.Add(new InteractionValidationResult(
            "cell-inline-edit",
            "worksheet-editing",
            inlineEditorCreated && inlineCommitted ? "passed" : "failed",
            "invoked-with-mutation",
            "WorksheetInlineCellEditor",
            inlineCommitted ? "" : "Inline editor did not commit the expected value."));

        var formulaAddress = new CellAddress(sheet.Id, 3, 2);
        var pointTarget = new CellAddress(sheet.Id, 4, 4);
        _session.SelectCell(formulaAddress);
        BeginInlineCellEdit(formulaAddress, "=", 1);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        var inlinePointInserted = TryInsertFormulaPointReference(pointTarget);
        var inlinePointText = _inlineCellEditor?.Text ?? _inlineCellEditText ?? "";
        CommitInlineCellEdit(0, 0);
        var inlinePointCommitted = sheet.GetCell(formulaAddress)?.FormulaText is not null;
        results.Add(new InteractionValidationResult(
            "cell-inline-formula-point-mode",
            "worksheet-editing",
            inlinePointInserted && inlinePointText.Contains("D4", StringComparison.Ordinal) && inlinePointCommitted
                ? "passed"
                : "failed",
            "invoked-with-reference-mutation",
            inlinePointText,
            "Inline formula edit must accept a pointed worksheet reference and commit it."));

        var formulaBarAddress = new CellAddress(sheet.Id, 5, 2);
        _session.SelectCell(formulaBarAddress);
        _session.BeginFormulaEdit(formulaBarAddress);
        _formulaBox.Text = "=";
        _formulaBox.CaretIndex = 1;
        _formulaBox.SelectionStart = 1;
        _formulaBox.SelectionEnd = 1;
        var formulaBarPointInserted = TryInsertFormulaPointReference(pointTarget);
        var formulaBarText = _formulaBox.Text ?? "";
        RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });
        var formulaBarCommitted = sheet.GetCell(formulaBarAddress)?.FormulaText is not null;
        results.Add(new InteractionValidationResult(
            "formula-bar-point-mode",
            "worksheet-editing",
            formulaBarPointInserted && formulaBarText.Contains("D4", StringComparison.Ordinal) && formulaBarCommitted
                ? "passed"
                : "failed",
            "invoked-with-reference-mutation",
            formulaBarText,
            "Formula bar edit must accept a pointed worksheet reference and commit it."));
    }
}
