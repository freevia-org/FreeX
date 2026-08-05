using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PageLayout;
using Free.Shared.Shell.Avalonia;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // -------------------------------------------------------------------------------------------------------
    // Page break menu dialog chrome helpers
    // -------------------------------------------------------------------------------------------------------

    private static void ApplyPageBreakButtonChrome(Button button, double minWidth = 84, bool isDefault = false)
    {
        AvaloniaCompactDialogChrome.ApplyButton(button, PageLayoutDialogChromeStyle, minWidth, isDefault);
    }

    // Page Layout ▸ Breaks (parity gap: the ribbon button previously opened Page Setup as a stub).
    // Excel exposes a small dropdown with Insert Page Break / Remove Page Break / Reset All Page
    // Breaks. We surface the same three actions in a compact popup. The portable planner translates
    // the selected range into final break sets, then the shell runs the shared command through undo/redo.

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
        ApplyPageBreakButtonChrome(insertButton);
        AutomationProperties.SetAutomationId(insertButton, "PageBreakInsertButton");

        var removeButton = new Button
        {
            Content = UiText.Get("PageBreak_RemovePageBreak"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
        };
        ApplyPageBreakButtonChrome(removeButton);
        AutomationProperties.SetAutomationId(removeButton, "PageBreakRemoveButton");

        var resetButton = new Button
        {
            Content = UiText.Get("PageBreak_ResetAllPageBreaks"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
        };
        ApplyPageBreakButtonChrome(resetButton);
        AutomationProperties.SetAutomationId(resetButton, "PageBreakResetAllButton");

        var cancelButton = new Button
        {
            Content = UiText.Get("MoveCopySheet_Cancel"),
            IsCancel = true,
            MinWidth = 84,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        ApplyPageBreakButtonChrome(cancelButton, minWidth: 84);
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
            ApplyPageBreakAction(PageBreakMenuAction.Insert);
            dialog.Close();
        };
        removeButton.Click += (_, _) =>
        {
            ApplyPageBreakAction(PageBreakMenuAction.Remove);
            dialog.Close();
        };
        resetButton.Click += (_, _) =>
        {
            ApplyPageBreakAction(PageBreakMenuAction.ResetAll);
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiText.Get("PageBreak_Menu"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
                insertButton,
                removeButton,
                resetButton,
                cancelButton,
            },
        };

        await dialog.ShowDialog(this);
    }

    private void ApplyPageBreakAction(PageBreakMenuAction action)
    {
        var sheet = _session.ActiveSheet;
        var plan = CreatePageLayoutCommandSession().PlanPageBreakAction(
            action,
            _session.SelectedRange,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);

        var result = _session.ExecuteReviewCommand(plan.Command);
        RefreshShell(result.Success
            ? plan.SuccessStatusText ?? UiText.Get("PageBreak_Failed")
            : result.ErrorMessage ?? UiText.Get("PageBreak_Failed"));
    }
}
