using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Presentation.Interactions;

namespace FreeX.App.Avalonia;

internal sealed record DialogInteractionContractEvidence(
    string SurfaceId,
    string ActualModality,
    string InitialFocus,
    string TabForward,
    string TabBackward,
    string EscapeCancel,
    string OwnerFocusRestore,
    string DefaultEnter,
    bool HasFailure);

public sealed partial class MainWindow
{
    internal static Func<Window, Key, RawInputModifiers, string?>? DialogKeySenderOverride { get; set; }
    private readonly Dictionary<string, DialogInteractionContractEvidence> _dialogInteractionContracts =
        new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, DialogInteractionContractEvidence> DialogInteractionContracts =>
        _dialogInteractionContracts;

    private void ResetDialogInteractionContracts()
    {
        _dialogInteractionContracts.Clear();
        ResetDialogRangeInteractionContracts();
    }

    private IInputElement? PrepareOwnerFocusForDialogContract()
    {
        var focused = FocusManager?.GetFocusedElement();
        if (focused is not null)
            return focused;

        _sheetGridHost.Focus();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        return FocusManager?.GetFocusedElement();
    }

    private async Task RecordDialogInteractionContractAsync(
        string surfaceId,
        Window dialog,
        IInputElement? ownerFocusBeforeOpen,
        Task openerTask,
        Func<Task> opener)
    {
        if (_dialogInteractionContracts.ContainsKey(surfaceId))
            return;

        await SettleDialogInteractionAsync();
        var actualModality = openerTask.IsCompleted ? "modeless" : "modal";
        var initial = dialog.FocusManager?.GetFocusedElement();
        var initialFocus = IsFocusInside(dialog, initial)
            ? "passed:" + DescribeInputElement(initial)
            : "failed:no-focus-inside-dialog";

        var tabStops = CountDialogTabStops(dialog);
        var forward = await ExerciseTabCycleAsync(dialog, reverse: false, tabStops);
        var backward = await ExerciseTabCycleAsync(dialog, reverse: true, tabStops);
        await RecordDialogRangeInteractionContractsAsync(dialog);

        var defaultButton = FindDefaultButton(dialog);
        var defaultEnter = defaultButton is null
            ? "classified:no-default-button"
            : IsSafeDefaultAction(surfaceId, defaultButton)
                ? "pending:safe-default"
                : "classified:not-invoked-mutation-risk:" + DescribeButton(defaultButton);

        var escape = await ExerciseEscapeAsync(dialog);
        if (dialog.IsVisible)
        {
            try { dialog.Close(); } catch { /* best-effort cleanup after a failed Escape contract */ }
        }
        await AwaitOpenerQuietlyAsync(openerTask);
        await SettleDialogInteractionAsync();

        var ownerFocusAfterClose = FocusManager?.GetFocusedElement();
        var ownerRestore = ownerFocusBeforeOpen is null
            ? "classified:no-owner-focus-before-open"
            : ReferenceEquals(ownerFocusBeforeOpen, ownerFocusAfterClose)
                ? "passed:" + DescribeInputElement(ownerFocusAfterClose)
                : IsFocusInside(this, ownerFocusAfterClose)
                    ? "passed:owner-active:expected=" + DescribeInputElement(ownerFocusBeforeOpen) +
                      ",actual=" + DescribeInputElement(ownerFocusAfterClose)
                    : "failed:expected-owner-focus-from=" + DescribeInputElement(ownerFocusBeforeOpen) +
                      ",actual=" + DescribeInputElement(ownerFocusAfterClose);

        if (defaultButton is not null && IsSafeDefaultAction(surfaceId, defaultButton))
        {
            defaultEnter = await ExerciseSafeDefaultEnterAsync(surfaceId, opener);
        }

        var hasFailure = IsFailed(initialFocus) || IsFailed(forward) || IsFailed(backward) ||
            IsFailed(escape) || IsFailed(ownerRestore) || IsFailed(defaultEnter);
        _dialogInteractionContracts[surfaceId] = new DialogInteractionContractEvidence(
            surfaceId,
            actualModality,
            initialFocus,
            forward,
            backward,
            escape,
            ownerRestore,
            defaultEnter,
            hasFailure);
    }

