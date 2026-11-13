using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FreeX.App.Localization;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

// Excel's background error checking: the green triangle in the top-left corner of a cell that
// breaks an enabled error-checking rule (number stored as text, inconsistent formula, ...), and the
// warning smart tag beside the active cell that offers the fix, Ignore Error and the options.
public partial class MainWindow
{
    // Excel's "Automatic" error indicator colour; WPF's GridView.ErrorIndicatorBrush.
    private static readonly IBrush ErrorIndicatorFill = new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00));

    private readonly BackgroundErrorIndicatorCache _errorIndicatorCache = new();

    internal Button? ErrorSmartTagButtonForTest { get; private set; }

    internal void RefreshShellForTest() => RefreshShell(UiText.Get("MainLoc_Ready"));

    private bool HasErrorIndicatorAt(CellAddress address) => GetErrorIndicatorCode(address) is not null;

    internal string? GetErrorIndicatorCode(CellAddress address) =>
        _errorIndicatorCache.GetErrorCode(_session.Workbook, address);

    private void AddErrorSmartTagOverlay(Canvas overlay, ViewportModel viewport, bool showHeadings, double zoomFactor)
    {
        ErrorSmartTagButtonForTest = null;
        if (GetErrorIndicatorCode(_session.ActiveCell) is not { } code ||
            !TryGetDisplayedCellBounds(viewport, _session.ActiveCell, showHeadings, zoomFactor, out var left, out var top, out var width, out _))
        {
            return;
        }

        var size = ErrorSmartTagPlanner.ButtonSize;
        var buttonLeft = left - size - 2;
        if (buttonLeft < 0)
            buttonLeft = left + width + 2;

        var button = CreateErrorSmartTagButton(code);
        Canvas.SetLeft(button, buttonLeft);
        Canvas.SetTop(button, Math.Max(0, top));
        overlay.Children.Add(button);
        ErrorSmartTagButtonForTest = button;
    }

    private Button CreateErrorSmartTagButton(string code)
    {
        // Excel's smart tag: a yellow warning diamond with an exclamation mark.
        var diamond = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 7,0 L 14,7 L 7,14 L 0,7 Z"),
            Fill = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x3F)),
            Stroke = new ImmutableSolidColorBrush(Color.FromRgb(0xB0, 0x8A, 0x00)),
            StrokeThickness = 0.8,
            Width = 14,
            Height = 14,
        };
        var mark = new TextBlock
        {
            Text = "!",
            FontWeight = FontWeight.Bold,
            FontSize = 10,
            Foreground = Brushes.Black,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };

        var menu = new MenuFlyout();
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
                FontWeight = item.IsTitle ? FontWeight.Bold : FontWeight.Normal,
            };
            var action = item.Action;
            menuItem.Click += (_, _) => ExecuteErrorSmartTagAction(action);
            menu.Items.Add(menuItem);
        }

        var button = new Button
        {
            Width = ErrorSmartTagPlanner.ButtonSize,
            Height = ErrorSmartTagPlanner.ButtonSize,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Background = Brushes.White,
            BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xC6, 0xC6, 0xC6)),
            BorderThickness = new Thickness(1),
            Focusable = false,
            Content = new Panel { Children = { diamond, mark } },
            Flyout = menu,
        };
        ToolTip.SetTip(button, ErrorSmartTagPlanner.ButtonToolTip);
        AutomationProperties.SetAutomationId(button, ErrorSmartTagPlanner.ButtonAutomationId);
        AutomationProperties.SetName(button, ErrorSmartTagPlanner.ButtonToolTip);
        return button;
    }

    internal void ExecuteErrorSmartTagAction(ErrorSmartTagAction action)
    {
        switch (action)
        {
            case ErrorSmartTagAction.ConvertToNumber:
                ApplyErrorSmartTagEdit(_session.ConvertSelectionTextToNumbers());
                break;
            case ErrorSmartTagAction.IgnoreError:
                ApplyErrorSmartTagEdit(_session.IgnoreSelectionErrors());
                break;
            case ErrorSmartTagAction.ShowCalculationSteps:
                RunGuarded(ShowEvaluateFormulaDialogAsync);
                break;
            case ErrorSmartTagAction.HelpOnThisError:
                RunGuarded(() => OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, UiText.Get("MainWindow_Content_HelpOnline")));
                break;
            case ErrorSmartTagAction.EditInFormulaBar:
                BeginFormulaEdit(_session.ActiveCell);
                break;
            case ErrorSmartTagAction.ErrorCheckingOptions:
                RunGuarded(ShowOptionsDialogAsync);
                break;
        }
    }

    private void ApplyErrorSmartTagEdit(WorkbookCellEditResult result)
    {
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("MainWindowMessage_CommandCouldNotBeCompleted"));
            return;
        }

        RefreshShell(UiText.Get("MainLoc_Ready"));
    }
}
