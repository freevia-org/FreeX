using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PageLayout;
using Free.Shared.Shell.Avalonia;

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

    // Page Layout ▸ Breaks. Mirrors the WPF PageBreakDialog: three radio choices (insert a row
    // break, insert a column break, reset all breaks) with a row-number and a column-number entry,
    // only the entry of the checked choice enabled. PageBreakDialogPlanner owns the default seeding,
    // the validation and the final break sets; the shell runs the shared command through undo/redo.
    // The Insert / Remove / Reset All ribbon menu commands keep routing through ApplyPageBreakAction.

    /// <summary>Opens the Page Breaks dialog (the WPF PageBreakDialog shape).</summary>
    private void ShowPageBreaksMenu() => RunGuarded(ShowPageBreaksMenuAsync);

    private async Task ShowPageBreaksMenuAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var result = await ShowPageBreakDialogAsync(
            PageBreakDialogPlanner.BuildDefaultInput(_session.SelectedRange));
        if (result is null)
            return;

        ApplyPageBreakDialogResult(result);
    }

    private async Task<PageBreakDialogResult?> ShowPageBreakDialogAsync(string defaultValue)
    {
        // WPF SeedDefault: an unparsable default falls back to a row break at row 2.
        if (!PageBreakDialogPlanner.TryCreateResult(defaultValue, out var seed))
            seed = PageBreakDialogPlanner.CreateRowResult(2);

        var insertRowButton = new RadioButton
        {
            Content = UiText.Get("PageBreak_InsertRowPageBreak"),
            GroupName = "PageBreakAction",
            IsChecked = seed.Action == PageBreakDialogAction.AddRow,
        };
        ApplyPageLayoutRadioButtonChrome(insertRowButton);
        AutomationProperties.SetAutomationId(insertRowButton, "PageBreakInsertRowButton");

        var insertColumnButton = new RadioButton
        {
            Content = UiText.Get("PageBreak_InsertColumnPageBreak"),
            GroupName = "PageBreakAction",
            IsChecked = seed.Action == PageBreakDialogAction.AddColumn,
        };
        ApplyPageLayoutRadioButtonChrome(insertColumnButton);
        AutomationProperties.SetAutomationId(insertColumnButton, "PageBreakInsertColumnButton");

        var resetAllButton = new RadioButton
        {
            Content = UiText.Get("PageBreak_ResetAllPageBreaks"),
            GroupName = "PageBreakAction",
            IsChecked = seed.Action == PageBreakDialogAction.Clear,
            Margin = new Thickness(0, 4, 0, 12),
        };
        ApplyPageLayoutRadioButtonChrome(resetAllButton);
        AutomationProperties.SetAutomationId(resetAllButton, "PageBreakResetAllButton");

        var rowBreakBox = new TextBox
        {
            Text = (seed.RowBreak ?? 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Width = 96,
        };
        ApplyPageLayoutTextBoxChrome(rowBreakBox);
        AutomationProperties.SetName(rowBreakBox, UiText.Get("PageBreak_RowPageBreak"));
        AutomationProperties.SetAutomationId(rowBreakBox, "PageBreakRowBreakBox");
        AutomationProperties.SetHelpText(
            rowBreakBox,
            UiText.Get("PageBreak_EnterTheRowNumberWhereTheHorizontalPageBreakShouldBeInserted"));

        var columnBreakBox = new TextBox
        {
            Text = (seed.ColumnBreak ?? 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Width = 96,
        };
        ApplyPageLayoutTextBoxChrome(columnBreakBox);
        AutomationProperties.SetName(columnBreakBox, UiText.Get("PageBreak_ColumnPageBreak"));
        AutomationProperties.SetAutomationId(columnBreakBox, "PageBreakColumnBreakBox");
        AutomationProperties.SetHelpText(
            columnBreakBox,
            UiText.Get("PageBreak_EnterTheColumnNumberOrLetterWhereTheVerticalPageBreakShouldBeInserted"));

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPageBreakButtonChrome(okButton, minWidth: 72, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "PageBreakOkButton");

        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPageBreakButtonChrome(cancelButton, minWidth: 72);
        AutomationProperties.SetAutomationId(cancelButton, "PageBreakCancelButton");

        // WPF: Width 360, Height 240, NoResize; the height follows the content here because the
        // compact Avalonia rows are not pixel-identical to the WPF ones.
        var dialog = new Window
        {
            Title = UiText.Get("PageBreak_PageBreaks"),
            Width = 360,
            MinWidth = 360,
            MaxWidth = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PageBreakDialog");

        void UpdateBreakInputAvailability()
        {
            rowBreakBox.IsEnabled = insertRowButton.IsChecked == true;
            columnBreakBox.IsEnabled = insertColumnButton.IsChecked == true;
        }

        insertRowButton.IsCheckedChanged += (_, _) => UpdateBreakInputAvailability();
        insertColumnButton.IsCheckedChanged += (_, _) => UpdateBreakInputAvailability();
        resetAllButton.IsCheckedChanged += (_, _) => UpdateBreakInputAvailability();
        UpdateBreakInputAvailability();

        PageBreakDialogResult? accepted = null;
        var showingWarning = false;

        async Task AcceptAsync()
        {
            if (showingWarning)
                return;

            var action = resetAllButton.IsChecked == true
                ? PageBreakDialogAction.Clear
                : insertColumnButton.IsChecked == true
                    ? PageBreakDialogAction.AddColumn
                    : PageBreakDialogAction.AddRow;

            if (!PageBreakDialogPlanner.TryCreateResult(
                    action,
                    rowBreakBox.Text ?? string.Empty,
                    columnBreakBox.Text ?? string.Empty,
                    out var result))
            {
                var invalidBox = action == PageBreakDialogAction.AddColumn ? columnBreakBox : rowBreakBox;
                showingWarning = true;
                try
                {
                    await ShowPageBreakWarningAsync(
                        dialog,
                        UiText.Get(action == PageBreakDialogAction.AddColumn
                            ? "PageBreak_EnterAColumnNumberOrLetterWithinTheWorksheetForThePageBreak"
                            : "PageBreak_EnterARowNumberWithinTheWorksheetForThePageBreak"));
                }
                finally
                {
                    showingWarning = false;
                }

                AvaloniaCompactDialogChrome.FocusAndSelect(invalidBox);
                return;
            }

            accepted = result;
            dialog.Close();
        }

        okButton.Click += async (_, _) => await AcceptAsync();
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                insertRowButton,
                CreatePageBreakNumberRow("PageBreak_RowLabel", rowBreakBox),
                insertColumnButton,
                CreatePageBreakNumberRow("PageBreak_ColumnLabel", columnBreakBox),
                resetAllButton,
                AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]),
            },
        };

        // WPF FocusInitialKeyboardTarget: the entry of the checked choice (or the reset radio).
        dialog.Opened += (_, _) =>
        {
            dialog.UpdateLayout();
            if (resetAllButton.IsChecked == true)
                resetAllButton.Focus();
            else if (insertColumnButton.IsChecked == true)
                AvaloniaCompactDialogChrome.FocusAndSelect(columnBreakBox);
            else
                AvaloniaCompactDialogChrome.FocusAndSelect(rowBreakBox);
        };

        await dialog.ShowDialog(this);
        return accepted;
    }

    // WPF CreateNumberRow: a horizontal row indented 22px under its radio, a 72px label and a 96px box.
    private static StackPanel CreatePageBreakNumberRow(string labelResourceKey, TextBox box)
    {
        var label = PageSetupLabel(UiText.Get(labelResourceKey));
        label.Width = 72;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(22, 2, 0, 8),
            Children = { label, box },
        };
    }

    private static async Task ShowPageBreakWarningAsync(Window owner, string message)
    {
        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, IsCancel = true };
        ApplyPageBreakButtonChrome(okButton, minWidth: 72, isDefault: true);
        var warning = new Window
        {
            Title = owner.Title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                    },
                    AvaloniaCompactDialogChrome.CreateActionRow([okButton]),
                },
            },
        };
        AutomationProperties.SetAutomationId(warning, "PageBreakWarningDialog");
        okButton.Click += (_, _) => warning.Close();
        await warning.ShowDialog(owner);
    }

    private void ApplyPageBreakDialogResult(PageBreakDialogResult result)
    {
        var sheet = _session.ActiveSheet;
        var plan = PageBreakDialogPlanner.PlanPageBreaks(
            result,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);

        ExecutePageLayoutCommandWithShellRefresh(
            CreatePageLayoutCommandSession().PlanPageBreaks(plan));
    }

    private void ApplyPageBreakAction(PageBreakMenuAction action)
    {
        var sheet = _session.ActiveSheet;
        var plan = CreatePageLayoutCommandSession().PlanPageBreakAction(
            action,
            _session.SelectedRange,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);

        ExecutePageLayoutCommandWithShellRefresh(plan);
    }
}
