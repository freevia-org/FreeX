using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Page Layout ▸ Breaks (parity gap: the ribbon button previously opened Page Setup as a stub).
    // Excel exposes a small dropdown with Insert Page Break / Remove Page Break / Reset All Page
    // Breaks. We surface the same three actions in a compact popup. The break math lives in the
    // portable PageBreakActionPlanner; each action writes the resulting break sets back through the
    // shared SetPageBreaksCommand (undo/redo aware), so manual breaks render in Page Break Preview.

    /// <summary>Opens the compact Breaks popup (Insert / Remove / Reset All page breaks).</summary>
    private void ShowPageBreaksMenu() => _ = ShowPageBreaksMenuAsync();

    private async Task ShowPageBreaksMenuAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var insertButton = new Button
        {
            Content = UiText.Get("PageBreak_InsertPageBreak"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(insertButton, "PageBreakInsertButton");

        var removeButton = new Button
        {
            Content = UiText.Get("PageBreak_RemovePageBreak"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(removeButton, "PageBreakRemoveButton");

        var resetButton = new Button
        {
            Content = UiText.Get("PageBreak_ResetAllPageBreaks"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(resetButton, "PageBreakResetAllButton");

        var cancelButton = new Button
        {
            Content = UiText.Get("MoveCopySheet_Cancel"),
            IsCancel = true,
            MinWidth = 84,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        AutomationProperties.SetAutomationId(cancelButton, "PageBreakCancelButton");

        var dialog = new Window
        {
            Title = UiText.Get("PageBreak_Menu"),
            Width = 280,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PageBreakMenuDialog");

        insertButton.Click += (_, _) =>
        {
            ApplyPageBreakAction(PageBreakAction.Insert);
            dialog.Close();
        };
        removeButton.Click += (_, _) =>
        {
            ApplyPageBreakAction(PageBreakAction.Remove);
            dialog.Close();
        };
        resetButton.Click += (_, _) =>
        {
            ApplyPageBreakAction(PageBreakAction.ResetAll);
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiText.Get("PageBreak_Menu"), FontWeight = FontWeight.SemiBold },
                insertButton,
                removeButton,
                resetButton,
                cancelButton,
            },
        };

        await dialog.ShowDialog(this);
    }

    private enum PageBreakAction
    {
        Insert,
        Remove,
        ResetAll,
    }

    private void ApplyPageBreakAction(PageBreakAction action)
    {
        var sheet = _session.ActiveSheet;
        var active = _session.SelectedRange.Start;

        var plan = action switch
        {
            PageBreakAction.Insert => PageBreakActionPlanner.Insert(active, sheet.RowPageBreaks, sheet.ColumnPageBreaks),
            PageBreakAction.Remove => PageBreakActionPlanner.Remove(active, sheet.RowPageBreaks, sheet.ColumnPageBreaks),
            _ => PageBreakActionPlanner.ResetAll(),
        };

        var result = _session.ExecuteReviewCommand(
            new SetPageBreaksCommand(sheet.Id, plan.RowBreaks, plan.ColumnBreaks));
        RefreshShell(result.Success
            ? plan.Status
            : result.ErrorMessage ?? UiText.Get("PageBreak_Failed"));
    }
}
