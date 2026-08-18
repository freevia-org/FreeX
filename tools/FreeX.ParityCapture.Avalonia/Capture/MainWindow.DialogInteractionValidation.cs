using System.Reflection;
using System.Runtime.CompilerServices;

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
    string Ownership,
    string OpenerLifecycle,
    string OwnerInteractivity,
    string InitialFocus,
    string TabForward,
    string TabBackward,
    string EscapeCancel,
    string OwnerFocusRestore,
    string DefaultEnter,
    bool HasFailure);

public sealed partial class MainWindow
{
    private sealed class OwnedDialogKeyboardFocusState(
        MainWindow owner,
        IInputElement? ownerFocusBeforeOpen)
    {
        public MainWindow Owner { get; } = owner;
        public IInputElement? OwnerFocusBeforeOpen { get; } = ownerFocusBeforeOpen;
    }

    private static readonly ConditionalWeakTable<Window, OwnedDialogKeyboardFocusState>
        ConfiguredOwnedDialogKeyboardFocus = new();
    private static readonly bool OwnedDialogKeyboardFocusRegistered =
        RegisterOwnedDialogKeyboardFocus();
    internal static Func<Window, Key, RawInputModifiers, string?>? DialogKeySenderOverride { get; set; }
    private readonly Dictionary<string, DialogInteractionContractEvidence> _dialogInteractionContracts =
        new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, DialogInteractionContractEvidence> DialogInteractionContracts =>
        _dialogInteractionContracts;

    private static bool RegisterOwnedDialogKeyboardFocus()
    {
        Window.OwnerProperty.Changed.AddClassHandler<Window>(ConfigureOwnedDialogKeyboardFocus);
        return true;
    }

