using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

// Excel's background error checking: the green triangle in the top-left corner of a cell that
// breaks an enabled error-checking rule (number stored as text, inconsistent formula, ...), and the
// warning smart tag beside the active cell that offers the fix, Ignore Error and the options.
public partial class MainWindow
{
    private readonly BackgroundErrorIndicatorCache _errorIndicatorCache = new();
    private Button? _errorSmartTagButton;
    private string? _errorSmartTagCode;

    private bool HasErrorIndicatorAt(uint row, uint col) =>
        GetErrorIndicatorCode(new CellAddress(_session.ActiveSheet.Id, row, col)) is not null;

    internal string? GetErrorIndicatorCode(CellAddress address) =>
        _errorIndicatorCache.GetErrorCode(_workbook, address);

    internal Button? ErrorSmartTagButtonForTest => _errorSmartTagButton;

    /// <summary>Shows the smart tag beside the active cell when it carries an error indicator.</summary>
    private void RefreshErrorSmartTag()
    {
        var activeCell = SheetGrid.ActiveCell ?? SheetGrid.SelectedRange?.Start;
        var code = activeCell is { } address && address.Sheet == _currentSheetId
            ? GetErrorIndicatorCode(address)
            : null;
        if (code is null || TryGetCellOverlayRect(activeCell!.Value) is not { } cellRect)
        {
            HideErrorSmartTag();
            return;
        }

        EnsureErrorSmartTagButton();
        _errorSmartTagCode = code;
        var zoom = _zoomLevel;
        var size = ErrorSmartTagPlanner.ButtonSize;
        var left = (cellRect.Left * zoom) - size - 2;
        if (left < 0)
            left = (cellRect.Right * zoom) + 2;

        Canvas.SetLeft(_errorSmartTagButton!, left);
        Canvas.SetTop(_errorSmartTagButton!, Math.Max(0, cellRect.Top * zoom));
        _errorSmartTagButton!.Visibility = Visibility.Visible;
    }

    private void HideErrorSmartTag()
    {
        _errorSmartTagCode = null;
        if (_errorSmartTagButton is { Visibility: not Visibility.Collapsed })
            _errorSmartTagButton.Visibility = Visibility.Collapsed;
    }

    private void EnsureErrorSmartTagButton()
    {
        if (_errorSmartTagButton is not null)
            return;

        // Excel's smart tag: a yellow warning diamond with an exclamation mark.
        var diamond = new Path
        {
            Data = Geometry.Parse("M 7,0 L 14,7 L 7,14 L 0,7 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x3F)),
            Stroke = new SolidColorBrush(Color.FromRgb(0xB0, 0x8A, 0x00)),
            StrokeThickness = 0.8,
            Width = 14,
            Height = 14,
        };
        var mark = new TextBlock
        {
            Text = "!",
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = Brushes.Black,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0),
        };
        _errorSmartTagButton = new Button
        {
            Width = ErrorSmartTagPlanner.ButtonSize,
            Height = ErrorSmartTagPlanner.ButtonSize,
            Padding = new Thickness(0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0xC6, 0xC6)),
            BorderThickness = new Thickness(1),
            Focusable = false,
            Visibility = Visibility.Collapsed,
            ToolTip = ErrorSmartTagPlanner.ButtonToolTip,
            Content = new Grid { Children = { diamond, mark } },
        };
        AutomationProperties.SetAutomationId(_errorSmartTagButton, ErrorSmartTagPlanner.ButtonAutomationId);
        AutomationProperties.SetName(_errorSmartTagButton, ErrorSmartTagPlanner.ButtonToolTip);
        _errorSmartTagButton.Click += (_, _) => OpenErrorSmartTagMenu();
        CommentOverlay.Children.Add(_errorSmartTagButton);
    }

    internal void OpenErrorSmartTagMenu()
    {
        if (_errorSmartTagButton is null || _errorSmartTagCode is not { } code)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = _errorSmartTagButton,
            Placement = PlacementMode.Bottom,
        };
        foreach (var item in ErrorSmartTagPlanner.BuildMenu(code))
        {
            if (item.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var menuItem = new MenuItem
            {
                Header = item.Header,
                IsEnabled = !item.IsTitle,
                FontWeight = item.IsTitle ? FontWeights.Bold : FontWeights.Normal,
            };
            var action = item.Action;
            menuItem.Click += (_, _) => ExecuteErrorSmartTagAction(action);
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    internal void ExecuteErrorSmartTagAction(ErrorSmartTagAction action)
    {
        switch (action)
        {
            case ErrorSmartTagAction.ConvertToNumber:
                SynchronizeWorkbookSessionSelection();
                ApplyErrorSmartTagEdit(_session.ConvertSelectionTextToNumbers(), "Convert to Number");
                break;
            case ErrorSmartTagAction.IgnoreError:
                SynchronizeWorkbookSessionSelection();
                ApplyErrorSmartTagEdit(_session.IgnoreSelectionErrors(), "Ignore Error");
                break;
            case ErrorSmartTagAction.ShowCalculationSteps:
                EvaluateFormulaBtn_Click(this, new RoutedEventArgs());
                break;
            case ErrorSmartTagAction.HelpOnThisError:
                HelpOnlineBtn_Click(this, new RoutedEventArgs());
                break;
            case ErrorSmartTagAction.EditInFormulaBar:
                FocusFormulaBarAtEnd();
                break;
            case ErrorSmartTagAction.ErrorCheckingOptions:
                ShowOptionsDialog(OptionsDialogInitialSection.FormulaErrorChecking);
                break;
        }
    }

    private void ApplyErrorSmartTagEdit(WorkbookCellEditResult result, string title)
    {
        var outcome = ToCommandOutcome(result);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, title);
            return;
        }

        if (!outcome.IsNoOp)
            ApplySuccessfulWorkbookSessionCommand();

        RefreshErrorSmartTag();
    }
}