    private void RecordDialogInteractionOpenFailure(string surfaceId, string reason)
    {
        _dialogInteractionContracts.TryAdd(
            surfaceId,
            new DialogInteractionContractEvidence(
                surfaceId,
                "unavailable",
                "failed:not-opened",
                "failed:not-opened",
                "failed:not-opened",
                "failed:not-opened",
                "failed:not-opened",
                "classified:not-opened:" + reason,
                HasFailure: true));
    }

    private static async Task<string> ExerciseTabCycleAsync(Window dialog, bool reverse, int tabStops)
    {
        var initial = dialog.FocusManager?.GetFocusedElement();
        if (!IsFocusInside(dialog, initial))
            return "failed:no-starting-focus";

        var visited = new List<IInputElement?> { initial };
        var maximumSteps = Math.Max(2, tabStops + 2);
        for (var step = 1; step <= maximumSteps; step++)
        {
            if (!TrySendDialogKey(
                    dialog,
                    Key.Tab,
                    reverse ? RawInputModifiers.Shift : RawInputModifiers.None,
                    out var inputError))
            {
                return "failed:routed-input-unavailable:" + inputError;
            }

            await SettleDialogInteractionAsync();
            var after = dialog.FocusManager?.GetFocusedElement();
            if (!IsFocusInside(dialog, after))
                return "failed:focus-left-dialog:step=" + step;

            if (ReferenceEquals(initial, after))
            {
                var distinctStops = visited.Count;
                if (distinctStops == 1 && tabStops > 1)
                    return "failed:focus-did-not-move:" + DescribeInputElement(after);
                return $"passed:full-cycle:steps={step},stops={distinctStops}";
            }

            if (!visited.Any(element => ReferenceEquals(element, after)))
                visited.Add(after);
        }

        return $"failed:focus-cycle-did-not-wrap:steps={maximumSteps},stops={visited.Count}";
    }

    private static async Task<string> ExerciseEscapeAsync(Window dialog)
    {
        if (!TrySendDialogKey(dialog, Key.Escape, RawInputModifiers.None, out var inputError))
            return "failed:routed-input-unavailable:" + inputError;
        for (var attempt = 0; attempt < 4 && dialog.IsVisible; attempt++)
            await SettleDialogInteractionAsync();
        return dialog.IsVisible ? "failed:escape-did-not-close" : "passed:closed-by-escape";
    }

    private async Task<string> ExerciseSafeDefaultEnterAsync(string surfaceId, Func<Task> opener)
    {
        var preexisting = OwnedWindows.ToHashSet();
        var openerTask = RunParityModalOpenerAsync(opener);
        var dialog = await WaitForOwnedDialogAsync(preexisting);
        if (dialog is null)
        {
            await AwaitOpenerQuietlyAsync(openerTask);
            return "failed:safe-default-reopen-failed";
        }

        var clicked = false;
        try
        {
            await SettleDialogInteractionAsync();
            var defaultButton = FindDefaultButton(dialog);
            if (defaultButton is null)
                return "failed:default-button-missing-on-reopen";
            defaultButton.Click += (_, _) => clicked = true;
            if (!TrySendDialogKey(dialog, Key.Enter, RawInputModifiers.None, out var inputError))
                return "failed:routed-input-unavailable:" + inputError;
            await SettleDialogInteractionAsync();
            return clicked
                ? "passed:invoked-nonmutating:" + DescribeButton(defaultButton)
                : "failed:enter-did-not-invoke-default:" + DescribeButton(defaultButton);
        }
        finally
        {
            if (dialog.IsVisible)
            {
                try { dialog.Close(); } catch { /* best-effort */ }
            }
            await AwaitOpenerQuietlyAsync(openerTask);
        }
    }