    private static void ConfigureOwnedDialogKeyboardFocus(
        Window dialog,
        AvaloniaPropertyChangedEventArgs _)
    {
        if (dialog.Owner is not MainWindow owner ||
            !ConfiguredOwnedDialogKeyboardFocus.TryAdd(
                dialog,
                new OwnedDialogKeyboardFocusState(owner, owner.FocusManager?.GetFocusedElement())))
        {
            return;
        }

        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);
        dialog.Opened += OwnedDialogOpened;
        dialog.Activated += OwnedDialogActivated;
        dialog.LayoutUpdated += OwnedDialogLayoutUpdated;
        dialog.KeyDown += OwnedDialogEscapeKeyDown;
        dialog.Closed += OwnedDialogClosed;
    }

    private static void OwnedDialogOpened(object? sender, EventArgs _)
    {
        if (sender is not Window dialog)
            return;

        QueueOwnedDialogInitialFocus(dialog);
    }

    private static void OwnedDialogActivated(object? sender, EventArgs _)
    {
        if (sender is Window dialog)
            QueueOwnedDialogInitialFocus(dialog);
    }

    private static void OwnedDialogLayoutUpdated(object? sender, EventArgs _)
    {
        if (sender is Window dialog)
            FocusFirstOwnedDialogControl(dialog);
    }

    private static void QueueOwnedDialogInitialFocus(Window dialog)
    {
        Dispatcher.UIThread.Post(
            () => FocusFirstOwnedDialogControl(dialog),
            DispatcherPriority.Background);
    }

    private static void FocusFirstOwnedDialogControl(Window dialog)
    {
        if (!dialog.IsVisible)
            return;

        var focused = dialog.FocusManager?.GetFocusedElement();
        if (IsFocusInside(dialog, focused))
        {
            dialog.LayoutUpdated -= OwnedDialogLayoutUpdated;
            return;
        }

        var firstControl = dialog.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control =>
                control.Focusable && control.IsVisible && control.IsEffectivelyEnabled);
        firstControl?.Focus();
        if (IsFocusInside(dialog, dialog.FocusManager?.GetFocusedElement()))
            dialog.LayoutUpdated -= OwnedDialogLayoutUpdated;
    }

    private static void OwnedDialogEscapeKeyDown(object? sender, KeyEventArgs args)
    {
        if (sender is not Window dialog ||
            !ConfiguredOwnedDialogKeyboardFocus.TryGetValue(dialog, out _) ||
            args.Handled ||
            args.Key != Key.Escape ||
            args.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var cancelButton = dialog.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button =>
                button.IsCancel && button.IsVisible && button.IsEffectivelyEnabled);
        cancelButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelButton));
        if (dialog.IsVisible)
            dialog.Close();
        args.Handled = true;
    }

    private static void OwnedDialogClosed(object? sender, EventArgs _)
    {
        if (sender is not Window dialog ||
            !ConfiguredOwnedDialogKeyboardFocus.TryGetValue(dialog, out var state))
        {
            return;
        }

        dialog.Opened -= OwnedDialogOpened;
        dialog.Activated -= OwnedDialogActivated;
        dialog.LayoutUpdated -= OwnedDialogLayoutUpdated;
        dialog.KeyDown -= OwnedDialogEscapeKeyDown;
        dialog.Closed -= OwnedDialogClosed;

        Dispatcher.UIThread.Post(
            () => RestoreOwnedDialogOwnerFocus(state),
            DispatcherPriority.Input);
    }

    private static void RestoreOwnedDialogOwnerFocus(OwnedDialogKeyboardFocusState state)
    {
        var owner = state.Owner;
        if (!owner.IsVisible)
            return;

        owner.Activate();
        if (state.OwnerFocusBeforeOpen is InputElement priorFocus &&
            priorFocus.Focusable && priorFocus.IsEffectivelyEnabled &&
            IsFocusInside(owner, priorFocus))
        {
            priorFocus.Focus();
            return;
        }

        owner._sheetGridHost.Focus();
    }

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
        var actualModality = dialog.IsDialog ? "modal" : "modeless";
        if (!dialog.IsDialog && !openerTask.IsCompleted)
            await Task.WhenAny(openerTask, Task.Delay(250));

        var ownership = ReferenceEquals(dialog.Owner, this)
            ? "passed:owned-by-main-window"
            : "failed:not-owned-by-main-window";
        var openerLifecycle = dialog.IsDialog
            ? openerTask.IsCompleted
                ? "failed:modal-opener-completed-while-open"
                : "passed:modal-opener-blocked-while-open"
            : openerTask.IsCompleted
                ? "passed:modeless-opener-completed-while-open"
                : "failed:modeless-opener-still-blocked-while-open";
        var ownerInteractivity = dialog.IsDialog
            ? "classified:modal-owner-input-blocked"
            : IsEffectivelyEnabled
                ? "passed:modeless-owner-enabled"
                : "failed:modeless-owner-disabled";
        var initial = dialog.FocusManager?.GetFocusedElement();
        // Describe initial focus at the same granularity the tab cycle uses. Focusing a list
        // delegates to its selected item, so the raw element is a ListBoxItem while the meaningful
        // stop -- and what WPF reports -- is the list itself. Without this the two halves of the
        // contract disagree about the same control.
        var initialFocus = IsFocusInside(dialog, initial)
            ? "passed:" + DescribeInputElement(NormalizeDialogTabStop(initial))
            : "failed:no-focus-inside-dialog";

        var tabStops = CountDialogTabStops(dialog);
        var forward = await ExerciseTabCycleAsync(dialog, reverse: false, tabStops);
        var backward = await ExerciseTabCycleAsync(dialog, reverse: true, tabStops);
        await RecordDialogRangeInteractionContractsAsync(dialog);
        await SettleDialogRangeInteractionBoundaryAsync(dialog);

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

        var hasFailure = IsFailed(ownership) || IsFailed(openerLifecycle) || IsFailed(ownerInteractivity) ||
            IsFailed(initialFocus) || IsFailed(forward) || IsFailed(backward) ||
            IsFailed(escape) || IsFailed(ownerRestore) || IsFailed(defaultEnter);
        _dialogInteractionContracts[surfaceId] = new DialogInteractionContractEvidence(
            surfaceId,
            actualModality,
            ownership,
            openerLifecycle,
            ownerInteractivity,
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
                "failed:not-opened",
                "failed:not-opened",
                "failed:not-opened",
                "classified:not-opened:" + reason,
                HasFailure: true));
    }

    private static async Task<string> ExerciseTabCycleAsync(Window dialog, bool reverse, int tabStops)
    {
        var initialFocus = dialog.FocusManager?.GetFocusedElement();
        if (!IsFocusInside(dialog, initialFocus))
            return "failed:no-starting-focus";

        var initial = NormalizeDialogTabStop(initialFocus);
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
            var afterFocus = dialog.FocusManager?.GetFocusedElement();
            if (!IsFocusInside(dialog, afterFocus))
                return "failed:focus-left-dialog:step=" + step;

            var after = NormalizeDialogTabStop(afterFocus);

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
        // Contract probe: keep the dialog alive past the inspection callback so Enter can reach it.
        var openerTask = RunParityModalOpenerAsync(opener, suppressInspectionAutoClose: true);
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
            var currentProperty = typeof(AvaloniaLocator).GetProperty(
                "Current",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var resolver = currentProperty?.GetValue(null);
            var getService = resolver?.GetType().GetMethod(
                "GetService",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: [typeof(Type)],
                modifiers: null);
            var inputManager = getService?.Invoke(resolver, [typeof(IInputManager)]) as IInputManager;
            var keyboard = getService?.Invoke(resolver, [typeof(IKeyboardDevice)]) as IKeyboardDevice;
            var inputRoot = typeof(TopLevel).GetProperty(
                    "InputRoot",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(dialog) as IInputRoot;
            var rawKeyConstructor = typeof(RawKeyEventArgs)
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SingleOrDefault(constructor => constructor.GetParameters().Length == 9);
            var processInput = inputManager?.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SingleOrDefault(method =>
                    method.Name.EndsWith("ProcessInput", StringComparison.Ordinal) &&
                    method.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(RawInputEventArgs));
            if (inputManager is null || keyboard is null || inputRoot is null ||
                rawKeyConstructor is null || processInput is null)
            {
                error = "Avalonia raw keyboard input services are unavailable.";
                return false;
            }

            var physicalKey = key switch
            {
                Key.Tab => PhysicalKey.Tab,
                Key.Enter => PhysicalKey.Enter,
                Key.Escape => PhysicalKey.Escape,
                _ => PhysicalKey.None,
            };
            var timestamp = unchecked((ulong)Environment.TickCount64);

            RawInputEventArgs CreateRawKeyEvent(RawKeyEventType eventType) =>
                (RawInputEventArgs)rawKeyConstructor.Invoke(
                [
                    keyboard,
                    timestamp,
                    inputRoot,
                    eventType,
                    key,
                    modifiers,
                    physicalKey,
                    null,
                    KeyDeviceType.Keyboard,
                ]);

            processInput.Invoke(inputManager, [CreateRawKeyEvent(RawKeyEventType.KeyDown)]);
            if (dialog.IsVisible)
                processInput.Invoke(inputManager, [CreateRawKeyEvent(RawKeyEventType.KeyUp)]);

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

    private static async Task SettleDialogRangeInteractionBoundaryAsync(Window dialog)
    {
        const int maximumAttempts = 4;
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            await SettleDialogInteractionAsync();
            if (!dialog.IsVisible || IsFocusInside(dialog, dialog.FocusManager?.GetFocusedElement()))
                return;
        }
    }

    internal static bool SendDialogKeyForTest(
        Window dialog,
        Key key,
        RawInputModifiers modifiers,
        out string error) =>
        TrySendDialogKey(dialog, key, modifiers, out error);

    private static int CountDialogTabStops(Window dialog) =>
        dialog.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Focusable && control.IsVisible && control.IsEffectivelyEnabled)
            .Select(NormalizeDialogTabStop)
            .Distinct()
            .Count();

    private static IInputElement NormalizeDialogTabStop(IInputElement? element)
    {
        if (element is not Control control)
            return element!;

        return control.GetVisualAncestors().OfType<ListBox>().FirstOrDefault() ?? control;
    }

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
                    $"modality={contract.ActualModality}; ownership={contract.Ownership}; " +
                    $"opener={contract.OpenerLifecycle}; owner={contract.OwnerInteractivity}; " +
                    $"initial={contract.InitialFocus}; " +
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
                    $"modality={contract.ActualModality}; ownership={contract.Ownership}; " +
                    $"opener={contract.OpenerLifecycle}; owner={contract.OwnerInteractivity}; " +
                    $"initial={contract.InitialFocus}; " +
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
