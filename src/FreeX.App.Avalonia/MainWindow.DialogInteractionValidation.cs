using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Presentation.Interactions;
using System.Reflection;

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

    private void ResetDialogInteractionContracts() => _dialogInteractionContracts.Clear();

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
        var forward = await ExerciseTabAsync(dialog, reverse: false, tabStops);
        var backward = await ExerciseTabAsync(dialog, reverse: true, tabStops);

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
                : "failed:expected=" + DescribeInputElement(ownerFocusBeforeOpen) +
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

    private static async Task<string> ExerciseTabAsync(Window dialog, bool reverse, int tabStops)
    {
        var before = dialog.FocusManager?.GetFocusedElement();
        if (!IsFocusInside(dialog, before))
            return "failed:no-starting-focus";

        if (!TrySendRawDialogKey(
                dialog,
                Key.Tab,
                reverse ? RawInputModifiers.Shift : RawInputModifiers.None,
                out var inputError))
        {
            return "failed:raw-input-unavailable:" + inputError;
        }
        await SettleDialogInteractionAsync();
        var after = dialog.FocusManager?.GetFocusedElement();
        if (!IsFocusInside(dialog, after))
            return "failed:focus-left-dialog";
        if (tabStops <= 1)
            return "passed:single-tab-stop:" + DescribeInputElement(after);
        return ReferenceEquals(before, after)
            ? "failed:focus-did-not-move:" + DescribeInputElement(after)
            : "passed:" + DescribeInputElement(before) + "->" + DescribeInputElement(after);
    }

    private static async Task<string> ExerciseEscapeAsync(Window dialog)
    {
        if (!TrySendRawDialogKey(dialog, Key.Escape, RawInputModifiers.None, out var inputError))
            return "failed:raw-input-unavailable:" + inputError;
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
            if (!TrySendRawDialogKey(dialog, Key.Enter, RawInputModifiers.None, out var inputError))
                return "failed:raw-input-unavailable:" + inputError;
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

    private static bool TrySendRawDialogKey(
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
            // Avalonia 12 keeps raw-input construction behind PrivateApi in its reference assembly.
            // Reflection here intentionally reaches the framework's own input manager so validation
            // follows the same keyboard-device pipeline as X11/Win32 instead of calling Focus/Click.
            var locatorType = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator", throwOnError: true)!;
            var current = locatorType
                .GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;
            var getService = current.GetType().GetMethod(
                "GetService",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(Type)],
                modifiers: null)!;
            var inputManager = getService.Invoke(current, [typeof(IInputManager)])!;
            var keyboard = getService.Invoke(current, [typeof(IKeyboardDevice)])!;
            var rawKeyType = typeof(RawInputEventArgs).Assembly.GetType(
                "Avalonia.Input.Raw.RawKeyEventArgs",
                throwOnError: true)!;
            var constructor = rawKeyType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(ctor => ctor.GetParameters().Length == 9);
            var processInput = inputManager.GetType().GetMethod(
                "ProcessInput",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(RawInputEventArgs)],
                modifiers: null)!;
            var physicalKey = key switch
            {
                Key.Tab => PhysicalKey.Tab,
                Key.Enter => PhysicalKey.Enter,
                Key.Escape => PhysicalKey.Escape,
                _ => PhysicalKey.None,
            };
            var timestamp = unchecked((ulong)Environment.TickCount64);
            foreach (var (eventType, eventTimestamp) in new[]
                     {
                         (RawKeyEventType.KeyDown, timestamp),
                         (RawKeyEventType.KeyUp, timestamp + 1),
                     })
            {
                var rawEvent = constructor.Invoke(
                [
                    keyboard,
                    eventTimestamp,
                    dialog,
                    eventType,
                    key,
                    modifiers,
                    physicalKey,
                    null,
                    KeyDeviceType.Keyboard,
                ]);
                processInput.Invoke(inputManager, [rawEvent]);
            }

            error = "";
            return true;
        }
        catch (Exception ex)
        {
            Exception cause = ex is TargetInvocationException { InnerException: not null } invocation
                ? invocation.InnerException!
                : ex;
            error = cause.GetType().Name + ":" + cause.Message;
            return false;
        }
    }

    private static Task SettleDialogInteractionAsync()
    {
        return SettleDialogInteractionCoreAsync();

        static async Task SettleDialogInteractionCoreAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
            await Task.Delay(75);
        }
    }

    internal static bool SendDialogKeyForTest(
        Window dialog,
        Key key,
        RawInputModifiers modifiers,
        out string error) =>
        TrySendRawDialogKey(dialog, key, modifiers, out error);

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
        var results = new List<InteractionValidationResult>(InteractionSurfaceCatalog.Dialogs.Count);
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
                EvidenceLevel: "raw-keyboard-focus-contract",
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
