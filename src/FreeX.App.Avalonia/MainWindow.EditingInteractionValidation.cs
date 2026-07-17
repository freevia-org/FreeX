using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal async Task<IReadOnlyList<InteractionValidationResult>> RunEditingInteractionValidationForTestAsync()
    {
        var results = new List<InteractionValidationResult>();
        await AddPhysicalEditingInteractionResultsAsync(results);
        return results;
    }

    private async Task AddPhysicalEditingInteractionResultsAsync(List<InteractionValidationResult> results)
    {
        var sheet = _session.Workbook.AddSheet("InteractionValidation");
        _session.SelectSheet(sheet.Id);

        var inlineAddress = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(inlineAddress, new TextValue("before"));
        _session.SelectCell(inlineAddress);
        RefreshShell("Ready");
        await RaiseEditingValidationKeyAsync(ResolveWorksheetValidationKeyTarget(), Key.F2);
        var inlineEditorCreated = _inlineCellEditor is not null && _inlineCellEditAddress == inlineAddress;
        if (_inlineCellEditor is { } inlineEditor)
        {
            inlineEditor.SelectAll();
            RaiseEditingValidationTextInput(inlineEditor, "after");
            await RaiseEditingValidationKeyAsync(inlineEditor, Key.Enter);
        }

        var inlineCommitted = sheet.GetValue(inlineAddress) is TextValue { Value: "after" };
        results.Add(new InteractionValidationResult(
            "cell-inline-edit",
            "worksheet-editing",
            inlineEditorCreated && inlineCommitted ? "passed" : "failed",
            "routed-f2-textinput-enter",
            $"editor={inlineEditorCreated}; committed={inlineCommitted}",
            "Routed F2 opened the production inline editor; TextInput replaced the value and routed Enter committed it."));

        var formulaAddress = new CellAddress(sheet.Id, 3, 2);
        var pointTarget = new CellAddress(sheet.Id, 4, 4);
        _session.SelectCell(formulaAddress);
        RefreshShell("Ready");
        await RaiseEditingValidationKeyAsync(ResolveWorksheetValidationKeyTarget(), Key.F2);
        var inlinePointModeStarted = false;
        var inlineModeToggled = false;
        var inlinePointInserted = false;
        var inlinePointText = "";
        if (_inlineCellEditor is { } formulaEditor)
        {
            formulaEditor.SelectAll();
            RaiseEditingValidationTextInput(formulaEditor, "=");
            inlinePointModeStarted = _formulaRangeEntryMode;
            await RaiseEditingValidationKeyAsync(formulaEditor, Key.F2);
            var editMode = !_formulaRangeEntryMode;
            await RaiseEditingValidationKeyAsync(formulaEditor, Key.F2);
            inlineModeToggled = editMode && _formulaRangeEntryMode;
            inlinePointInserted = TryInsertFormulaPointReference(pointTarget);
            inlinePointText = formulaEditor.Text ?? _inlineCellEditText ?? "";
            await RaiseEditingValidationKeyAsync(formulaEditor, Key.Enter);
        }

        var inlinePointCommitted = sheet.GetCell(formulaAddress)?.FormulaText is not null;
        results.Add(new InteractionValidationResult(
            "cell-inline-formula-edit-point-mode",
            "worksheet-editing",
            inlinePointModeStarted && inlineModeToggled && inlinePointInserted &&
            inlinePointText.Contains("D4", StringComparison.Ordinal) && inlinePointCommitted
                ? "passed"
                : "failed",
            "routed-f2-textinput-mode-toggle-cell-point-enter",
            $"pointStarted={inlinePointModeStarted}; toggled={inlineModeToggled}; inserted={inlinePointInserted}; text={inlinePointText}; committed={inlinePointCommitted}",
            "The production inline editor entered Point mode from '=', toggled Edit/Point with F2, accepted a grid reference, and committed with Enter."));

        var formulaBarAddress = new CellAddress(sheet.Id, 5, 2);
        _session.SelectCell(formulaBarAddress);
        RefreshShell("Ready");
        _formulaBox.Focus();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        _formulaBox.SelectAll();
        RaiseEditingValidationTextInput(_formulaBox, "=");
        var formulaBarPointModeStarted = _formulaRangeEntryMode;
        await RaiseEditingValidationKeyAsync(_formulaBox, Key.F2);
        var formulaBarEditMode = !_formulaRangeEntryMode;
        await RaiseEditingValidationKeyAsync(_formulaBox, Key.F2);
        var formulaBarModeToggled = formulaBarEditMode && _formulaRangeEntryMode;
        var formulaBarPointInserted = TryInsertFormulaPointReference(pointTarget);
        var formulaBarText = _formulaBox.Text ?? "";
        await RaiseEditingValidationKeyAsync(_formulaBox, Key.Enter);
        var formulaBarCommitted = sheet.GetCell(formulaBarAddress)?.FormulaText is not null;
        results.Add(new InteractionValidationResult(
            "formula-bar-edit-point-mode",
            "worksheet-editing",
            formulaBarPointModeStarted && formulaBarModeToggled && formulaBarPointInserted &&
            formulaBarText.Contains("D4", StringComparison.Ordinal) && formulaBarCommitted
                ? "passed"
                : "failed",
            "focus-textinput-mode-toggle-cell-point-enter",
            $"pointStarted={formulaBarPointModeStarted}; toggled={formulaBarModeToggled}; inserted={formulaBarPointInserted}; text={formulaBarText}; committed={formulaBarCommitted}",
            "The production formula bar entered edit/Point mode through focus and TextInput, accepted a grid reference, and committed with routed Enter."));
    }

    private InputElement ResolveWorksheetValidationKeyTarget() =>
        (InputElement?)_activeCellBorder ?? _sheetGridHost;

    private static void RaiseEditingValidationTextInput(InputElement target, string text)
    {
        target.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
            Source = target,
        });
    }

    private static async Task RaiseEditingValidationKeyAsync(
        InputElement target,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var physicalKey = key switch
        {
            Key.F2 => PhysicalKey.F2,
            Key.Enter => PhysicalKey.Enter,
            Key.Escape => PhysicalKey.Escape,
            _ => PhysicalKey.None,
        };
        KeyEventArgs Create(RoutedEvent routedEvent) => new()
        {
            RoutedEvent = routedEvent,
            Key = key,
            KeyModifiers = modifiers,
            PhysicalKey = physicalKey,
            KeyDeviceType = KeyDeviceType.Keyboard,
            Source = target,
        };

        target.RaiseEvent(Create(InputElement.KeyDownEvent));
        if (target.IsAttachedToVisualTree())
            target.RaiseEvent(Create(InputElement.KeyUpEvent));
        await Task.Delay(75);
    }
}
