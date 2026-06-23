using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task CheckFormulaErrorsAsync()
    {
        _session.RecalculateWorkbook();

        var issues = FormulaAuditingService.FindFormulaErrorIssues(_session.Workbook, _session.ActiveSheet.Id);
        if (issues.Count == 0)
        {
            ShowEditIssue(UiText.Get(ErrorCheckingDialogPlanner.NoIssuesMessageKey));
            RefreshShell(UiText.Get(ErrorCheckingDialogPlanner.NoIssuesTitleKey));
            return;
        }

        await ShowErrorCheckingDialogAsync(issues);
    }

    private Task ShowErrorCheckingParityDialogAsync() =>
        ShowErrorCheckingDialogAsync(CreateErrorCheckingParityIssues(_session.ActiveSheet.Id));

    private async Task ShowErrorCheckingDialogAsync(IReadOnlyList<FormulaErrorIssue> sourceIssues)
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
        AutomationProperties.SetAutomationId(dialog, ErrorCheckingDialogPlanner.DialogAutomationId);

        var header = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        var rowsPanel = new StackPanel();
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
            Header = UiText.Get(ErrorCheckingDialogPlanner.HelpGroupHeaderKey),
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
                        HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    },
                },
            },
        });

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(actionPanel, Dock.Right);
        DockPanel.SetDock(buttons, Dock.Bottom);

        var root = new DockPanel { Margin = new Thickness(ErrorCheckingDialogPlanner.RootMargin) };
        root.Children.Add(header);
        root.Children.Add(actionPanel);
        root.Children.Add(buttons);
        root.Children.Add(listPanel);
        dialog.Content = root;

        RefreshHeader();
        RenderRows();
        UpdateCommandStates();
        dialog.Opened += (_, _) =>
        {
            NavigateSelected();
            rowsPanel.Focus();
        };

        await dialog.ShowDialog(this);

        Label CreateErrorCheckingLabel()
        {
            var label = new Label
            {
                Content = UiText.Get(ErrorCheckingDialogPlanner.IssuesLabelKey),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 4),
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
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 2),
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static Button CreateErrorCheckingButton(string contentKey, double? width = null) =>
        new()
        {
            Content = UiText.Get(contentKey),
            Width = width ?? double.NaN,
            Height = ErrorCheckingDialogPlanner.ButtonHeight,
            Margin = new Thickness(width is null ? 0 : 4, 0, 0, width is null ? 6 : 0),
        };

    private static IReadOnlyList<FormulaErrorIssue> CreateErrorCheckingParityIssues(SheetId sheetId) =>
    [
        new(
            sheetId,
            "Sheet1",
            new CellAddress(sheetId, 6, 4),
            "D6",
            ErrorValue.DivByZero.Code,
            "=D2/0",
            "Formula divides by zero."),
        new(
            sheetId,
            "Sheet1",
            new CellAddress(sheetId, 7, 4),
            "D7",
            FormulaAuditingService.FormulaStoredAsTextErrorCode,
            null,
            "The formula in this cell is stored as text."),
    ];
}