    private static bool TrySendDialogKey(
        Window dialog,
        Key key,
        RawInputModifiers modifiers,
        out string error)
    {
        if (DialogKeySenderOverride is { } senderOverride)
        {
            error = senderOverride(dialog, key, modifiers) ?? "";
            return error.Length == 0;
        }

        try
        {
            var target = dialog.FocusManager?.GetFocusedElement() as InputElement ?? dialog;
            var physicalKey = key switch
            {
                Key.Tab => PhysicalKey.Tab,
                Key.Enter => PhysicalKey.Enter,
                Key.Escape => PhysicalKey.Escape,
                _ => PhysicalKey.None,
            };
            var keyModifiers = KeyModifiers.None;
            if ((modifiers & RawInputModifiers.Shift) != 0)
                keyModifiers |= KeyModifiers.Shift;
            if ((modifiers & RawInputModifiers.Control) != 0)
                keyModifiers |= KeyModifiers.Control;
            if ((modifiers & RawInputModifiers.Alt) != 0)
                keyModifiers |= KeyModifiers.Alt;
            if ((modifiers & RawInputModifiers.Meta) != 0)
                keyModifiers |= KeyModifiers.Meta;

            KeyEventArgs CreateKeyEvent(RoutedEvent routedEvent) => new()
            {
                RoutedEvent = routedEvent,
                Key = key,
                KeyModifiers = keyModifiers,
                PhysicalKey = physicalKey,
                KeyDeviceType = KeyDeviceType.Keyboard,
                Source = target,
            };

            target.RaiseEvent(CreateKeyEvent(InputElement.KeyDownEvent));
            if (dialog.IsVisible)
                target.RaiseEvent(CreateKeyEvent(InputElement.KeyUpEvent));

            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ":" + ex.Message;
            return false;
        }
    }

    private static Task SettleDialogInteractionAsync() => Task.Delay(75);

    internal static bool SendDialogKeyForTest(
        Window dialog,
        Key key,
        RawInputModifiers modifiers,
        out string error) =>
        TrySendDialogKey(dialog, key, modifiers, out error);

    private static int CountDialogTabStops(Window dialog) =>
        dialog.GetVisualDescendants()
            .OfType<Control>()
            .Count(control => control.Focusable && control.IsVisible && control.IsEffectivelyEnabled);

