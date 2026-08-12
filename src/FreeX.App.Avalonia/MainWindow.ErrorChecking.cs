using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle ErrorCheckingDialogChromeStyle => new(FormulaBarFontFamily);

    private async Task CheckFormulaErrorsAsync()
    {
        _session.RecalculateWorkbook();

        var issues = FormulaAuditingService.FindFormulaErrorIssues(_session.Workbook, _session.ActiveSheet.Id, _session.CyclicCells);
        if (issues.Count == 0)
        {
            ShowEditIssue(UiText.Get(ErrorCheckingDialogPlanner.NoIssuesMessageKey));
            RefreshShell(UiText.Get(ErrorCheckingDialogPlanner.NoIssuesTitleKey));
            return;
        }

        await ShowErrorCheckingDialogAsync(issues);
    }

    private Task ShowErrorCheckingDialogAsync(IReadOnlyList<FormulaErrorIssue> sourceIssues)
    {
        var issues = sourceIssues.ToList();
        var selectedIndex = issues.Count > 0 ? 0 : -1;

        var dialog = new Window
        {
            Title = UiText.Get(ErrorCheckingDialogPlanner.TitleKey),
            Width = ErrorCheckingDialogPlanner.Width,
            Height = ErrorCheckingDialogPlanner.Height,
            MinWidth = ErrorCheckingDialogPlanner.MinWidth,
            MinHeight = ErrorCheckingDialogPlanner.MinHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, ErrorCheckingDialogChromeStyle);
        AutomationProperties.SetAutomationId(dialog, ErrorCheckingDialogPlanner.DialogAutomationId);

        var header = new TextBlock
        {
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var rowsPanel = new StackPanel { Focusable = true };
        AutomationProperties.SetAutomationId(rowsPanel, ErrorCheckingDialogPlanner.IssuesAutomationId);
        AutomationProperties.SetName(rowsPanel, UiText.Get(ErrorCheckingDialogPlanner.IssuesAutomationNameKey));

        var helpButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.HelpButtonKey);
        var showStepsButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.ShowCalculationStepsButtonKey);
        var sideIgnoreButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.IgnoreErrorButtonKey);
        var editFormulaButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.EditInFormulaBarButtonKey);
        var goToButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.GoToButtonKey, ErrorCheckingDialogPlanner.GoToButtonWidth);
        var previousButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.PreviousButtonKey, ErrorCheckingDialogPlanner.PreviousButtonWidth);
        var nextButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.NextButtonKey, ErrorCheckingDialogPlanner.NextButtonWidth);
        var ignoreButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.IgnoreErrorButtonKey, ErrorCheckingDialogPlanner.IgnoreButtonWidth);
        var traceButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.TraceErrorButtonKey, ErrorCheckingDialogPlanner.TraceButtonWidth);
        var optionsButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.OptionsButtonKey, ErrorCheckingDialogPlanner.OptionsButtonWidth);
        var closeButton = CreateErrorCheckingButton(ErrorCheckingDialogPlanner.CloseButtonKey, ErrorCheckingDialogPlanner.CloseButtonWidth);
        closeButton.IsCancel = true;

        helpButton.Click += (_, _) => ShowSelectedIssueHelp();
        showStepsButton.Click += async (_, _) => await ShowCalculationStepsForSelectedAsync();
        sideIgnoreButton.Click += (_, _) => IgnoreSelected();
        editFormulaButton.Click += (_, _) => NavigateSelected();
        goToButton.Click += (_, _) => NavigateSelected();
        previousButton.Click += (_, _) => MoveSelection(-1);
        nextButton.Click += (_, _) => MoveSelection(1);
        ignoreButton.Click += (_, _) => IgnoreSelected();
        traceButton.Click += (_, _) => TraceSelected();
        optionsButton.Click += async (_, _) => await ShowOptionsDialogAsync();
        closeButton.Click += (_, _) => dialog.Close();

        var actionStack = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get(ErrorCheckingDialogPlanner.ActionIntroTextKey),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 8),
                },
                helpButton,
                showStepsButton,
                sideIgnoreButton,
                editFormulaButton,
            },
        };

        var actionPanel = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get(ErrorCheckingDialogPlanner.HelpGroupHeaderKey)),
            Width = ErrorCheckingDialogPlanner.ActionPanelWidth,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8),
            Content = actionStack,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                goToButton,
                previousButton,
                nextButton,
                ignoreButton,
                traceButton,
                optionsButton,
                closeButton,
            },
        };

        var headerRow = CreateErrorCheckingHeaderRow();
        DockPanel.SetDock(headerRow, Dock.Top);

        var listPanel = new DockPanel();
        listPanel.Children.Add(CreateErrorCheckingLabel());
        listPanel.Children.Add(new Border
        {
            BorderBrush = Brush(171, 173, 179),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = new DockPanel
            {
                Children =
                {
                    headerRow,
                    new ScrollViewer
                    {
                        Content = rowsPanel,
                        // Disabled (not Auto) horizontal scrolling: the columns already truncate at the
                        // right edge, and Windows shows no horizontal scrollbar here — Auto rendered a
                        // stray black scrollbar bar under the issues list.
                        HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    },
                },
            },
        });

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(actionPanel, Dock.Right);
        DockPanel.SetDock(buttons, Dock.Bottom);

        var root = new DockPanel { Margin = new Thickness(ErrorCheckingDialogPlanner.RootMargin) };
        // Dock the bottom button bar BEFORE the right-hand action panel so the bar spans the full
        // dialog width (matching the WPF/Windows layout); the action panel then claims the right
        // edge above it and the issues list fills the remaining area.
        root.Children.Add(header);
        root.Children.Add(buttons);
        root.Children.Add(actionPanel);
        root.Children.Add(listPanel);
        dialog.Content = new Border
        {
            Width = ErrorCheckingDialogPlanner.AvaloniaClientWidth,
            Height = ErrorCheckingDialogPlanner.AvaloniaClientHeight,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Background = Brushes.White,
            Child = root,
        };

        rowsPanel.KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case Key.Up:
                    MoveSelection(-1);
                    args.Handled = true;
                    break;
                case Key.Down:
                    MoveSelection(1);
                    args.Handled = true;
                    break;
                case Key.Enter:
                    NavigateSelected();
                    args.Handled = true;
                    break;
            }
        };

        RefreshHeader();
        RenderRows();
        UpdateCommandStates();
        ShowOwnedModelessWindow(dialog, () =>
        {
            NavigateSelected();
            rowsPanel.Focus();
        });
        return Task.CompletedTask;

        Label CreateErrorCheckingLabel()
        {
            var label = new Label
            {
                Content = UiText.Get(ErrorCheckingDialogPlanner.IssuesLabelKey),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            };
            DockPanel.SetDock(label, Dock.Top);
            return label;
        }

        void RefreshHeader()
        {
            header.Text = UiText.Format(ErrorCheckingDialogPlanner.IssueCountHeaderKey, issues.Count);
        }

        void MoveSelection(int delta)
        {
            if (issues.Count == 0)
                return;

            selectedIndex = Math.Clamp(selectedIndex < 0 ? 0 : selectedIndex + delta, 0, issues.Count - 1);
            NavigateSelected();
            RenderRows();
            UpdateCommandStates();
        }

        void NavigateSelected()
        {
            if (GetSelectedIssue() is not { } issue)
                return;

            _session.SelectCell(issue.Address);
            RefreshShell(UiText.Format("MainLoc_SelectedX", issue.Cell));
        }

        void IgnoreSelected()
        {
            if (GetSelectedIssue() is not { } issue)
                return;

            var result = _session.ExecuteReviewCommand(
                new SetFormulaErrorIgnoredCommand(issue.SheetId, issue.Address, ignored: true),
                issue.Address);
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? UiText.Get(ErrorCheckingDialogPlanner.IgnoreErrorButtonKey));
                return;
            }

            issues.RemoveAll(candidate =>
                candidate.SheetId == issue.SheetId &&
                candidate.Address.Equals(issue.Address));
            RefreshHeader();
            if (issues.Count == 0)
            {
                dialog.Close();
                RefreshShell(UiText.Get(ErrorCheckingDialogPlanner.NoIssuesTitleKey));
                return;
            }

            selectedIndex = Math.Min(selectedIndex, issues.Count - 1);
            NavigateSelected();
            RenderRows();
            UpdateCommandStates();
        }

        async Task ShowCalculationStepsForSelectedAsync()
        {
            if (GetSelectedIssue() is not { } issue ||
                !ErrorCheckingDialogPlanner.HasCalculationSteps(issue))
            {
                return;
            }

            _session.SelectCell(issue.Address);
            RefreshShell(UiText.Format("MainLoc_SelectedX", issue.Cell));

            var summary = FormulaEvaluationSummaryService.GetSummary(_session.Workbook, issue.Address);
            if (summary is null)
            {
                ShowEditIssue(UiText.Get(EvaluateFormulaDialogPlanner.SelectFormulaMessageKey));
                return;
            }

            await ShowEvaluateFormulaDialogAsync(summary);
        }

        void TraceSelected()
        {
            if (GetSelectedIssue() is not { } issue)
                return;

            _session.SelectCell(issue.Address);
            TraceFormulaPrecedents();
        }

        void ShowSelectedIssueHelp()
        {
            var message = GetSelectedIssue() is { } issue
                ? UiText.Format(ErrorCheckingDialogPlanner.SelectedIssueHelpBodyKey, issue.ErrorCode, issue.Description)
                : UiText.Get(ErrorCheckingDialogPlanner.NoSelectionHelpBodyKey);

            ShowEditIssue(message);
        }

        FormulaErrorIssue? GetSelectedIssue() =>
            selectedIndex >= 0 && selectedIndex < issues.Count ? issues[selectedIndex] : null;

        void UpdateCommandStates()
        {
            var state = ErrorCheckingDialogPlanner.CreateCommandState(selectedIndex, issues.Count, GetSelectedIssue());
            helpButton.IsEnabled = state.HasSelection;
            showStepsButton.IsEnabled = state.CanShowCalculationSteps;
            sideIgnoreButton.IsEnabled = state.HasSelection;
            editFormulaButton.IsEnabled = state.HasSelection;
            goToButton.IsEnabled = state.HasSelection;
            ignoreButton.IsEnabled = state.HasSelection;
            traceButton.IsEnabled = state.HasSelection;
            previousButton.IsEnabled = state.CanPrevious;
            nextButton.IsEnabled = state.CanNext;
        }

        void RenderRows()
        {
            rowsPanel.Children.Clear();
            for (var i = 0; i < issues.Count; i++)
            {
                rowsPanel.Children.Add(CreateIssueRow(issues[i], i, i == selectedIndex));
            }
        }

        Border CreateIssueRow(FormulaErrorIssue issue, int index, bool selected)
        {
            var row = new Border
            {
                Background = selected ? Brush(199, 224, 244) : Brushes.White,
                BorderBrush = selected ? Brush(0, 120, 215) : Brushes.Transparent,
                BorderThickness = selected ? new Thickness(1) : new Thickness(0, 0, 0, 1),
                Child = CreateIssueRowGrid(issue),
            };
            row.PointerPressed += (_, args) =>
            {
                selectedIndex = index;
                NavigateSelected();
                RenderRows();
                UpdateCommandStates();
                args.Handled = true;
            };
            row.DoubleTapped += (_, _) => NavigateSelected();
            return row;
        }
    }

    private static Grid CreateIssueRowGrid(FormulaErrorIssue issue)
    {
        var row = CreateErrorCheckingColumnGrid();
        AddErrorCheckingCell(row, issue.SheetName, 0);
        AddErrorCheckingCell(row, issue.Cell, 1);
        AddErrorCheckingCell(row, issue.ErrorCode, 2);
        AddErrorCheckingCell(row, issue.FormulaText ?? string.Empty, 3);
        AddErrorCheckingCell(row, issue.Description, 4);
        return row;
    }

    private static Grid CreateErrorCheckingHeaderRow()
    {
        var row = CreateErrorCheckingColumnGrid();
        row.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.SheetColumnHeaderKey), 0, bold: true);
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.CellColumnHeaderKey), 1, bold: true);
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.IssueColumnHeaderKey), 2, bold: true);
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.FormulaColumnHeaderKey), 3, bold: true);
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.DescriptionColumnHeaderKey), 4, bold: true);
        return row;
    }

    private static Grid CreateErrorCheckingColumnGrid() =>
        new()
        {
            MinHeight = 24,
            ColumnDefinitions = new ColumnDefinitions(
                $"{ErrorCheckingDialogPlanner.SheetColumnWidth}," +
                $"{ErrorCheckingDialogPlanner.CellColumnWidth}," +
                $"{ErrorCheckingDialogPlanner.IssueColumnWidth}," +
                $"{ErrorCheckingDialogPlanner.FormulaColumnWidth}," +
                $"{ErrorCheckingDialogPlanner.DescriptionColumnWidth}"),
        };

    private static void AddErrorCheckingCell(Grid grid, string text, int column, bool bold = false)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 2),
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    /// <summary>
    /// Creates a shared compact-chrome Error Checking dialog button. Side-panel buttons (no explicit
    /// width) stretch full-width with bottom margin; bottom-bar buttons use the provided width with
    /// a left margin.
    /// </summary>
    private static Button CreateErrorCheckingButton(string contentKey, double? width = null)
    {
        var button = new Button
        {
            Content = UiText.Get(contentKey),
            Width = width ?? double.NaN,
            // Side-panel buttons (no explicit width) stretch to a uniform full-panel width so the
            // "Error help" action stack is not ragged; bottom-bar buttons keep their fixed width.
            HorizontalAlignment = width is null ? AvaloniaHorizontalAlignment.Stretch : AvaloniaHorizontalAlignment.Left,
            Margin = new Thickness(width is null ? 0 : 4, 0, 0, width is null ? 6 : 0),
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            ErrorCheckingDialogChromeStyle,
            width ?? 0);
        return button;
    }

}
