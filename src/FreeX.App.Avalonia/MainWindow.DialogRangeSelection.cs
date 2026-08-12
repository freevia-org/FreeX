using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Threading;

using FreeX.App.Presentation;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private sealed record DialogRangeInteractionEvidence(
        string TargetId,
        bool Passed,
        string Evidence);

    private static readonly DialogRangePickerRegistration[] DialogRangePickerRegistrations =
    [
        new("range.create-table.range", "CreateTableDialog", "CreateTableRangePicker", "CreateTableRangeBox", DialogRangeSelectionFormat.Range),
        new("range.sparklines.data-range", "InsertSparklineDialog", "SparklineSelectDataRangeButton", "SparklineDataRangeBox", DialogRangeSelectionFormat.Range),
        new("range.sparklines.location-range", "InsertSparklineDialog", "SparklineSelectLocationRangeButton", "SparklineLocationRangeBox", DialogRangeSelectionFormat.StartCell),
        new("range.consolidate.reference", "ConsolidateDialog", "ConsolidateBrowseReferenceButton", "ConsolidateReferenceBox", DialogRangeSelectionFormat.Range),
        new("range.consolidate.destination-cell", "ConsolidateDialog", "ConsolidateBrowseDestinationButton", "ConsolidateDestinationCellBox", DialogRangeSelectionFormat.StartCell),
        new("range.advanced-filter.list-range", FreeXAutomationIdCatalog.AdvancedFilter.Dialog, FreeXAutomationIdCatalog.AdvancedFilter.SelectListRangeButton, FreeXAutomationIdCatalog.AdvancedFilter.ListRangeBox, DialogRangeSelectionFormat.Range),
        new("range.advanced-filter.criteria-range", FreeXAutomationIdCatalog.AdvancedFilter.Dialog, FreeXAutomationIdCatalog.AdvancedFilter.SelectCriteriaRangeButton, FreeXAutomationIdCatalog.AdvancedFilter.CriteriaRangeBox, DialogRangeSelectionFormat.Range),
        new("range.advanced-filter.copy-to", FreeXAutomationIdCatalog.AdvancedFilter.Dialog, FreeXAutomationIdCatalog.AdvancedFilter.SelectCopyToButton, FreeXAutomationIdCatalog.AdvancedFilter.CopyToBox, DialogRangeSelectionFormat.Range),
        new("range.goal-seek.set-cell", "GoalSeekCompactDialog", "GoalSeekSetCellPickerButton", "GoalSeekSetCellBox", DialogRangeSelectionFormat.StartCell),
        new("range.goal-seek.changing-cell", "GoalSeekCompactDialog", "GoalSeekChangingCellPickerButton", "GoalSeekChangingCellBox", DialogRangeSelectionFormat.StartCell),
        new("range.chart-data-source.range", "SelectChartDataDialog", "SelectChartDataRangePickButton", "SelectChartDataRangeBox", DialogRangeSelectionFormat.Range),
        new("range.data-table.row-input-cell", "DataTableCompactDialog", "DataTableRowInputCellPickerButton", "DataTableRowInputCellBox", DialogRangeSelectionFormat.StartCell, CreatePickerWhenMissing: true),
        new("range.data-table.column-input-cell", "DataTableCompactDialog", "DataTableColumnInputCellPickerButton", "DataTableColumnInputCellBox", DialogRangeSelectionFormat.StartCell, CreatePickerWhenMissing: true),
        new("range.data-validation.formula-1", "DataValidationCompactDialog", "DataValidationSourcePickerButton", "DataValidationFormula1Box", DialogRangeSelectionFormat.DataValidationFormula, CreatePickerWhenMissing: true),
        new("range.data-validation.formula-2", "DataValidationCompactDialog", "DataValidationSourcePicker2Button", "DataValidationFormula2Box", DialogRangeSelectionFormat.DataValidationFormula, CreatePickerWhenMissing: true),
        new("range.page-setup.print-area", "PageSetupDialog", "PageSetupPrintAreaPickerButton", "PageSetupPrintAreaBox", DialogRangeSelectionFormat.PageSetupPrintArea),
        new("range.page-setup.rows-to-repeat", "PageSetupDialog", "PageSetupRowsRepeatPickerButton", "PageSetupRepeatRowsBox", DialogRangeSelectionFormat.PageSetupRepeatRows),
        new("range.page-setup.columns-to-repeat", "PageSetupDialog", "PageSetupColumnsRepeatPickerButton", "PageSetupRepeatColumnsBox", DialogRangeSelectionFormat.PageSetupRepeatColumns),
        new("range.allow-edit-range.range", "AllowEditRangeDialog", "AllowEditRangePickerButton", "AllowEditRangeBox", DialogRangeSelectionFormat.Range),
        new("range.text-to-columns.destination", "TextToColumnsDialog", "TextToColumnsDestinationPickerButton", "TextToColumnsDestinationBox", DialogRangeSelectionFormat.StartCell),
        new("range.resize-table.range", "TableResizeDialog", "TableResizeRangePickerButton", "TableResizeRangeBox", DialogRangeSelectionFormat.Range),
        new("range.named-ranges.selected-refers-to", "NameManagerDialog", "NameManagerSelectedRefersToPickerButton", "NameManagerSelectedRefersToBox", DialogRangeSelectionFormat.Range),
        new("range.named-ranges.definition-refers-to", "DefineNameDialog", "DefineNameRefersToPickerButton", "DefineNameRefersToBox", DialogRangeSelectionFormat.Range),
        new("range.pivot-create.source", "InsertPivotTableDialog", "InsertPivotTableSourceRangePickerButton", "InsertPivotTableSourceRangeBox", DialogRangeSelectionFormat.Range),
        new("range.pivot-create.destination", "InsertPivotTableDialog", "InsertPivotTableDestinationRangePickerButton", "InsertPivotTableDestinationRangeBox", DialogRangeSelectionFormat.StartCell),
        new("range.scenario-manager.changing-cells", FreeXAutomationIdCatalog.ScenarioManager.AvaloniaDialog, FreeXAutomationIdCatalog.ScenarioManager.ChangingCellsPickerButton, FreeXAutomationIdCatalog.ScenarioManager.ChangingCellsBox, DialogRangeSelectionFormat.Range),
        new("range.scenario-manager.result-cells", FreeXAutomationIdCatalog.ScenarioManager.AvaloniaDialog, FreeXAutomationIdCatalog.ScenarioManager.ResultCellsPickerButton, FreeXAutomationIdCatalog.ScenarioManager.ResultCellsBox, DialogRangeSelectionFormat.Range),
        new("range.function-argument.reference", "FunctionArgumentsDialog", "FunctionArgumentReferencePicker0", "FunctionArgumentBox0", DialogRangeSelectionFormat.Range),
        new("range.conditional-format.applies-to", "ManageConditionalFormatsDialog", "ManageConditionalFormatsAppliesToPickerButton", "ManageConditionalFormatsAppliesToBox", DialogRangeSelectionFormat.Range),
        new("range.move-pivot.destination", "MovePivotDialog", "MovePivotDestinationPickerButton", "MovePivotDestinationBox", DialogRangeSelectionFormat.StartCell),
        new("range.pivot-data-source.range", "PivotDataSourceDialog", "PivotDataSourceRangePickerButton", "PivotDataSourceRangeBox", DialogRangeSelectionFormat.Range),
    ];

    private static readonly ConditionalWeakTable<Button, DialogRangePickerRegistration> ConfiguredDialogRangePickers = new();
    private static readonly ConditionalWeakTable<Window, object> ConfiguredDialogKeyboardContracts = new();
    private static readonly MethodInfo? SetPlatformWindowEnabledMethod = typeof(IWindowImpl).GetMethod(
        "SetEnabled",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static IReadOnlySet<string> InteractiveValidationRangeTargetIds { get; } =
        DialogRangePickerRegistrations
            .Select(registration => registration.TargetId)
            .ToFrozenSet(StringComparer.Ordinal);

    private readonly DialogRangeSelectionController<DialogRangePickerContext> _dialogRangeSelectionController = new();
    private bool _isDialogRangeInteractionProbe;
    private readonly Dictionary<string, DialogRangeInteractionEvidence> _dialogRangeInteractionEvidence =
        new(StringComparer.Ordinal);

    private void ResetDialogRangeInteractionContracts() => _dialogRangeInteractionEvidence.Clear();

    private async Task RecordDialogRangeInteractionContractsAsync(Window dialog)
    {
        var dialogAutomationId = AutomationProperties.GetAutomationId(dialog);
        var registrations = DialogRangePickerRegistrations
            .Where(registration =>
                string.Equals(registration.DialogAutomationId, dialogAutomationId, StringComparison.Ordinal) &&
                !_dialogRangeInteractionEvidence.ContainsKey(registration.TargetId))
            .ToArray();
        if (registrations.Length == 0)
            return;

        var controls = dialog.GetLogicalDescendants().OfType<Control>().ToArray();
        foreach (var registration in registrations)
        {
            var picker = controls.OfType<Button>().FirstOrDefault(button =>
                string.Equals(AutomationProperties.GetAutomationId(button), registration.PickerAutomationId, StringComparison.Ordinal));
            var target = controls.OfType<TextBox>().FirstOrDefault(textBox =>
                string.Equals(AutomationProperties.GetAutomationId(textBox), registration.TextBoxAutomationId, StringComparison.Ordinal));
            if (picker is null || target is null)
            {
                RecordRangeInteractionEvidence(
                    registration,
                    passed: false,
                    $"missing-controls:picker={picker is not null},target={target is not null},dialog={dialogAutomationId}");
                continue;
            }

            var previousSelection = _session.SelectedRange;
            try
            {
                _isDialogRangeInteractionProbe = true;
                var originalText = target.Text ?? string.Empty;
                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent) { Source = picker });
                if (_dialogRangeSelectionController.Active?.Context.TargetId != registration.TargetId)
                {
                    RecordRangeInteractionEvidence(registration, passed: false, "picker-click-did-not-start-session");
                    continue;
                }

                var sheet = _session.ActiveSheet;
                var pointedRange = new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 3, 3));
                _session.SelectRange(pointedRange);
                RaiseDialogRangeValidationKey(Key.Enter);
                var expected = FormatDialogRangeSelection(pointedRange, registration.Format);
                if (_dialogRangeSelectionController.IsActive || !string.Equals(target.Text, expected, StringComparison.Ordinal))
                {
                    RecordRangeInteractionEvidence(
                        registration,
                        passed: false,
                        $"apply-failed:expected={expected},actual={target.Text},sessionActive={_dialogRangeSelectionController.IsActive}");
                    CancelDialogRangeSelection(restoreDialog: true, restoreOriginalText: true);
                    continue;
                }

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent) { Source = picker });
                if (_dialogRangeSelectionController.Active?.Context.TargetId != registration.TargetId)
                {
                    RecordRangeInteractionEvidence(registration, passed: false, "second-picker-click-did-not-start-session");
                    continue;
                }

                _session.SelectRange(new GridRange(
                    new CellAddress(sheet.Id, 4, 4),
                    new CellAddress(sheet.Id, 5, 5)));
                RaiseDialogRangeValidationKey(Key.Escape);
                var cancelRestored = !_dialogRangeSelectionController.IsActive &&
                    string.Equals(target.Text, expected, StringComparison.Ordinal);
                RecordRangeInteractionEvidence(
                    registration,
                    cancelRestored,
                    cancelRestored
                        ? $"picker-click; enter-apply={expected}; escape-restore={expected}; original={originalText}"
                        : $"cancel-failed:expected={expected},actual={target.Text},sessionActive={_dialogRangeSelectionController.IsActive}");
            }
            catch (Exception ex)
            {
                CancelDialogRangeSelection(restoreDialog: true, restoreOriginalText: true);
                RecordRangeInteractionEvidence(
                    registration,
                    passed: false,
                    $"{ex.GetType().Name}:{ex.Message}");
            }
            finally
            {
                _isDialogRangeInteractionProbe = false;
                _session.SelectRange(previousSelection);
            }
        }

        await Task.CompletedTask;
    }

    private void RaiseDialogRangeValidationKey(Key key)
    {
        var target = FocusManager?.GetFocusedElement() as InputElement ?? _sheetGridHost;
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            PhysicalKey = key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape,
            KeyDeviceType = KeyDeviceType.Keyboard,
            Source = target,
        });
    }

    private void RecordRangeInteractionEvidence(
        DialogRangePickerRegistration registration,
        bool passed,
        string evidence) =>
        _dialogRangeInteractionEvidence[registration.TargetId] =
            new DialogRangeInteractionEvidence(registration.TargetId, passed, evidence);

    static MainWindow()
    {
        // Some inventory dialogs still live in the protected MainWindow.cs builder. Attach their existing
        // or registration-supplied picker controls after Avalonia assigns this MainWindow as owner.
        Window.OwnerProperty.Changed.AddClassHandler<Window>(DialogRangePickerOwnerChanged);
    }

    private static void DialogRangePickerOwnerChanged(Window dialog, AvaloniaPropertyChangedEventArgs _)
    {
        if (dialog.Owner is MainWindow owner)
        {
            owner.AttachInventoryDialogRangePickers(dialog);
            owner.AttachDefaultDialogKeyboardContract(dialog);
        }
    }

    private void AttachDefaultDialogKeyboardContract(Window dialog)
    {
        if (!ConfiguredDialogKeyboardContracts.TryAdd(dialog, new object()))
            return;

        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                if (dialog.FocusManager?.GetFocusedElement() is Visual focused &&
                    ReferenceEquals(TopLevel.GetTopLevel(focused), dialog))
                {
                    return;
                }

                dialog.GetLogicalDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(control =>
                        control.Focusable && control.IsVisible && control.IsEffectivelyEnabled)
                    ?.Focus();
            },
            DispatcherPriority.Input);
        dialog.Closed += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                var focused = FocusManager?.GetFocusedElement();
                if (focused is not Visual visual || !ReferenceEquals(TopLevel.GetTopLevel(visual), this))
                {
                    Activate();
                    _sheetGridHost.Focus();
                }
            },
            DispatcherPriority.Input);
    }

    private void AttachInventoryDialogRangePickers(Window dialog)
    {
        var dialogAutomationId = AutomationProperties.GetAutomationId(dialog);
        var registrations = DialogRangePickerRegistrations
            .Where(registration => string.Equals(registration.DialogAutomationId, dialogAutomationId, StringComparison.Ordinal))
            .ToArray();
        if (registrations.Length == 0)
            return;

        var controls = dialog.GetLogicalDescendants().OfType<Control>().ToArray();
        foreach (var registration in registrations)
        {
            var picker = controls.OfType<Button>().FirstOrDefault(button =>
                string.Equals(AutomationProperties.GetAutomationId(button), registration.PickerAutomationId, StringComparison.Ordinal));
            var target = controls.OfType<TextBox>().FirstOrDefault(textBox =>
                string.Equals(AutomationProperties.GetAutomationId(textBox), registration.TextBoxAutomationId, StringComparison.Ordinal));
            if (picker is null && target is not null && registration.CreatePickerWhenMissing)
                picker = AddMissingDialogRangePicker(target, registration);
            if (picker is not null && target is not null)
                AttachDialogRangePicker(dialog, picker, target, registration.TargetId);
        }
    }

    private static Button? AddMissingDialogRangePicker(
        TextBox target,
        DialogRangePickerRegistration registration)
    {
        if (target.Parent is not Panel parent)
            return null;

        var index = parent.Children.IndexOf(target);
        if (index < 0)
            return null;

        var picker = CreateDialogRangePickerButton(
            registration.PickerAutomationId,
            UiText.Format(
                "DialogRangePicker_SelectRangeForFormat",
                AutomationProperties.GetName(target) ?? UiText.Get("Common_Input")));
        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, BuildDialogRangePickerRow(target, picker));
        return picker;
    }

    private static Button CreateDialogRangePickerButton(string automationId, string automationName)
    {
        var picker = new Button
        {
            Content = "...",
            Width = 28,
            MinWidth = 28,
            Margin = new Thickness(6, 0, 0, 0),
        };
        ApplyDialogButtonChrome(picker, 28);
        AutomationProperties.SetAutomationId(picker, automationId);
        AutomationProperties.SetName(picker, automationName);
        AutomationProperties.SetHelpText(picker, UiText.Get("DialogReferencePicker_HelpText"));
        ToolTip.SetTip(picker, UiText.Get("DialogReferencePicker_ToolTip"));
        return picker;
    }

    private static Grid BuildDialogRangePickerRow(TextBox target, Button picker)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(target, 0);
        Grid.SetColumn(picker, 1);
        row.Children.Add(target);
        row.Children.Add(picker);
        return row;
    }

    private void AttachDialogRangePicker(Window dialog, Button picker, TextBox target, string targetId)
    {
        var registration = DialogRangePickerRegistrations.Single(candidate =>
            string.Equals(candidate.TargetId, targetId, StringComparison.Ordinal));
        if (!ConfiguredDialogRangePickers.TryAdd(picker, registration))
            return;

        picker.Click += (_, _) => BeginDialogRangeSelection(dialog, target, registration);
    }

    private void BeginDialogRangeSelection(
        Window dialog,
        TextBox target,
        DialogRangePickerRegistration registration)
    {
        var session = _dialogRangeSelectionController.Begin(
            new DialogRangePickerContext(
                dialog,
                target,
                registration.TargetId,
                dialog.Position,
                dialog.Opacity,
                dialog.IsHitTestVisible),
            target.Text,
            registration.Format,
            collapseDialog: true,
            IsEnabled || dialog.IsDialog,
            FinishDialogRangeSelectionTransition);
        _sheetGridHost.AddHandler(
            InputElement.PointerReleasedEvent,
            DialogRangePickerPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.KeyDownEvent,
            DialogRangePickerKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        dialog.Closed += DialogRangePickerDialogClosed;

        CollapseDialogForRangeSelection(session);
        SetDialogRangePickerOwnerInputEnabled(true);
        Activate();
        _sheetGridHost.Focus();
    }

    private void DialogRangePickerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dialogRangeSelectionController.IsActive || e.InitialPressMouseButton != MouseButton.Left)
            return;

        Dispatcher.UIThread.Post(
            () => CompleteDialogRangeSelection(applySelection: true),
            DispatcherPriority.Background);
    }

    private void DialogRangePickerKeyDown(object? sender, KeyEventArgs e)
    {
        var result = _dialogRangeSelectionController.HandleKey(e.Key switch
        {
            Key.Escape => DialogRangeSelectionKey.Escape,
            Key.Enter => DialogRangeSelectionKey.Enter,
            _ => DialogRangeSelectionKey.Other,
        }, _session.SelectedRange);
        if (!result.Handled)
            return;

        if (result.Transition is { } transition)
            FinishDialogRangeSelectionTransition(transition);
        e.Handled = true;
    }

    private void DialogRangePickerDialogClosed(object? sender, EventArgs e) =>
        CancelDialogRangeSelection(restoreDialog: false, restoreOriginalText: false);

    private void CompleteDialogRangeSelection(bool applySelection)
    {
        if (_dialogRangeSelectionController.Complete(_session.SelectedRange, applySelection) is { } transition)
            FinishDialogRangeSelectionTransition(transition);
    }

    private void CancelDialogRangeSelection(bool restoreDialog, bool restoreOriginalText)
    {
        if (_dialogRangeSelectionController.Cancel(restoreDialog, restoreOriginalText) is { } transition)
            FinishDialogRangeSelectionTransition(transition);
    }

    private void FinishDialogRangeSelectionTransition(
        DialogRangeSelectionTransition<DialogRangePickerContext> transition) =>
        _dialogRangeSelectionController.FinishTransition(
            transition,
            DetachDialogRangeSelection,
            (state, selectedRange) =>
                state.Context.Target.Text = FormatDialogRangeSelection(selectedRange, state.Format),
            state => state.Context.Target.Text = state.OriginalText,
            RestoreDialogAfterRangeSelection);

    private void DetachDialogRangeSelection(DialogRangePickerContext context)
    {
        _sheetGridHost.RemoveHandler(InputElement.PointerReleasedEvent, DialogRangePickerPointerReleased);
        RemoveHandler(InputElement.KeyDownEvent, DialogRangePickerKeyDown);
        context.Dialog.Closed -= DialogRangePickerDialogClosed;
    }

    private void RestoreDialogAfterRangeSelection(
        DialogRangeSelectionState<DialogRangePickerContext> session)
    {
        var context = session.Context;
        IsEnabled = session.OwnerWasEnabled;
        SetPlatformWindowEnabled(context.Dialog.IsVisible && context.Dialog.IsDialog
            ? false
            : session.OwnerWasEnabled);
        context.Dialog.Opacity = context.DialogOpacity;
        context.Dialog.IsHitTestVisible = context.DialogIsHitTestVisible;

        if (!context.Dialog.IsVisible)
            return;

        if (!_isDialogRangeInteractionProbe)
            context.Dialog.Position = context.DialogPosition;

        void ActivateDialogAndRestoreTargetFocus()
        {
            if (!context.Dialog.IsVisible || _dialogRangeSelectionController.IsActive)
                return;

            context.Dialog.Activate();
            context.Target.Focus();
            context.Target.SelectAll();
        }

        ActivateDialogAndRestoreTargetFocus();
        Dispatcher.UIThread.Post(ActivateDialogAndRestoreTargetFocus, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(ActivateDialogAndRestoreTargetFocus, DispatcherPriority.Background);
    }

    private void SetDialogRangePickerOwnerInputEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
        SetPlatformWindowEnabled(isEnabled);
    }

    private void SetPlatformWindowEnabled(bool isEnabled)
    {
        if (PlatformImpl is { } platformImpl)
            SetPlatformWindowEnabledMethod?.Invoke(platformImpl, [isEnabled]);
    }

    private void CollapseDialogForRangeSelection(
        DialogRangeSelectionState<DialogRangePickerContext> session)
    {
        var context = session.Context;
        // The automated contract selects cells through the live session rather than a pointer. Keeping
        // the modal at its native X11 position avoids an Openbox recenter/offscreen geometry loop while
        // still exercising the production picker click, owner enablement, and Enter/Escape handlers.
        if (_isDialogRangeInteractionProbe)
        {
            context.Dialog.Opacity = 0;
            context.Dialog.IsHitTestVisible = false;
            return;
        }

        var width = DialogRangeSelectionGeometryPlanner.ResolveDimension(
            context.Dialog.Bounds.Width,
            context.Dialog.Width,
            420);
        var height = DialogRangeSelectionGeometryPlanner.ResolveDimension(
            context.Dialog.Bounds.Height,
            context.Dialog.Height,
            560);
        var screens = context.Dialog.Screens.All;
        var virtualLeft = screens.Count > 0 ? screens.Min(screen => screen.Bounds.X) : -10000;
        var virtualTop = screens.Count > 0 ? screens.Min(screen => screen.Bounds.Y) : -10000;
        context.Dialog.Opacity = 0;
        context.Dialog.IsHitTestVisible = false;
        context.Dialog.Position = new PixelPoint(
            virtualLeft - (int)Math.Ceiling(width) - 32,
            virtualTop - (int)Math.Ceiling(height) - 32);
    }

    private string FormatDialogRangeSelection(GridRange range, DialogRangeSelectionFormat format) =>
        DialogRangeSelectionFormatter.Format(
            range,
            format,
            new DialogRangeSelectionFormatContext(
                _session.Workbook.GetSheet(range.Start.Sheet)?.Name,
                _session.ActiveSheet.Name,
                UseR1C1ReferenceStyle));

    private sealed record DialogRangePickerRegistration(
        string TargetId,
        string DialogAutomationId,
        string PickerAutomationId,
        string TextBoxAutomationId,
        DialogRangeSelectionFormat Format,
        bool CreatePickerWhenMissing = false);

    private sealed record DialogRangePickerContext(
        Window Dialog,
        TextBox Target,
        string TargetId,
        PixelPoint DialogPosition,
        double DialogOpacity,
        bool DialogIsHitTestVisible);
}
