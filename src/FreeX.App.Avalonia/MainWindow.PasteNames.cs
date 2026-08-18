using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Paste Names dialog for the Avalonia/macOS shell (Formulas ▸ Use in Formula ▸ Paste Names). It lists the
/// workbook's defined names (Name | Refers To) and offers either pasting the selected name's reference into
/// the active cell or pasting the whole list as a two-column block. The portable projection and the
/// paste-list edit plan come from <see cref="PasteNamesPlanner"/>; edits run through the shared session
/// command path (undoable + refreshing). User-facing strings route through <see cref="UiText"/>.
/// </summary>
public sealed partial class MainWindow
{
    // ── Formulas ▸ Use in Formula entry point ──────────────────────────────────
    private void PasteNames() => RunGuarded(() => ShowPasteNamesDialogAsync());

    private async Task ShowPasteNamesDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var commandPlan = DefinedNameUiPolicy.PlanUseInFormula(
            _session.Workbook,
            FormatRangeReferenceQualified,
            DefinedNameUiProfile.Avalonia);
        var items = commandPlan.Items;

        var dialog = new Window
        {
            Title = UiText.Get("PasteNames_Title"),
            Width = 380,
            Height = 300,
            MinWidth = 340,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PasteNamesDialog");

        var namesList = new ListBox { MinHeight = 150 };
        ApplyNamesListBoxStyle(namesList);
        AutomationProperties.SetAutomationId(namesList, "PasteNamesList");
        namesList.ItemsSource = items.Select(DefinedNameUiPolicy.FormatPasteNamesRow).ToList();
        if (items.Count > 0)
            namesList.SelectedIndex = 0;

        var warningText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "PasteNamesWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 72 };
        ApplyNamesButtonChrome(okButton, minWidth: 72, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "PasteNamesOkButton");
        var pasteListButton = new Button { Content = UiText.Get("PasteNames_PasteList"), MinWidth = 92 };
        ApplyNamesButtonChrome(pasteListButton, minWidth: 92);
        AutomationProperties.SetAutomationId(pasteListButton, "PasteNamesPasteListButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 72 };
        ApplyNamesButtonChrome(cancelButton, minWidth: 72);
        AutomationProperties.SetAutomationId(cancelButton, "PasteNamesCancelButton");

        void SyncButtonState()
        {
            var plan = DefinedNameUiPolicy.PlanPasteNamesSelection(items, namesList.SelectedIndex);
            okButton.IsEnabled = plan.CanInsertName;
            pasteListButton.IsEnabled = plan.CanPasteList;
        }

        namesList.SelectionChanged += (_, _) => SyncButtonState();
        SyncButtonState();

        void ShowWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        okButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            var plan = DefinedNameUiPolicy.PlanPasteNamesSelection(items, namesList.SelectedIndex);
            if (plan.SelectedItem is not { } item)
                return;

            if (!ApplyPasteNameReference(item))
                return;

            dialog.Close();
        };

        pasteListButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            if (!PasteNamesPlanner.TryBuildPasteListEdits(_session.SelectedRange.Start, items, out var edits, out var error))
            {
                ShowWarning(DescribePasteNamesListError(error));
                return;
            }

            var command = new EditCellsCommand(_session.ActiveSheet.Id, edits);
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                ShowWarning(result.ErrorMessage ?? DescribePasteNamesListError(PasteNamesListError.NoNames));
                return;
            }

            RefreshShell(UiText.Format("PasteNames_PastedList", items.Count));
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, pasteListButton, okButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new DockPanel
                {
                    Children =
                    {
                        WithDock(
                            new TextBlock { Text = UiText.Get("PasteNames_NamesLabel"), FontSize = 12, Foreground = HeaderForeground },
                            Dock.Top,
                            new Thickness(0, 0, 0, 8)),
                        WithDock(warningText, Dock.Bottom, new Thickness(0, 8, 0, 0)),
                        namesList,
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Pastes the selected defined name as a live "=Name" FORMULA into the active cell (matching
    /// Excel's Paste Name, which inserts a reference that re-evaluates if the name's target
    /// changes) through the session command path. Previously this wrote the name's current
    /// RefersTo address as static text, so the pasted cell never updated when the name was
    /// redefined and displayed a raw address instead of evaluating like a formula.
    /// </summary>
    private bool ApplyPasteNameReference(PasteNamesItem item)
    {
        var address = _session.SelectedRange.Start;
        var edits = new (CellAddress Address, Cell NewCell)[]
        {
            (address, Cell.FromFormula(item.Name)),
        };

        var command = new EditCellsCommand(_session.ActiveSheet.Id, edits);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Format("PasteNames_Pasted", item.Name));
            return false;
        }

        RefreshShell(UiText.Format("PasteNames_Pasted", item.Name));
        return true;
    }

    private static string DescribePasteNamesListError(PasteNamesListError error) =>
        UiText.Get(DefinedNameUiPolicy.GetPasteNamesListErrorResourceKey(error, DefinedNameUiProfile.Avalonia));
}
