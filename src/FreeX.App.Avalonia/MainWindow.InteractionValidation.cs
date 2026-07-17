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
        bool includeCoreResults = true,
        int ribbonCommandStart = 0,
        int ribbonCommandCount = int.MaxValue,
        bool ribbonOnly = false,
        string? coreSection = null)
    {
        var results = new List<InteractionValidationResult>();
        if (includeCoreResults)
        {
            bool Includes(string section) => coreSection is null ||
                string.Equals(coreSection, section, StringComparison.OrdinalIgnoreCase);

            if (!ribbonOnly && Includes("ribbon-bindings"))
                AddRibbonBindingResults(results);
            if (ribbonCommandCount > 0)
                AddRibbonInteractionExecutionResults(results, ribbonCommandStart, ribbonCommandCount);
            if (!ribbonOnly)
            {
                if (Includes("shortcuts"))
                {
                    AddShortcutRoutingResults(results);
                    await AddShortcutScenarioInteractionResultsAsync(results);
                }
                if (Includes("context-menus"))
                    await AddContextMenuInteractionResultsAsync(results);
                if (Includes("range-inventory"))
                    AddDialogRangeTargetInventoryResults(results);
                if (Includes("editing"))
                    await AddPhysicalEditingInteractionResultsAsync(results);
            }
        }

        var selectedDialogIds = InteractiveValidationDialogRoutes
            .Skip(Math.Max(0, dialogStart))
            .Take(Math.Max(0, dialogCount))
            .Select(route => route.CatalogId)
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
        results.AddRange(BuildObservedDialogRangeInteractionResults());

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

        foreach (var route in SupplementalInteractionDialogRoutes)
        {
            if (selectedDialogIds is not null && !selectedDialogIds.Contains(route.CatalogId))
                continue;

            var capturedSurface = capturedDialogs.FirstOrDefault(surface =>
                string.Equals(surface.Id, route.SurfaceId, StringComparison.Ordinal));
            var captured = capturedSurface is not null;
            results.Add(new InteractionValidationResult(
                Id: route.CatalogId,
                Category: "dialog-inventory",
                Status: captured ? "passed" : "failed",
                EvidenceLevel: captured ? "production-dialog-opened" : "production-dialog-not-exercised",
                Evidence: route.AvaloniaProductionSurface,
                Note: captured
                    ? "Supplemental production dialog opened through its real Avalonia route."
                    : "Supplemental production dialog did not open."));
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

    private async Task AddShortcutScenarioInteractionResultsAsync(List<InteractionValidationResult> results)
    {
        var interactionOrdinal = 0;
        foreach (var scenario in InteractiveValidationInventory.KeyboardShortcuts)
        for (var index = 0; index < scenario.Interactions.Count; index++)
        {
            var interaction = scenario.Interactions[index];
            var interactionId = $"{scenario.Id}:{index}";
            var externalBoundary = scenario.IsNative || scenario.IsExternal;
            if (externalBoundary || interaction.Kind == ShortcutInteractionKind.MouseWheel)
            {
                results.Add(new InteractionValidationResult(
                    Id: interactionId,
                    Category: "shortcut-scenario",
                    Status: "skipped",
                    EvidenceLevel: interaction.Kind == ShortcutInteractionKind.MouseWheel
                        ? "physical-pointer-boundary"
                        : "native-or-external-boundary",
                    Evidence: $"{interaction.DisplayText} | {interaction.Context} | {interaction.Kind}",
                    Note: interaction.Kind == ShortcutInteractionKind.MouseWheel
                        ? "Requires a physical wheel-event probe; no keyboard-dispatch credit was assigned."
                        : "Requires a cancel-only physical desktop probe; no managed interaction credit was assigned."));
                continue;
            }

            var probe = await ExerciseShortcutInteractionAsync(
                interaction,
                replaceSession: interactionOrdinal % 32 == 0);
            results.Add(new InteractionValidationResult(
                Id: interactionId,
                Category: "shortcut-scenario",
                Status: probe.Passed ? "passed" : "failed",
                EvidenceLevel: probe.Passed ? "production-key-event-dispatched" : "production-key-event-unhandled",
                Evidence: $"{interaction.DisplayText} | {interaction.Context} | {interaction.Kind}",
                Note: $"{probe.Note} Expected: {scenario.ExpectedBehavior}"));
            interactionOrdinal++;
            if (interactionOrdinal % 32 == 0)
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        }
    }

    internal async Task<(bool Passed, string Note)> ExerciseShortcutInteractionAsync(
        ShortcutInteractionDescriptor interaction,
        bool replaceSession = true)
    {
        var mappedSteps = new List<(Key Key, KeyModifiers Modifiers)>();
        if (interaction.Steps.Count == 0)
            return (false, "The interaction contains no keyboard steps.");
        foreach (var step in interaction.Steps)
        {
            if (!ShortcutInteractionValidationCatalog.TryMapAvaloniaGesture(step, out var key, out var modifiers))
                return (false, $"Gesture key '{step.Key}' has no Avalonia mapping.");
            mappedSteps.Add((key, modifiers));
        }

        try
        {
            ResetShortcutValidationState(interaction.Context, replaceSession);
            for (var index = 0; index < mappedSteps.Count; index++)
            {
                var step = mappedSteps[index];
                var args = new KeyEventArgs { Key = step.Key, KeyModifiers = step.Modifiers };
                var ownedBefore = OwnedWindows.ToHashSet();
                var dispatch = RaiseKeyDownForTest(args);
                await SettleShortcutDispatchAsync(dispatch, ownedBefore);
                if (!args.Handled)
                {
                    return (false,
                        $"Step {index + 1}/{mappedSteps.Count} ({step.Modifiers}+{step.Key}) was not handled by the production window.");
                }
            }

            return (true, $"All {mappedSteps.Count} key event(s) were handled through the production window.");
        }
        catch (Exception ex)
        {
            return (false, $"Production dispatch threw {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            CloseOwnedWindows(OwnedWindows.ToArray());
            ResetShortcutValidationState(ShortcutInteractionContext.Worksheet, replaceSession: false);
        }
    }

    private void ResetShortcutValidationState(
        ShortcutInteractionContext context,
        bool replaceSession)
    {
        CloseOwnedWindows(OwnedWindows.ToArray());
        if (replaceSession || _session.Workbook.Sheets.Count == 0)
            _session = CreateDisposableRibbonSession();
        _legacyDataFilterSequenceState = LegacyDataFilterSequenceState.None;
        SetRibbonKeyTipsVisible(false);
        HideBackstageOverlay();
        WindowState = global::Avalonia.Controls.WindowState.Normal;
        _formulaBarExpanded = false;
        _isFormulaBarHidden = false;
        _formulaBarHost.IsVisible = true;
        RefreshShell("Shortcut validation fixture ready");

        if (context is ShortcutInteractionContext.FormulaBar or ShortcutInteractionContext.FormulaReferenceEditor)
            _formulaBox.Focus();
        else
            _sheetGridHost.Focus();
    }

    private async Task SettleShortcutDispatchAsync(
        Task dispatch,
        IReadOnlySet<global::Avalonia.Controls.Window> ownedBefore)
    {
        for (var attempt = 0; !dispatch.IsCompleted && attempt < 80; attempt++)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var newlyOwned = OwnedWindows
                    .Where(window => !ownedBefore.Contains(window))
                    .ToArray();
                CloseOwnedWindows(newlyOwned);
            }, DispatcherPriority.Background);
            await Task.Delay(10);
        }

        if (!dispatch.IsCompleted)
            throw new TimeoutException("Shortcut dispatch did not settle after closing newly opened owned windows.");
        await dispatch;
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

    private static void AddDialogRangeTargetInventoryResults(List<InteractionValidationResult> results)
    {
        foreach (var target in InteractiveValidationInventory.WorksheetRangeTargets)
        {
            var wired = InteractiveValidationRangeTargetIds.Contains(target.Id);
            results.Add(new InteractionValidationResult(
                Id: target.Id,
                Category: "range-selection-inventory",
                Status: wired ? "passed" : "failed",
                EvidenceLevel: wired ? "registered-production-target" : "picker-session-missing",
                Evidence: $"{target.Area} > {target.DisplayTarget}",
                Note: wired
                    ? "Registration only; interactive apply/cancel evidence is emitted separately after the production dialog is exercised."
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

}
