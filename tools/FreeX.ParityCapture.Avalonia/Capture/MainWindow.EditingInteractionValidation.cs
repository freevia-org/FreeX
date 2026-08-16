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
        var inlineEditorStable = false;
        if (_inlineCellEditor is { } inlineEditor)
        {
            inlineEditor.SelectAll();
            await RaiseEditingValidationTextInputAsync(inlineEditor, "after");
            inlineEditorStable = ReferenceEquals(_inlineCellEditor, inlineEditor);
            if (_inlineCellEditor is { } currentInlineEditor)
                await RaiseEditingValidationKeyAsync(currentInlineEditor, Key.Enter);
        }

        var inlineCommitted = sheet.GetValue(inlineAddress) is TextValue { Value: "after" };
        results.Add(new InteractionValidationResult(
            "cell-inline-edit",
            "worksheet-editing",
            inlineEditorCreated && inlineEditorStable && inlineCommitted ? "passed" : "failed",
            "routed-f2-textinput-enter",
            $"editor={inlineEditorCreated}; stable={inlineEditorStable}; committed={inlineCommitted}",
            "Routed F2 opened the production inline editor; TextInput kept that editor attached, replaced the value, and routed Enter committed it."));

        var formulaAddress = new CellAddress(sheet.Id, 3, 2);
        var pointTarget = new CellAddress(sheet.Id, 4, 4);
        _session.SelectCell(formulaAddress);
        RefreshShell("Ready");
        await RaiseEditingValidationKeyAsync(ResolveWorksheetValidationKeyTarget(), Key.F2);
        var inlinePointModeStarted = false;
        var inlineModeToggled = false;
        var inlinePointInserted = false;
        var inlinePointEditorStable = false;
        var inlinePointText = "";
        if (_inlineCellEditor is { } formulaEditor)
        {
            formulaEditor.SelectAll();
            await RaiseEditingValidationTextInputAsync(formulaEditor, "=");
            inlinePointEditorStable = ReferenceEquals(_inlineCellEditor, formulaEditor);
            inlinePointModeStarted = _formulaRangeEditingSession.PointMode;
            await RaiseEditingValidationKeyAsync(_inlineCellEditor ?? formulaEditor, Key.F2);
            var editMode = !_formulaRangeEditingSession.PointMode;
            await RaiseEditingValidationKeyAsync(_inlineCellEditor ?? formulaEditor, Key.F2);
            inlineModeToggled = editMode && _formulaRangeEditingSession.PointMode;
            inlinePointInserted = TryInsertFormulaPointReference(pointTarget);
            inlinePointText = _inlineCellEditor?.Text ?? _inlineCellEditText ?? "";
            if (_inlineCellEditor is { } currentFormulaEditor)
                await RaiseEditingValidationKeyAsync(currentFormulaEditor, Key.Enter);
        }

        var inlinePointCommitted = sheet.GetCell(formulaAddress)?.FormulaText is not null;
        results.Add(new InteractionValidationResult(
            "cell-inline-formula-edit-point-mode",
            "worksheet-editing",
            inlinePointEditorStable && inlinePointModeStarted && inlineModeToggled && inlinePointInserted &&
            inlinePointText.Contains("D4", StringComparison.Ordinal) && inlinePointCommitted
                ? "passed"
                : "failed",
            "routed-f2-textinput-mode-toggle-cell-point-enter",
            $"stable={inlinePointEditorStable}; pointStarted={inlinePointModeStarted}; toggled={inlineModeToggled}; inserted={inlinePointInserted}; text={inlinePointText}; committed={inlinePointCommitted}",
            "The production inline editor stayed attached while entering Point mode from '=', toggled Edit/Point with F2, accepted a grid reference, and committed with Enter."));

        var formulaBarAddress = new CellAddress(sheet.Id, 5, 2);
        _session.SelectCell(formulaBarAddress);
        RefreshShell("Ready");
        _formulaBox.Focus();
        await SettleEditingInputAsync();
        _formulaBox.SelectAll();
        await RaiseEditingValidationTextInputAsync(_formulaBox, "=");
        var formulaBarPointModeStarted = _formulaRangeEditingSession.PointMode;
        await RaiseEditingValidationKeyAsync(_formulaBox, Key.F2);
        var formulaBarEditMode = !_formulaRangeEditingSession.PointMode;
        await RaiseEditingValidationKeyAsync(_formulaBox, Key.F2);
        var formulaBarModeToggled = formulaBarEditMode && _formulaRangeEditingSession.PointMode;
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

        var dragFormulaAddress = new CellAddress(sheet.Id, 6, 2);
        var dragStart = new CellAddress(sheet.Id, 2, 2);
        var dragEnd = new CellAddress(sheet.Id, 4, 4);
        _session.SelectCell(dragFormulaAddress);
        RefreshShell("Ready");
        await RaiseEditingValidationKeyAsync(ResolveWorksheetValidationKeyTarget(), Key.F2);
        var dragPointStarted = false;
        var dragAnchorReplayConsumed = false;
        var dragPointExtended = false;
        var dragPointEditSessionPreserved = false;
        var dragPointFocusRestored = false;
        var dragPointText = "";
        if (_inlineCellEditor is { } dragFormulaEditor)
        {
            dragFormulaEditor.SelectAll();
            await RaiseEditingValidationTextInputAsync(dragFormulaEditor, "=");
            dragPointStarted = TryInsertFormulaPointReference(dragStart);
            TrackFormulaPointDragAnchor(
                dragStart,
                _formulaRangeEditingSession.ReferenceSpan?.Start,
                _formulaRangeEditingSession.ReferenceSpan?.Length);
            dragAnchorReplayConsumed = TryContinueFormulaRangeSelectionDrag(dragStart);
            dragPointExtended = TryContinueFormulaRangeSelectionDrag(dragEnd);
            dragPointText = _inlineCellEditor?.Text ?? _inlineCellEditText ?? "";
            // Only FormulaEditAddress: while pointing, the selection (and with it ActiveCell) follows
            // the pointed range by design -- SelectRangeForFormulaEdit moves the selection precisely so
            // it can hold the edit session separately. Requiring ActiveCell to stay on the formula cell
            // asserted the opposite of how point mode works.
            dragPointEditSessionPreserved = _session.FormulaEditAddress == dragFormulaAddress;
            var liveEditorAfterRelease = GetFormulaRangeEntryEditor();
            _sheetGridHost.Focus();
            RestoreFormulaRangeEditorFocusAfterDrag(liveEditorAfterRelease);
            await SettleEditingInputAsync();
            dragPointFocusRestored = liveEditorAfterRelease?.IsFocused == true;
            if (_inlineCellEditor is { } currentDragFormulaEditor)
                await RaiseEditingValidationKeyAsync(currentDragFormulaEditor, Key.Enter);
        }

        var dragPointCommitted = string.Equals(
            sheet.GetCell(dragFormulaAddress)?.FormulaText,
            "B2:D4",
            StringComparison.Ordinal);
        results.Add(new InteractionValidationResult(
            "cell-inline-formula-point-range-drag",
            "worksheet-editing",
            dragPointStarted && dragAnchorReplayConsumed && dragPointExtended &&
            dragPointEditSessionPreserved && dragPointFocusRestored &&
            string.Equals(dragPointText, "=B2:D4", StringComparison.Ordinal) && dragPointCommitted
                ? "passed"
                : "failed",
            "production-point-drag-continuation-enter",
            $"started={dragPointStarted}; anchorReplayConsumed={dragAnchorReplayConsumed}; extended={dragPointExtended}; editSessionPreserved={dragPointEditSessionPreserved}; focusRestored={dragPointFocusRestored}; text={dragPointText}; committed={dragPointCommitted}",
            "The production inline formula editor inserted the drag anchor, extended the live reference without ordinary range-selection fallthrough, restored the live editor's focus after release, and committed the range formula with Enter."));
    }

    private InputElement ResolveWorksheetValidationKeyTarget() =>
        (InputElement?)_activeCellBorder ?? _sheetGridHost;

    private async Task RaiseEditingValidationTextInputAsync(InputElement target, string text)
    {
        target.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
            Source = target,
        });
        await SettleEditingInputAsync();
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
        await SettleEditingInputAsync();
    }

    private static async Task SettleEditingInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
