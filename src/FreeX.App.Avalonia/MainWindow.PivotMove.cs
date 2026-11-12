using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "Move PivotTable" dialog for the Avalonia/macOS shell: a single reference box seeded with
/// the active pivot's current top-left cell. The typed destination is resolved through the shared
/// <see cref="WorkbookSession.TryResolveReferenceRange"/> seam (the same parser Go To / Change Data Source
/// editing use); the move round-trips through <see cref="MovePivotTableCommand"/> (the same command the
/// desktop host's PivotTableMoveBtn handler uses, which retargets dependent charts/slicers/timelines). Like
/// the WPF host the move is restricted to a cell on the current sheet. Reached from the Analyze ▸ Actions ▸
/// Move PivotTable ribbon command (<c>pivotAnalyze.move</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Analyze ▸ Move PivotTable — opens the destination dialog for the active pivot.</summary>
    private void OpenPivotMove()
    {
        if (!TryResolvePivotApplicationTarget(out var target))
            return;

        RunGuarded(() => OpenPivotMoveDialogAsync(target));
    }

    private async Task OpenPivotMoveDialogAsync(PivotApplicationTarget target)
    {
        if (_isOpening || _isSaving)
            return;

        var pivot = target.PivotTable;
        var destinationBox = new TextBox
        {
            Text = FormatCellReference(pivot.TargetRange.Start),
        };
        ApplyPivotTextBoxChrome(destinationBox);
        AutomationProperties.SetAutomationId(destinationBox, "MovePivotDestinationBox");
        AutomationProperties.SetName(destinationBox, UiText.Get("MovePivot_RangeName"));

        var destinationPicker = new Button { Content = "..." };
        AutomationProperties.SetAutomationId(destinationPicker, "MovePivotDestinationPickerButton");
        AutomationProperties.SetName(destinationPicker, UiText.Get("MovePivotTable_SelectDestination"));

        // WPF MovePivotTableDialog: a 420px NoResize window whose height follows the content, with a
        // 160px outer minimum.
        var dialog = new Window
        {
            Title = UiText.Get("MovePivotTable_Title"),
            Width = 420,
            MinWidth = 420,
            MaxWidth = 420,
            MinHeight = 160 - WpfNoResizeFrameHeight,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "MovePivotDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(ok, PivotDialogButtonMinWidth, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "MovePivotOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, PivotDialogButtonMinWidth);
        AutomationProperties.SetAutomationId(cancel, "MovePivotCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            var plan = PivotApplication.PlanMove(target, destinationBox.Text);
            if (!plan.CanApply)
            {
                ShowPivotApplicationIssue(plan.Message);
                return;
            }

            dialog.Close(true);
        };

        // WPF: StackPanel { Margin = 16 }: label (0,3,0,4), the picker + reference box editor with a
        // 16px bottom margin, then the shared pivot OK/Cancel row (12px top margin).
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(CreatePivotDialogLabel(UiText.Get("MovePivotTable_DestinationLabel")));
        content.Children.Add(CreatePivotDialogReferenceEditor(
            destinationPicker,
            destinationBox,
            new Thickness(0, 0, 0, 16)));
        content.Children.Add(CreatePivotDialogButtonRow(ok, cancel));
        dialog.Content = content;
        dialog.Opened += (_, _) =>
        {
            destinationBox.Focus();
            destinationBox.SelectAll();
        };
        AttachDialogRangePicker(
            dialog,
            destinationPicker,
            destinationBox,
            "range.move-pivot.destination");

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanMove(target, destinationBox.Text));
    }
}
