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
/// Windows-parity "Change PivotTable Data Source" dialog for the Avalonia/macOS shell: a single reference box
/// pre-filled with the active pivot's current source range, validated and resolved into the new source
/// <see cref="GridRange"/>. The current-range capture, the reference validation (a header row plus at least one
/// data row), and the change building come from the portable <see cref="PivotDataSourcePlanner"/> so the
/// behavior is single-sourced with the WPF host and reusable on macOS; reference resolution reuses the shared
/// <see cref="WorkbookSession.TryResolveReferenceRange"/> seam (same parser Go To / conditional-format applies-to
/// editing use). The result round-trips through <see cref="ChangePivotTableSourceCommand"/>. Reached from the
/// Analyze ▸ Change Data Source ribbon command (<c>pivotAnalyze.changeDataSource</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Analyze ▸ Change Data Source — opens the data-source dialog for the active pivot and applies the
    /// resolved range through the Core change-source command. Reports an honest status when no pivot is active.
    /// </summary>
    private void OpenPivotDataSource()
    {
        if (!TryResolvePivotApplicationTarget(out var target))
            return;

        _ = OpenPivotDataSourceDialogAsync(target);
    }

    private async Task OpenPivotDataSourceDialogAsync(PivotApplicationTarget target)
    {
        if (_isOpening || _isSaving)
            return;

        var pivot = target.PivotTable;
        var sourceBox = new TextBox
        {
            Text = PivotDataSourcePlanner.Capture(pivot),
            MinWidth = 320,
        };
        ApplyPivotTextBoxChrome(sourceBox);
        AutomationProperties.SetAutomationId(sourceBox, "PivotDataSourceRangeBox");
        AutomationProperties.SetName(sourceBox, UiText.Get("PivotDataSource_RangeName"));

        var sourcePicker = new Button
        {
            Content = "...",
            Width = 30,
            MinWidth = 30,
            Margin = new Thickness(6, 0, 0, 0),
        };
        ApplyPivotButtonChrome(sourcePicker, 30);
        AutomationProperties.SetAutomationId(sourcePicker, "PivotDataSourceRangePickerButton");
        AutomationProperties.SetName(sourcePicker, UiText.Get("PivotTableDataSource_SelectPivotTableSourceRange"));

        var dialog = new Window
        {
            Title = UiText.Get("PivotDataSource_Title"),
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotDataSourceDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotDataSourceOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "PivotDataSourceCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            var plan = PivotApplication.PlanChangeDataSource(target, sourceBox.Text);
            if (!plan.CanApply)
            {
                ShowPivotApplicationIssue(plan.Message);
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("PivotDataSource_RangeLabel"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
        });
        var sourceRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        sourceRow.Children.Add(sourceBox);
        Grid.SetColumn(sourcePicker, 1);
        sourceRow.Children.Add(sourcePicker);
        content.Children.Add(sourceRow);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;
        AttachDialogRangePicker(
            dialog,
            sourcePicker,
            sourceBox,
            "range.pivot-data-source.range");

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanChangeDataSource(target, sourceBox.Text));
    }
}
