using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

/// <summary>
/// Read-only Review > Check Performance report. The scan intentionally never changes workbook
/// formatting; the user can review each bounded range before using the existing Clear Formats command.
/// </summary>
public sealed class WorkbookPerformanceDialog : Window
{
    public WorkbookPerformanceDialog(WorkbookPerformanceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        Title = "Check Performance";
        Width = 560;
        Height = 460;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;

        Content = CreateContent(WorkbookPerformanceFormatter.Format(report));
        Loaded += (_, _) => StatusDialogKeyboardFocus.FocusDefaultButton(this);
    }

    public static string CreateMessage(WorkbookPerformanceReport report) =>
        WorkbookPerformanceFormatter.Format(report);

    private Grid CreateContent(string message)
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var reportBlock = new TextBox
        {
            Text = message,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8),
            MinHeight = 220,
        };
        AutomationProperties.SetName(reportBlock, "Performance check results");
        AutomationProperties.SetAutomationId(reportBlock, "WorkbookPerformanceReport");
        AutomationProperties.SetHelpText(reportBlock, "Reports formatting-only cells that extend worksheet used ranges. This report does not change the workbook.");
        root.Children.Add(reportBlock);

        var close = new Button
        {
            Content = UiText.Ok,
            MinWidth = 76,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        close.Click += (_, _) => DialogResult = true;
        Grid.SetRow(close, 1);
        root.Children.Add(close);
        return root;
    }
}
