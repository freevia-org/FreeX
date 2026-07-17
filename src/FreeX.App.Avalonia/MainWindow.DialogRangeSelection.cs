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
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly DialogRangePickerRegistration[] DialogRangePickerRegistrations =
    [
        new("range.create-table.range", "CreateTableDialog", "CreateTableRangePicker", "CreateTableRangeBox", DialogRangeSelectionFormat.Range),
        new("range.sparklines.data-range", "InsertSparklineDialog", "SparklineSelectDataRangeButton", "SparklineDataRangeBox", DialogRangeSelectionFormat.Range),
        new("range.sparklines.location-range", "InsertSparklineDialog", "SparklineSelectLocationRangeButton", "SparklineLocationRangeBox", DialogRangeSelectionFormat.StartCell),
        new("range.consolidate.reference", "ConsolidateDialog", "ConsolidateBrowseReferenceButton", "ConsolidateReferenceBox", DialogRangeSelectionFormat.Range),
        new("range.consolidate.destination-cell", "ConsolidateDialog", "ConsolidateBrowseDestinationButton", "ConsolidateDestinationCellBox", DialogRangeSelectionFormat.StartCell),
        new("range.advanced-filter.list-range", "AdvancedFilterCompactDialog", "AdvancedFilterSelectListRangeButton", "AdvancedFilterListRangeBox", DialogRangeSelectionFormat.Range),
        new("range.advanced-filter.criteria-range", "AdvancedFilterCompactDialog", "AdvancedFilterSelectCriteriaRangeButton", "AdvancedFilterCriteriaRangeBox", DialogRangeSelectionFormat.Range),
        new("range.advanced-filter.copy-to", "AdvancedFilterCompactDialog", "AdvancedFilterSelectCopyToButton", "AdvancedFilterCopyToBox", DialogRangeSelectionFormat.Range),
        new("range.goal-seek.set-cell", "GoalSeekCompactDialog", "GoalSeekSetCellPickerButton", "GoalSeekSetCellBox", DialogRangeSelectionFormat.StartCell),
        new("range.goal-seek.changing-cell", "GoalSeekCompactDialog", "GoalSeekChangingCellPickerButton", "GoalSeekChangingCellBox", DialogRangeSelectionFormat.StartCell),
        new("range.chart-data-source.range", "SelectChartDataDialog", "SelectChartDataRangePickButton", "SelectChartDataRangeBox", DialogRangeSelectionFormat.Range),
    ];

    private static readonly ConditionalWeakTable<Button, DialogRangePickerRegistration> ConfiguredDialogRangePickers = new();
    private static readonly MethodInfo? SetPlatformWindowEnabledMethod = typeof(IWindowImpl).GetMethod(
        "SetEnabled",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static IReadOnlySet<string> InteractiveValidationRangeTargetIds { get; } =
        DialogRangePickerRegistrations
            .Select(registration => registration.TargetId)
            .ToFrozenSet(StringComparer.Ordinal);

    private DialogRangePickerSession? _dialogRangePickerSession;

    static MainWindow()
    {
        // Advanced Filter and Goal Seek still live in the protected MainWindow.cs builder. Attach them
        // when Avalonia assigns this MainWindow as owner, after their automation-labelled content exists.
        Window.OwnerProperty.Changed.AddClassHandler<Window>(DialogRangePickerOwnerChanged);
    }

    private static void DialogRangePickerOwnerChanged(Window dialog, AvaloniaPropertyChangedEventArgs _)
    {
        if (dialog.Owner is MainWindow owner)
            owner.AttachInventoryDialogRangePickers(dialog);
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
            if (picker is not null && target is not null)
                AttachDialogRangePicker(dialog, picker, target, registration.TargetId);
        }
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
        CancelDialogRangeSelection(restoreDialog: true, restoreOriginalText: true);
        var session = new DialogRangePickerSession(
            dialog,
            target,
            registration,
            target.Text ?? string.Empty,
            IsEnabled,
            dialog.Position,
            dialog.Opacity,
            dialog.IsHitTestVisible);
        _dialogRangePickerSession = session;
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
        if (_dialogRangePickerSession is null || e.InitialPressMouseButton != MouseButton.Left)
            return;

        Dispatcher.UIThread.Post(
            () => CompleteDialogRangeSelection(applySelection: true),
            DispatcherPriority.Background);
    }

    private void DialogRangePickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dialogRangePickerSession is null)
            return;

        if (e.Key == Key.Escape)
        {
            CompleteDialogRangeSelection(applySelection: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CompleteDialogRangeSelection(applySelection: true);
            e.Handled = true;
        }
    }

    private void DialogRangePickerDialogClosed(object? sender, EventArgs e) =>
        CancelDialogRangeSelection(restoreDialog: false, restoreOriginalText: false);

    private void CompleteDialogRangeSelection(bool applySelection)
    {
        var session = _dialogRangePickerSession;
        if (session is null)
            return;

        CancelDialogRangeSelection(restoreDialog: false, restoreOriginalText: false);
        try
        {
            session.Target.Text = applySelection
                ? FormatDialogRangeSelection(_session.SelectedRange, session.Registration.Format)
                : session.OriginalText;
        }
        finally
        {
            RestoreDialogAfterRangeSelection(session);
        }
    }

    private void CancelDialogRangeSelection(bool restoreDialog, bool restoreOriginalText)
    {
        var session = _dialogRangePickerSession;
        if (session is null)
            return;

        _dialogRangePickerSession = null;
        _sheetGridHost.RemoveHandler(InputElement.PointerReleasedEvent, DialogRangePickerPointerReleased);
        RemoveHandler(InputElement.KeyDownEvent, DialogRangePickerKeyDown);
        session.Dialog.Closed -= DialogRangePickerDialogClosed;
        if (restoreOriginalText)
            session.Target.Text = session.OriginalText;
        if (restoreDialog)
            RestoreDialogAfterRangeSelection(session);
    }

    private void RestoreDialogAfterRangeSelection(DialogRangePickerSession session)
    {
        IsEnabled = session.OwnerWasEnabled;
        SetPlatformWindowEnabled(session.Dialog.IsVisible && session.Dialog.IsDialog
            ? false
            : session.OwnerWasEnabled);
        session.Dialog.Position = session.DialogPosition;
        session.Dialog.Opacity = session.DialogOpacity;
        session.Dialog.IsHitTestVisible = session.DialogIsHitTestVisible;

        if (!session.Dialog.IsVisible)
            return;

        session.Dialog.Activate();
        session.Target.Focus();
        session.Target.SelectAll();
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

    private static void CollapseDialogForRangeSelection(DialogRangePickerSession session)
    {
        var width = EffectiveDialogRangeSelectionDimension(session.Dialog.Bounds.Width, session.Dialog.Width, 420);
        var height = EffectiveDialogRangeSelectionDimension(session.Dialog.Bounds.Height, session.Dialog.Height, 560);
        var screens = session.Dialog.Screens.All;
        var virtualLeft = screens.Count > 0 ? screens.Min(screen => screen.Bounds.X) : -10000;
        var virtualTop = screens.Count > 0 ? screens.Min(screen => screen.Bounds.Y) : -10000;
        session.Dialog.Opacity = 0;
        session.Dialog.IsHitTestVisible = false;
        session.Dialog.Position = new PixelPoint(
            virtualLeft - (int)Math.Ceiling(width) - 32,
            virtualTop - (int)Math.Ceiling(height) - 32);
    }

    private static double EffectiveDialogRangeSelectionDimension(double actual, double configured, double fallback)
    {
        if (!double.IsNaN(actual) && actual > 0)
            return actual;
        if (!double.IsNaN(configured) && configured > 0)
            return configured;
        return fallback;
    }

    private static string FormatDialogRangeSelection(GridRange range, DialogRangeSelectionFormat format) =>
        format == DialogRangeSelectionFormat.StartCell
            ? SpreadsheetDisplayFormatter.FormatCellReference(range.Start, useR1C1ReferenceStyle: false)
            : SpreadsheetDisplayFormatter.FormatRangeReference(range.Start, range.End, useR1C1ReferenceStyle: false);

    private enum DialogRangeSelectionFormat
    {
        Range,
        StartCell,
    }

    private sealed record DialogRangePickerRegistration(
        string TargetId,
        string DialogAutomationId,
        string PickerAutomationId,
        string TextBoxAutomationId,
        DialogRangeSelectionFormat Format);

    private sealed record DialogRangePickerSession(
        Window Dialog,
        TextBox Target,
        DialogRangePickerRegistration Registration,
        string OriginalText,
        bool OwnerWasEnabled,
        PixelPoint DialogPosition,
        double DialogOpacity,
        bool DialogIsHitTestVisible);
}
