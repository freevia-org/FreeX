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
/// Windows-parity "Resize Table" dialog for the Avalonia/macOS shell: a single reference box pre-filled with the
/// active table's current range, validated and resolved into the new data <see cref="GridRange"/>. The current-
/// range capture and the validation (same sheet, top-left header cell fixed, a header row plus at least one data
/// row, at least one column) come from the portable <see cref="TableResizePlanner"/> so the behavior is single-
/// sourced with the WPF host and reusable on macOS; reference resolution reuses the shared
/// <see cref="WorkbookSession.TryResolveReferenceRange"/> seam (the same parser Go To / data-source editing use).
/// The result round-trips through <see cref="ResizeStructuredTableCommand"/>, followed by a style reapply (via
/// <see cref="ApplyStructuredTableStyleCommand"/> when the table carries a built-in style) so any newly enclosed
/// cells pick up the table's banding — mirroring the WPF host. Reached from the Table Design ▸ Resize Table
/// ribbon command (<c>tableDesign.resize</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Table Design ▸ Resize Table — opens the resize dialog for the active table and applies the resolved range
    /// through the Core resize command. Reports an honest status when no table is active.
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

        var command = BuildResizeCommand(sheetId, table, change!.NewRange);
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

    /// <summary>
    /// Builds the resize command: the bare <see cref="ResizeStructuredTableCommand"/>, paired (in a composite)
    /// with an <see cref="ApplyStructuredTableStyleCommand"/> that repaints the resized table's banding when the
    /// table carries a built-in style, so any newly enclosed cells inherit the table's style — matching the WPF
    /// host. A table with no (or a non-built-in) style only runs the resize.
    /// </summary>
    private IWorkbookCommand BuildResizeCommand(SheetId sheetId, StructuredTableModel table, GridRange newRange)
    {
        var resize = new ResizeStructuredTableCommand(sheetId, table.Id, newRange);
        if (!TableStyleGalleryPlanner.TryGetOption(table.StyleName, _session.Workbook.Theme, out var option))
            return resize;

        return new CompositeWorkbookCommand("Resize Table", new IWorkbookCommand[]
        {
            resize,
            new ApplyStructuredTableStyleCommand(sheetId, table.Id, option.Banding),
        });
    }
}