    private static Button? FindDefaultButton(Window dialog) =>
        dialog.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.IsDefault && button.IsVisible && button.IsEffectivelyEnabled);

    private static bool IsSafeDefaultAction(string surfaceId, Button button)
    {
        if (button.IsCancel)
            return true;

        var label = DescribeButton(button).ToLowerInvariant();
        if (label.Contains("close", StringComparison.Ordinal) ||
            label.Contains("cancel", StringComparison.Ordinal) ||
            label.Contains("dismiss", StringComparison.Ordinal))
        {
            return true;
        }

        return surfaceId is "dialog.About" or "dialog.LegalNotices" or
            "dialog.WorkbookStatistics" or "dialog.GoalSeekStatus";
    }

    private static bool IsFocusInside(Window dialog, IInputElement? element) =>
        element is Visual visual && ReferenceEquals(TopLevel.GetTopLevel(visual), dialog);

    private static string DescribeInputElement(IInputElement? element)
    {
        if (element is null)
            return "none";
        if (element is Control control)
        {
            var automationId = AutomationProperties.GetAutomationId(control);
            if (!string.IsNullOrWhiteSpace(automationId))
                return control.GetType().Name + "#" + automationId;
            if (!string.IsNullOrWhiteSpace(control.Name))
                return control.GetType().Name + "#" + control.Name;
        }
        return element.GetType().Name;
    }

    private static string DescribeButton(Button button) =>
        button.Content?.ToString() is { Length: > 0 } label
            ? label
            : DescribeInputElement(button);

    private static bool IsFailed(string value) => value.StartsWith("failed:", StringComparison.Ordinal);

    internal IReadOnlyList<InteractionValidationResult> BuildDialogInteractionContractResults(
        IReadOnlySet<string>? selectedDialogIds = null)
    {
        var results = new List<InteractionValidationResult>(InteractiveValidationDialogRouteCount);
        foreach (var dialog in InteractionSurfaceCatalog.Dialogs)
        {
            if (selectedDialogIds is not null && !selectedDialogIds.Contains(dialog.Id))
                continue;

            var route = ParityInteractionDialogRoutes.Single(candidate =>
                string.Equals(candidate.CatalogId, dialog.Id, StringComparison.Ordinal));
            var contract = FindDialogInteractionContract(route.SurfaceId);
            if (contract is null)
            {
                results.Add(new InteractionValidationResult(
                    Id: dialog.Id,
                    Category: "dialog-contract",
                    Status: "failed",
                    EvidenceLevel: "catalogued-not-exercised",
                    Evidence: $"{dialog.Name} -> {route.AvaloniaProductionSurface}",
                    Note: "No production dialog keyboard/focus contract was observed during capture."));
                continue;
            }

            var expectedModality = dialog.Modality.ToString().ToLowerInvariant();
            var modalityMatches = string.Equals(expectedModality, contract.ActualModality, StringComparison.Ordinal);
            var failed = contract.HasFailure || !modalityMatches;
            results.Add(new InteractionValidationResult(
                Id: dialog.Id,
                Category: "dialog-contract",
                Status: failed ? "failed" : "passed",
                EvidenceLevel: "routed-keyboard-focus-contract",
                Evidence:
                    $"modality={contract.ActualModality}; initial={contract.InitialFocus}; " +
                    $"tab={contract.TabForward}; shift-tab={contract.TabBackward}; " +
                    $"escape={contract.EscapeCancel}; owner-focus={contract.OwnerFocusRestore}; " +
                    $"enter={contract.DefaultEnter}",
                Note: modalityMatches
                    ? $"Production opener: {route.AvaloniaProductionSurface}."
                    : $"Expected {expectedModality} from the authoritative catalog, observed {contract.ActualModality}; " +
                      $"production opener: {route.AvaloniaProductionSurface}."));
        }

        foreach (var route in SupplementalInteractionDialogRoutes)
        {
            if (selectedDialogIds is not null && !selectedDialogIds.Contains(route.CatalogId))
                continue;

            var contract = FindDialogInteractionContract(route.SurfaceId);
            if (contract is null)
            {
                results.Add(new InteractionValidationResult(
                    Id: route.CatalogId,
                    Category: "dialog-contract",
                    Status: "failed",
                    EvidenceLevel: "production-dialog-not-exercised",
                    Evidence: route.AvaloniaProductionSurface,
                    Note: "The production-only dialog surface did not produce a keyboard/focus contract."));
                continue;
            }

            var modalityMatches = string.Equals("modal", contract.ActualModality, StringComparison.Ordinal);
            results.Add(new InteractionValidationResult(
                Id: route.CatalogId,
                Category: "dialog-contract",
                Status: contract.HasFailure || !modalityMatches ? "failed" : "passed",
                EvidenceLevel: "routed-keyboard-focus-contract",
                Evidence:
                    $"modality={contract.ActualModality}; initial={contract.InitialFocus}; " +
                    $"tab={contract.TabForward}; shift-tab={contract.TabBackward}; " +
                    $"escape={contract.EscapeCancel}; owner-focus={contract.OwnerFocusRestore}; " +
                    $"enter={contract.DefaultEnter}",
                Note: modalityMatches
                    ? $"Supplemental production opener: {route.AvaloniaProductionSurface}."
                    : $"Expected modal supplemental production dialog, observed {contract.ActualModality}."));
        }

        return results;
    }

    private DialogInteractionContractEvidence? FindDialogInteractionContract(string routeSurfaceId)
    {
        if (_dialogInteractionContracts.TryGetValue(routeSurfaceId, out var exact))
            return exact;

        return _dialogInteractionContracts
            .Where(pair =>
                routeSurfaceId.StartsWith(pair.Key + ".", StringComparison.Ordinal) ||
                pair.Key.StartsWith(routeSurfaceId + ".", StringComparison.Ordinal))
            .OrderByDescending(pair => pair.Key.Length)
            .Select(pair => pair.Value)
            .FirstOrDefault();
    }
}
