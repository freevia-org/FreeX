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

        _ = OpenPivotMoveDialogAsync(target);
    }

    private async Task OpenPivotMoveDialogAsync(PivotApplicationTarget target)
    {
        if (_isOpening || _isSaving)
            return;

        var pivot = target.PivotTable;
        var destinationBox = new TextBox
        {
            Text = FormatCellReference(pivot.TargetRange.Start),
            MinWidth = 280,
        };
        ApplyPivotTextBoxChrome(destinationBox);
        AutomationProperties.SetAutomationId(destinationBox, "MovePivotDestinationBox");
        AutomationProperties.SetName(destinationBox, UiText.Get("MovePivot_RangeName"));

        var destinationPicker = new Button
        {
            Content = "...",
            Width = 30,
            MinWidth = 30,
            Margin = new Thickness(6, 0, 0, 0),
        };
        ApplyPivotButtonChrome(destinationPicker, 30);
        AutomationProperties.SetAutomationId(destinationPicker, "MovePivotDestinationPickerButton");
        AutomationProperties.SetName(destinationPicker, UiText.Get("MovePivotTable_SelectDestination"));

        var dialog = new Window
        {
            Title = UiText.Get("MovePivot_Title"),
            Width = 420,
            Height = 180,
            MinWidth = 420,
            MinHeight = 180,
            SizeToContent = SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "MovePivotDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "MovePivotOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, 80);
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

        var content = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = UiText.Get("MovePivot_Label"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        var destinationRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        destinationRow.Children.Add(destinationBox);
        Grid.SetColumn(destinationPicker, 1);
        destinationRow.Children.Add(destinationPicker);
        content.Children.Add(destinationRow);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel },
        });
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
