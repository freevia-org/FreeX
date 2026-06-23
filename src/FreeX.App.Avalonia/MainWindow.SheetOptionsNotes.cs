using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Page Layout ▸ Sheet Options ▸ Gridlines / Headings, and Review ▸ Show Notes.
    //
    // In Excel the Page Layout "Gridlines" and "Headings" Sheet Options expose two
    // sub-toggles each (View + Print). The View tab already wires the on-screen view
    // toggles (view.gridlines -> ToggleShowGridlines, view.headings -> ToggleShowHeadings,
    // backed by WorkbookSession.SetShowGridlines / SetShowHeadings).
    //
    // The print side IS modeled: Sheet.PrintGridlines / Sheet.PrintHeadings are real,
    // editable fields persisted via SetPrintOptionsCommand. We therefore present the same
    // two-checkbox popup Excel does (View / Print), so the Page Layout buttons control BOTH
    // the view setting and the print setting, with undo/redo for the print half (the view
    // half routes through the existing session toggles which also support undo).

    /// <summary>
    /// Page Layout ▸ Sheet Options ▸ Gridlines. Two-checkbox popup: View + Print.
    /// View half reuses SetShowGridlines; Print half routes through a narrow print-options command
    /// so it participates in undo/redo without rebuilding the full Page Setup state.
    /// </summary>
    private async Task ShowGridlinesSheetOptionsAsync() =>
        await ShowSheetOptionTwoToggleAsync(
            title: UiText.Get("ShellLoc_GridlinesTitle"),
            label: UiText.Get("ShellLoc_GridlinesTitle"),
            getView: () => _session.IsShowingGridlines,
            getPrint: () => _session.ActiveSheet.PrintGridlines,
            setView: showView =>
            {
                if (showView == _session.IsShowingGridlines)
                    return true;
                var result = _session.SetShowGridlines(showView);
                if (!result.Success)
                    ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_GridlinesFailed"));
                return result.Success;
            },
            buildPrintCommand: (sheet, print) => PageLayoutRibbonCommandPlanner.BuildPrintGridlinesCommand(sheet, print));

    /// <summary>
    /// Page Layout ▸ Sheet Options ▸ Headings. Two-checkbox popup: View + Print.
    /// </summary>
    private async Task ShowHeadingsSheetOptionsAsync() =>
        await ShowSheetOptionTwoToggleAsync(
            title: UiText.Get("ShellLoc_HeadingsTitle"),
            label: UiText.Get("ShellLoc_HeadingsTitle"),
            getView: () => _session.IsShowingHeadings,
            getPrint: () => _session.ActiveSheet.PrintHeadings,
            setView: showView =>
            {
                if (showView == _session.IsShowingHeadings)
                    return true;
                var result = _session.SetShowHeadings(showView);
                if (!result.Success)
                {
                    ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_HeadingsFailed"));
                    return false;
                }

                RefreshViewportSizeForZoom();
                return true;
            },
            buildPrintCommand: (sheet, print) => PageLayoutRibbonCommandPlanner.BuildPrintHeadingsCommand(sheet, print));

    private async Task ShowSheetOptionTwoToggleAsync(
        string title,
        string label,
        Func<bool> getView,
        Func<bool> getPrint,
        Func<bool, bool> setView,
        Func<Sheet, bool, IWorkbookCommand> buildPrintCommand)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var viewCheck = new CheckBox { Content = UiText.Get("ShellLoc_SheetOptionView"), IsChecked = getView() };
        ApplySheetOptionCheckBoxChrome(viewCheck);
        AutomationProperties.SetAutomationId(viewCheck, "SheetOptionViewCheck");
        var printCheck = new CheckBox { Content = UiText.Get("ShellLoc_SheetOptionPrint"), IsChecked = getPrint() };
        ApplySheetOptionCheckBoxChrome(printCheck);
        AutomationProperties.SetAutomationId(printCheck, "SheetOptionPrintCheck");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplySheetOptionButtonChrome(ok, 84, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "SheetOptionOkButton");
        var cancel = new Button
        {
            Content = UiText.Get("Common_Cancel"),
            IsCancel = true,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ApplySheetOptionButtonChrome(cancel, 84);

        var dialog = new Window
        {
            Title = title,
            Width = 280,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 8) },
                    viewCheck,
                    printCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                        Margin = new Thickness(0, 14, 0, 0),
                        Children = { ok, cancel },
                    },
                },
            },
        };
        AutomationProperties.SetAutomationId(dialog, "SheetOptionDialog");

        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        var wantView = viewCheck.IsChecked == true;
        var wantPrint = printCheck.IsChecked == true;

        // View half via the existing session toggles.
        if (wantView != getView() && !setView(wantView))
            return;

        // Print half via a rebuilt page-setup command (undo/redo aware).
        if (wantPrint != getPrint())
        {
            var sheet = _session.ActiveSheet;
            var result = _session.ExecuteReviewCommand(buildPrintCommand(sheet, wantPrint));
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotUpdatePrintOptions"));
                return;
            }
        }

        RefreshShell(UiText.Format(
            "ShellLoc_SheetOptionStatus",
            label,
            wantView ? UiText.Get("ShellLoc_OnState") : UiText.Get("ShellLoc_OffState"),
            wantPrint ? UiText.Get("ShellLoc_OnState") : UiText.Get("ShellLoc_OffState")));
    }

    /// <summary>
    /// Review ▸ Show Notes — list every legacy note and threaded comment on the active
    /// sheet (cell ref + author + text). Double-click a row or use Go To to select that cell.
    /// Notes are enumerated from Sheet.Comments / Sheet.CommentAuthors and threaded comments
    /// from Sheet.ThreadedComments (FreeX.Core.Model).
    /// </summary>
    private async Task ShowNotesListAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var sheet = _session.ActiveSheet;
        var notes = CollectSheetNotes(sheet);

        var listBox = new ListBox { MinHeight = 240, MinWidth = 420 };
        ApplySheetOptionListBoxStyle(listBox);
        AutomationProperties.SetAutomationId(listBox, "ShowNotesList");
        listBox.ItemsSource = notes.Select(FormatNoteRow).ToList();

        var emptyText = new TextBlock
        {
            Text = UiText.Get("ShellLoc_NoNotesOnSheet"),
            Foreground = Brush(110, 110, 110),
            IsVisible = notes.Count == 0,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };

        var goToButton = new Button { Content = UiText.Get("ShellLoc_GoToButton"), MinWidth = 84, IsEnabled = false };
        ApplySheetOptionButtonChrome(goToButton, 84);
        AutomationProperties.SetAutomationId(goToButton, "ShowNotesGoToButton");
        var closeButton = new Button
        {
            Content = UiText.Get("Common_Close"),
            IsCancel = true,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ApplySheetOptionButtonChrome(closeButton, 84);
        AutomationProperties.SetAutomationId(closeButton, "ShowNotesCloseButton");

        var dialog = new Window
        {
            Title = UiText.Get("ShellLoc_NotesTitle"),
            Width = 520,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ShowNotesDialog");

        void GoToSelected()
        {
            var index = listBox.SelectedIndex;
            if (index < 0 || index >= notes.Count)
                return;

            _session.SelectCell(notes[index].Address);
            RefreshShell(UiText.Format("ShellLoc_SelectedCell", FormatCellReference(notes[index].Address)));
            dialog.Close();
        }

        listBox.SelectionChanged += (_, _) =>
            goToButton.IsEnabled = listBox.SelectedIndex >= 0 && listBox.SelectedIndex < notes.Count;
        listBox.DoubleTapped += (_, _) => GoToSelected();
        goToButton.Click += (_, _) => GoToSelected();
        closeButton.Click += (_, _) => dialog.Close();

        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { goToButton, closeButton },
        };
        DockPanel.SetDock(bottomRow, Dock.Bottom);
        DockPanel.SetDock(emptyText, Dock.Top);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children = { bottomRow, emptyText, listBox },
        };

        await dialog.ShowDialog(this);
    }

    private static List<SheetNoteEntry> CollectSheetNotes(Sheet sheet)
    {
        var entries = new List<SheetNoteEntry>();

        foreach (var (address, text) in sheet.Comments)
        {
            var author = sheet.CommentAuthors.TryGetValue(address, out var a) && !string.IsNullOrWhiteSpace(a)
                ? a
                : null;
            entries.Add(new SheetNoteEntry(address, author, text, IsThreaded: false));
        }

        foreach (var (address, comment) in sheet.ThreadedComments)
            entries.Add(new SheetNoteEntry(address, comment.Author, comment.Text, IsThreaded: true));

        entries.Sort(static (x, y) => x.Address.CompareTo(y.Address));
        return entries;
    }

    private static string FormatNoteRow(SheetNoteEntry entry)
    {
        var cellRef = FormatCellReference(entry.Address);
        var kind = entry.IsThreaded ? UiText.Get("ShellLoc_NoteKindComment") : UiText.Get("ShellLoc_NoteKindNote");
        var author = string.IsNullOrWhiteSpace(entry.Author) ? "" : $" — {entry.Author}";
        var body = (entry.Text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return $"{cellRef} [{kind}]{author}: {body}";
    }

    private readonly record struct SheetNoteEntry(CellAddress Address, string? Author, string? Text, bool IsThreaded);

    // ── Visual chrome helpers (SheetOptions / ShowNotes dialogs) ─────────────

    /// <summary>
    /// Applies standard SheetOption-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// </summary>
    private static void ApplySheetOptionButtonChrome(Button button, double minWidth, bool isDefault = false)
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
    /// Applies standard SheetOption-dialog check-box chrome (MinHeight=20, MaxHeight=20, FontSize=12).
    /// </summary>
    private static void ApplySheetOptionCheckBoxChrome(CheckBox checkBox)
    {
        checkBox.FontSize = 12;
        checkBox.FontFamily = FormulaBarFontFamily;
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
    }

    /// <summary>
    /// Applies standard ShowNotes list-box row chrome (MinHeight=24 per row, FontSize=12).
    /// </summary>
    private static void ApplySheetOptionListBoxStyle(ListBox listBox)
    {
        listBox.FontSize = 12;
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 1)),
                new Setter(Layoutable.MinHeightProperty, 24.0),
                new Setter(TemplatedControl.FontSizeProperty, 12.0),
            },
        });
    }
}
