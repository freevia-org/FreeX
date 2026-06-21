using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

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
        AutomationProperties.SetAutomationId(ok, "TableResizeOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
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
}
