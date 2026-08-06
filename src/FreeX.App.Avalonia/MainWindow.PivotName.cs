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
/// Windows-parity "PivotTable Name" rename dialog for the Avalonia/macOS shell: a single name box seeded with
/// the active pivot's current name, validated through the portable <see cref="PivotNamePlanner"/> (non-empty,
/// not colliding with another PivotTable) so the behavior is single-sourced with the WPF host and reusable on
/// macOS. The collision check is a closure over the workbook's pivot tables. The result round-trips through
/// <see cref="RenamePivotTableCommand"/> (the same command the desktop host's rename uses, which also retargets
/// dependent pivot charts/slicers/timelines). Reached from the Analyze ▸ PivotTable Name ribbon command
/// (<c>pivotAnalyze.name</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Analyze ▸ PivotTable Name — opens the rename dialog for the active pivot and applies the result through
    /// the Core rename command. Reports an honest status when no pivot is active.
    /// </summary>
    private void OpenPivotName()
    {
        if (!TryResolvePivotApplicationTarget(out var target))
            return;

        _ = OpenPivotNameDialogAsync(target);
    }

    private async Task OpenPivotNameDialogAsync(PivotApplicationTarget target)
    {
        if (_isOpening || _isSaving)
            return;

        var pivot = target.PivotTable;
        var nameBox = new TextBox
        {
            Text = PivotNamePlanner.Capture(pivot),
            MinWidth = 280,
        };
        ApplyPivotTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "PivotNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("PivotName_NameAutomation"));

        var dialog = new Window
        {
            Title = UiText.Get("PivotName_Title"),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotNameDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotNameOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "PivotNameCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);

        ok.Click += (_, _) =>
        {
            var plan = PivotApplication.PlanRename(target, nameBox.Text);
            if (!plan.CanApply)
            {
                ShowPivotApplicationIssue(plan.Message);
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotName_Label"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(nameBox);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanRename(target, nameBox.Text));
    }
}
