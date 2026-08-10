using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public sealed class WorkbookStatisticsDialog : Window
{
    private readonly IPlatformClipboard _platformClipboard;

    public WorkbookStatisticsDialog(
        WorkbookStatistics statistics,
        IPlatformClipboard? platformClipboard = null)
    {
        _platformClipboard = platformClipboard ?? new WpfPlatformClipboard(Dispatcher);
        Title = UiText.Get("WorkbookStatistics_WorkbookStatistics");
        Width = WorkbookStatisticsDialogPlanner.Width;
        Height = WorkbookStatisticsDialogPlanner.Height;
        MinWidth = WorkbookStatisticsDialogPlanner.MinWidth;
        MinHeight = WorkbookStatisticsDialogPlanner.MinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Content = CreateTextContent(CreateMessage(statistics));
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(WorkbookStatistics statistics) =>
        WorkbookStatisticsFormatter.Format(statistics);

    private Grid CreateTextContent(string message)
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var statisticsBlock = new TextBox
        {
            Text = message,
            AcceptsReturn = true,
            Background = SystemColors.WindowBrush,
            BorderBrush = SystemColors.ControlDarkBrush,
            IsReadOnly = true,
            Padding = new Thickness(8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 260
        };
        AutomationProperties.SetName(statisticsBlock, UiText.Get("WorkbookStatistics_WorkbookStatistics"));
        AutomationProperties.SetAutomationId(
            statisticsBlock,
            FreeXAutomationIdCatalog.WorkbookStatisticsSummary);
        AutomationProperties.SetHelpText(statisticsBlock, UiText.Get("WorkbookStatistics_SummarizesSheetCellFormulaCommentAndObjectCountsForTheWorkbook"));
        root.Children.Add(statisticsBlock);

        var buttonRow = CreateButtonRow(root, message);
        Grid.SetRow(buttonRow, 1);
        root.Children.Add(buttonRow);
        return root;
    }

    private StackPanel CreateButtonRow(DependencyObject root, string message)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        const string copyContent = "_Copy to Clipboard";
        const string copyHelpText = "Copy the workbook statistics report to the Clipboard.";
        var copy = new Button
        {
            Content = copyContent,
            MinWidth = 132,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetName(copy, UiText.CreateAutomationName(copyContent));
        AutomationProperties.SetAutomationId(
            copy,
            FreeXAutomationIdCatalog.WorkbookStatisticsCopyButton);
        AutomationProperties.SetHelpText(copy, copyHelpText);
        copy.Click += (_, _) => CopyMessageToClipboard(message);
        row.Children.Add(copy);

        var ok = new Button
        {
            Content = UiText.Ok,
            MinWidth = 76,
            IsDefault = true,
            IsCancel = true
        };
        AutomationProperties.SetName(ok, UiText.CreateAutomationName(UiText.Ok));
        ok.Click += (_, _) => Window.GetWindow(root)!.DialogResult = true;
        row.Children.Add(ok);

        return row;
    }

    private void CopyMessageToClipboard(string message)
    {
        try
        {
            _ = _platformClipboard.WriteAsync(new PlatformClipboardContent(Text: message))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex) when (IsClipboardUnavailableException(ex))
        {
        }
    }

    private static bool IsClipboardUnavailableException(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException;

    private void FocusInitialKeyboardTarget()
    {
        StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }
}
