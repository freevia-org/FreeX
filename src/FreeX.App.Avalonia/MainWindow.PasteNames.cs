using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
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
    private void PasteNames() => RunGuarded(ShowPasteNamesDialogAsync);

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
            // WPF PasteNamesDialog: Width = 380, Height = 300 (outer, resizable), MinWidth 340, MinHeight 260.
            Width = 380 - WpfResizableFrameWidth,
            Height = 300 - WpfResizableFrameHeight,
            MinWidth = 340 - WpfResizableFrameWidth,
            MinHeight = 260 - WpfResizableFrameHeight,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PasteNamesDialog");

        // WPF: a GridView with Name (130) | Refers To (210) columns.
        var namesList = new ListBox
        {
            MinHeight = 120,
            ItemTemplate = new FuncDataTemplate<PasteNamesItem>(
                (item, _) => BuildPasteNamesRowGrid(item), supportsRecycling: false),
        };
        ApplyNamesListBoxStyle(namesList);
        AutomationProperties.SetAutomationId(namesList, "PasteNamesList");
        AutomationProperties.SetName(namesList, UiText.Get("PasteNames_NamesAutomationName"));
        namesList.ItemsSource = items.ToList();
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

        // WPF: OK (72) | Paste List (92) | Cancel (72), 8px apart.
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

        // WPF: double-clicking a name accepts it like OK.
        namesList.DoubleTapped += (_, _) =>
        {
            if (okButton.IsEnabled)
                okButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        };

        // WPF: buttons.Margin = (0, 12, 0, 0); OK, Paste List, Cancel.
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { okButton, pasteListButton, cancelButton },
        };
        AvaloniaGrid.SetRow(buttonRow, 3);

        // WPF: Label { Padding = (0, 0, 0, 4) } above the list.
        var namesLabel = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("PasteNames_NamesLabel")),
            FontSize = 12,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var namesHeader = BuildPasteNamesColumnHeader();
        DockPanel.SetDock(namesHeader, Dock.Top);
        var namesPanel = new DockPanel { Children = { namesHeader, namesList } };
        AvaloniaGrid.SetRow(namesPanel, 1);

        warningText.Margin = new Thickness(0, 8, 0, 0);
        AvaloniaGrid.SetRow(warningText, 2);

        // WPF CreateContent: Grid { Margin = 16 } with rows Auto / * / Auto (label, list, buttons).
        dialog.Content = new AvaloniaGrid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Children = { namesLabel, namesPanel, warningText, buttonRow },
        };
        // WPF FocusInitialTarget: the list when it has names, otherwise Cancel.
        dialog.Opened += (_, _) =>
        {
            if (items.Count > 0)
                namesList.Focus();
            else
                cancelButton.Focus();
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

    private static AvaloniaGrid CreatePasteNamesColumnGrid() => new()
    {
        ColumnDefinitions =
        {
            new ColumnDefinition { Width = new GridLength(130) },
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
        },
    };

    private static Border BuildPasteNamesColumnHeader()
    {
        var grid = CreatePasteNamesColumnGrid();
        var keys = new[] { "NamedRange_Name", "NamedRange_RefersTo" };
        for (var i = 0; i < keys.Length; i++)
        {
            var header = new TextBlock
            {
                Text = StripDisplayMnemonic(UiText.Get(keys[i])),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Margin = new Thickness(6, 3),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            AvaloniaGrid.SetColumn(header, i);
            grid.Children.Add(header);
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush(213, 213, 213),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Child = grid,
        };
    }

    private static Control BuildPasteNamesRowGrid(PasteNamesItem item)
    {
        var grid = CreatePasteNamesColumnGrid();
        AutomationProperties.SetName(grid, DefinedNameUiPolicy.FormatPasteNamesRow(item));
        var values = new[] { item.Name, item.RefersTo };
        for (var i = 0; i < values.Length; i++)
        {
            var cell = new TextBlock
            {
                Text = values[i],
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Margin = new Thickness(2, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            };
            AvaloniaGrid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private static string DescribePasteNamesListError(PasteNamesListError error) =>
        UiText.Get(DefinedNameUiPolicy.GetPasteNamesListErrorResourceKey(error, DefinedNameUiProfile.Avalonia));
}
