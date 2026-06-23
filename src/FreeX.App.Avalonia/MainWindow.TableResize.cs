using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "Resize Table" dialog for the Avalonia/macOS shell: the dialog stays renderer-local while
/// range capture, validation, and resize command composition come from portable TableUI planners.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Opens the resize dialog for the active table and applies the resolved range through the shared command
    /// planner. Reports an honest status when no table is active.
    /// </summary>
    private void OpenTableResize()
    {
        if (!TryGetActiveStructuredTable(out var table))
        {
            RefreshShell(UiText.Get("TableResize_NoTable"));
            return;
        }

        _ = OpenTableResizeDialogAsync(table);
    }

    private async Task OpenTableResizeDialogAsync(StructuredTableModel table)
    {
        if (_isOpening || _isSaving)
            return;

        var sheetId = _session.ActiveSheet.Id;

        var rangeBox = new TextBox
        {
            Text = TableResizePlanner.Capture(table),
            MinWidth = 320,
        };
        ApplyTableResizeTextBoxChrome(rangeBox);
        AutomationProperties.SetAutomationId(rangeBox, "TableResizeRangeBox");
        AutomationProperties.SetName(rangeBox, UiText.Get("TableResize_BoxName"));

        var dialog = new Window
        {
            Title = UiText.Get("TableResize_Title"),
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "TableResizeDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyTableResizeButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "TableResizeOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyTableResizeButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "TableResizeCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!TableResizePlanner.TryCreateResize(
                    table, rangeBox.Text, _session.TryResolveReferenceRange, out _, out var error))
            {
                ShowEditIssue(error ?? UiText.Get("TableResize_Failed"));
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("TableResize_Label"),
            Foreground = HeaderForeground,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        });
        content.Children.Add(rangeBox);
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

        if (!TableResizePlanner.TryCreateResize(
                table, rangeBox.Text, _session.TryResolveReferenceRange, out var change, out var lateError))
        {
            ShowEditIssue(lateError ?? UiText.Get("TableResize_Failed"));
            return;
        }

        var command = TableDesignCommandPlanner.BuildResizeCommand(
            sheetId,
            table,
            change!.NewRange,
            _session.Workbook.Theme);
        var result = _session.ExecuteReviewCommand(command);

        if (result.Success)
        {
            RefreshTableContextualTab();
            RefreshShell(UiText.Format("TableResize_Resized", TableDisplayName(table), change.NewRangeText));
        }
        else
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableResize_Failed"));
        }
    }

    // ── Visual chrome helpers (TableResize dialog) ───────────────────────────

    /// <summary>
    /// Applies standard TableResize-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// </summary>
    private static void ApplyTableResizeButtonChrome(Button button, double minWidth, bool isDefault = false)
    {
        button.MinWidth = minWidth;
        button.Height = 24;
        button.MinHeight = 24;
        button.MaxHeight = 24;
        button.Padding = new Thickness(4, 1);
        button.Background = Brushes.White;
        button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);
        button.BorderThickness = new Thickness(1);
        button.FontSize = 12;
        button.FontFamily = FormulaBarFontFamily;
        button.HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center;
        button.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    /// <summary>
    /// Applies standard TableResize-dialog text-box chrome (Height=24, Padding=(4,1), FontSize=12, grey border).
    /// </summary>
    private static void ApplyTableResizeTextBoxChrome(TextBox textBox)
    {
        textBox.Height = 24;
        textBox.MinHeight = 24;
        textBox.MaxHeight = 24;
        textBox.Padding = new Thickness(4, 1);
        textBox.FontSize = 12;
        textBox.FontFamily = FormulaBarFontFamily;
        textBox.BorderBrush = Brush(130, 130, 130);
        textBox.BorderThickness = new Thickness(1);
        textBox.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }
}
