using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
            // WPF ErrorCheckingDialog is a 720x420 *outer* resizable window; its client rectangle is
            // the planner's AvaloniaClient size, which is what an Avalonia window sizes.
            Width = ErrorCheckingDialogPlanner.AvaloniaClientWidth,
            Height = ErrorCheckingDialogPlanner.AvaloniaClientHeight,
            MinWidth = ErrorCheckingDialogPlanner.MinWidth - WpfResizableFrameWidth,
            MinHeight = ErrorCheckingDialogPlanner.MinHeight - WpfResizableFrameHeight,
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
        showStepsButton.Click += (_, _) => RunGuarded(ShowCalculationStepsForSelectedAsync);
        sideIgnoreButton.Click += (_, _) => IgnoreSelected();
        editFormulaButton.Click += (_, _) => NavigateSelected();
        goToButton.Click += (_, _) => NavigateSelected();
        previousButton.Click += (_, _) => MoveSelection(-1);
        nextButton.Click += (_, _) => MoveSelection(1);
        ignoreButton.Click += (_, _) => IgnoreSelected();
        traceButton.Click += (_, _) => TraceSelected();
        optionsButton.Click += (_, _) => RunGuarded(ShowOptionsDialogAsync);
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
        // WPF ErrorCheckingDialog is a plain Window: the help group caption is normal ink.
        ApplyPlainWindowGroupBoxChrome(actionPanel, ErrorCheckingDialogChromeStyle);

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
        var headerStrip = new Border { ClipToBounds = true, Child = headerRow };
        DockPanel.SetDock(headerStrip, Dock.Top);

        // The fixed-width columns are wider than the list. Like the WPF ListView (ScrollViewer
        // default Auto), the rows scroll horizontally under a classic bar, and the column header
        // follows the horizontal offset while staying above the rows.
        var rowsScroller = new ScrollViewer
        {
            Content = rowsPanel,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        rowsScroller.ScrollChanged += (_, _) =>
            headerRow.RenderTransform = new TranslateTransform(-rowsScroller.Offset.X, 0);

        var listPanel = new DockPanel();
        listPanel.Children.Add(CreateErrorCheckingLabel());
        listPanel.Children.Add(new Border
        {
            BorderBrush = Brush(171, 173, 179),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            // The header and rows are clipped at the list frame instead of drawing over the Error
            // help group.
            ClipToBounds = true,
            Child = new DockPanel
            {
                ClipToBounds = true,
                Children =
                {
                    headerStrip,
                    rowsScroller,
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
        // The content fills the client area (and follows a resize), as the WPF DockPanel does.
        dialog.Content = new Border
        {
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

        // The selected row turns grey when the list loses the keyboard focus, as a WPF ListView does.
        rowsPanel.GotFocus += (_, _) => RenderRows();
        rowsPanel.LostFocus += (_, _) => RenderRows();

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
            // WPF ListViewItem: a 1px frame (transparent unless selected) around a 22px row, in the
            // shared dialog list selection colours (grey while the list does not hold the focus).
            var active = rowsPanel.IsKeyboardFocusWithin;
            var row = new Border
            {
                Height = ErrorCheckingWpfRowHeight,
                Background = selected
                    ? (active ? DialogWindowDefaults.SelectedRowFace : DialogWindowDefaults.InactiveRowFace)
                    : Brushes.White,
                BorderBrush = selected
                    ? (active ? DialogWindowDefaults.SelectedRowFrame : DialogWindowDefaults.InactiveRowFrame)
                    : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                // WPF ListViewItem rows start 1 px inside the list frame, and their GridView columns
                // start where the header's do (3 px inside the frame).
                Margin = new Thickness(ErrorCheckingWpfRowInset, 0, 0, 0),
                Child = CreateIssueRowGrid(issue),
            };
            row.PointerPressed += (_, args) =>
            {
                selectedIndex = index;
                rowsPanel.Focus();
                NavigateSelected();
                RenderRows();
                UpdateCommandStates();
                args.Handled = true;
            };
            row.DoubleTapped += (_, _) => NavigateSelected();
            return row;
        }
    }

    // WPF ListViewItem rows at the 12px dialog font are 22px apart (frame included); the GridView
    // column header row is 24px.
    private const double ErrorCheckingWpfRowHeight = 22;
    private const double ErrorCheckingWpfHeaderHeight = 24;
    // WPF Aero2 GridViewColumnHeader at the 12 px dialog font: a 20 px white-to-grey face over a
    // 1 px #D5D5D5 line, each column closed by a 1 px grey gripper line.
    private const double ErrorCheckingWpfHeaderFaceHeight = 20;
    // The GridView columns start 3 px inside the list frame; a row frame starts 1 px inside it.
    private const double ErrorCheckingWpfColumnInset = 3;
    private const double ErrorCheckingWpfRowInset = 1;
    // WPF ListView item text renders in the list's dark navy item ink.
    private static readonly IBrush ErrorCheckingWpfRowInk = new ImmutableSolidColorBrush(Color.FromRgb(0x04, 0x22, 0x71));

    private static Grid CreateIssueRowGrid(FormulaErrorIssue issue)
    {
        var row = CreateErrorCheckingColumnGrid(minHeight: 0);
        row.Margin = new Thickness(ErrorCheckingWpfColumnInset - ErrorCheckingWpfRowInset - 1, 0, 0, 0);
        AddErrorCheckingCell(row, issue.SheetName, 0);
        AddErrorCheckingCell(row, issue.Cell, 1);
        AddErrorCheckingCell(row, issue.ErrorCode, 2);
        AddErrorCheckingCell(row, issue.FormulaText ?? string.Empty, 3);
        AddErrorCheckingCell(row, issue.Description, 4);
        foreach (var cell in row.Children.OfType<TextBlock>())
            cell.Foreground = ErrorCheckingWpfRowInk;
        return row;
    }

    private static Grid CreateErrorCheckingHeaderRow()
    {
        var row = CreateErrorCheckingColumnGrid(ErrorCheckingWpfHeaderHeight);
        row.ClipToBounds = true;
        row.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        // WPF GridView column headers centre their captions over the column.
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.SheetColumnHeaderKey), 0, isHeader: true);
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.CellColumnHeaderKey), 1, isHeader: true);
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.IssueColumnHeaderKey), 2, isHeader: true);
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.FormulaColumnHeaderKey), 3, isHeader: true);
        AddErrorCheckingCell(row, UiText.Get(ErrorCheckingDialogPlanner.DescriptionColumnHeaderKey), 4, isHeader: true);

        // The Aero2 header face: 20 px of white fading to a faint grey, a #D5D5D5 bottom line, and
        // a gripper line closing each column (the first pixel of the next column).
        row.MinHeight = ErrorCheckingWpfHeaderFaceHeight + 1;
        row.Height = ErrorCheckingWpfHeaderFaceHeight + 1;
        row.Margin = new Thickness(ErrorCheckingWpfColumnInset, 0, 0, 0);
        row.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.4),
                new GradientStop(Color.FromRgb(0xF7, 0xF8, 0xF9), 1),
            },
        };
        foreach (var cell in row.Children.OfType<TextBlock>())
            cell.Margin = new Thickness(4, 0);
        var bottomLine = new Border
        {
            Height = 1,
            VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
            Background = new ImmutableSolidColorBrush(Color.FromRgb(0xD5, 0xD5, 0xD5)),
        };
        Grid.SetColumnSpan(bottomLine, 5);
        row.Children.Add(bottomLine);
        for (var column = 0; column < 5; column++)
        {
            var gripper = new Border
            {
                Width = 1,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                Margin = new Thickness(0, 2, -1, 1),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0xF0, 0xF0, 0xF0), 0),
                        new GradientStop(Color.FromRgb(0xD7, 0xD7, 0xD7), 1),
                    },
                },
            };
            Grid.SetColumn(gripper, column);
            row.Children.Add(gripper);
        }

        return row;
    }

    private static Grid CreateErrorCheckingColumnGrid(double minHeight) =>
        new()
        {
            MinHeight = minHeight,
            ColumnDefinitions = new ColumnDefinitions(
                $"{ErrorCheckingDialogPlanner.SheetColumnWidth}," +
                $"{ErrorCheckingDialogPlanner.CellColumnWidth}," +
                $"{ErrorCheckingDialogPlanner.IssueColumnWidth}," +
                $"{ErrorCheckingDialogPlanner.FormulaColumnWidth}," +
                $"{ErrorCheckingDialogPlanner.DescriptionColumnWidth}"),
        };

    private static void AddErrorCheckingCell(Grid grid, string text, int column, bool isHeader = false)
    {
        var cell = new TextBlock
        {
            Text = text,
            // WPF GridView column headers and cells are both regular weight.
            FontWeight = FontWeight.Normal,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            TextAlignment = isHeader ? TextAlignment.Center : TextAlignment.Left,
            // WPF clips a GridView cell at the column's trailing edge instead of trimming it.
            TextTrimming = isHeader ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            ClipToBounds = true,
            // WPF GridView cells inset their text 6px from the leading column edge and keep the
            // rest of the column for the text.
            Margin = isHeader ? new Thickness(4, 2) : new Thickness(6, 2, 0, 2),
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
        // WPF: every Error Checking button is Height = ErrorCheckingDialogPlanner.ButtonHeight.
        button.Height = ErrorCheckingDialogPlanner.ButtonHeight;
        button.MinHeight = ErrorCheckingDialogPlanner.ButtonHeight;
        button.MaxHeight = ErrorCheckingDialogPlanner.ButtonHeight;
        return button;
    }

}
