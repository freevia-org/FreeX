using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.PageLayout;
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
    // editable fields persisted via SetPageSetupCommand (see PageSetupDialogFields /
    // PageSetupDialogModel). We therefore present the same two-checkbox popup Excel does
    // (View / Print), so the Page Layout buttons control BOTH the view setting and the
    // print setting, with undo/redo for the print half (the view half routes through the
    // existing session toggles which also support undo).

    /// <summary>
    /// Page Layout ▸ Sheet Options ▸ Gridlines. Two-checkbox popup: View + Print.
    /// View half reuses SetShowGridlines; Print half rebuilds the page setup via
    /// PageSetupDialogModel.TryBuildCommand so it participates in undo/redo.
    /// </summary>
    private async Task ShowGridlinesSheetOptionsAsync() =>
        await ShowSheetOptionTwoToggleAsync(
            title: "Gridlines",
            label: "Gridlines",
            getView: () => _session.IsShowingGridlines,
            getPrint: () => _session.ActiveSheet.PrintGridlines,
            setView: showView =>
            {
                if (showView == _session.IsShowingGridlines)
                    return true;
                var result = _session.SetShowGridlines(showView);
                if (!result.Success)
                    ShowEditIssue(result.ErrorMessage ?? "Gridlines failed.");
                return result.Success;
            },
            buildPrintFields: (fields, print) => fields with { PrintGridlines = print });

    /// <summary>
    /// Page Layout ▸ Sheet Options ▸ Headings. Two-checkbox popup: View + Print.
    /// </summary>
    private async Task ShowHeadingsSheetOptionsAsync() =>
        await ShowSheetOptionTwoToggleAsync(
            title: "Headings",
            label: "Headings",
            getView: () => _session.IsShowingHeadings,
            getPrint: () => _session.ActiveSheet.PrintHeadings,
            setView: showView =>
            {
                if (showView == _session.IsShowingHeadings)
                    return true;
                var result = _session.SetShowHeadings(showView);
                if (!result.Success)
                {
                    ShowEditIssue(result.ErrorMessage ?? "Headings failed.");
                    return false;
                }

                RefreshViewportSizeForZoom();
                return true;
            },
            buildPrintFields: (fields, print) => fields with { PrintHeadings = print });

    private async Task ShowSheetOptionTwoToggleAsync(
        string title,
        string label,
        Func<bool> getView,
        Func<bool> getPrint,
        Func<bool, bool> setView,
        Func<PageSetupDialogFields, bool, PageSetupDialogFields> buildPrintFields)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var viewCheck = new CheckBox { Content = "View", IsChecked = getView() };
        AutomationProperties.SetAutomationId(viewCheck, "SheetOptionViewCheck");
        var printCheck = new CheckBox { Content = "Print", IsChecked = getPrint() };
        AutomationProperties.SetAutomationId(printCheck, "SheetOptionPrintCheck");

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 84, Padding = new Thickness(10, 4) };
        AutomationProperties.SetAutomationId(ok, "SheetOptionOkButton");
        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 84,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(8, 0, 0, 0),
        };

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
                    new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
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
            var fields = buildPrintFields(PageSetupDialogModel.FromSheet(sheet), wantPrint);
            var build = PageSetupDialogModel.TryBuildCommand(sheet, fields);
            if (!build.Success)
            {
                ShowEditIssue(build.Error ?? "Could not update print options.");
                return;
            }

            var result = _session.ExecuteReviewCommand(build.Command!);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? "Could not update print options.");
                return;
            }
        }

        RefreshShell($"{label}: View {(wantView ? "on" : "off")}, Print {(wantPrint ? "on" : "off")}");
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
        AutomationProperties.SetAutomationId(listBox, "ShowNotesList");
        listBox.ItemsSource = notes.Select(FormatNoteRow).ToList();

        var emptyText = new TextBlock
        {
            Text = "There are no notes or comments on this sheet.",
            Foreground = Brush(110, 110, 110),
            IsVisible = notes.Count == 0,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var goToButton = new Button { Content = "Go To", MinWidth = 84, IsEnabled = false };
        AutomationProperties.SetAutomationId(goToButton, "ShowNotesGoToButton");
        var closeButton = new Button
        {
            Content = "Close",
            IsCancel = true,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
        };
        AutomationProperties.SetAutomationId(closeButton, "ShowNotesCloseButton");

        var dialog = new Window
        {
            Title = "Notes",
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
            RefreshShell($"Selected {FormatCellReference(notes[index].Address)}");
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
        var kind = entry.IsThreaded ? "Comment" : "Note";
        var author = string.IsNullOrWhiteSpace(entry.Author) ? "" : $" — {entry.Author}";
        var body = (entry.Text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return $"{cellRef} [{kind}]{author}: {body}";
    }

    private readonly record struct SheetNoteEntry(CellAddress Address, string? Author, string? Text, bool IsThreaded);
}
