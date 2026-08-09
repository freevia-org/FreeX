using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.AppServices.Printing;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaPrintDialogAutomationIds(
    string? Printer = null,
    string? Copies = null,
    string? PageRange = null,
    string? Orientation = null,
    string? Collation = null,
    string? Submit = null);

public sealed record AvaloniaPrintDialogCollation(bool IsSelectable, bool FixedValue)
{
    public static AvaloniaPrintDialogCollation Selectable { get; } = new(true, true);

    public static AvaloniaPrintDialogCollation Fixed(bool value) => new(false, value);

    public bool Resolve(bool? selectedValue) => IsSelectable ? selectedValue != false : FixedValue;
}

public sealed record AvaloniaPrintDialogOptions
{
    public double Width { get; init; } = 480;

    public double ChoiceMinWidth { get; init; } = 220;

    public string? LayoutSummary { get; init; }

    public AvaloniaPrintDialogAutomationIds AutomationIds { get; init; } = new();

    public AvaloniaPrintDialogCollation Collation { get; init; } = AvaloniaPrintDialogCollation.Fixed(true);

    public bool ApplyCompactActionButtonChrome { get; init; } = true;
}

/// <summary>
/// Builds and runs the shared Avalonia print-selection surface. Product-specific print planning
/// remains outside this renderer workflow; callers supply only presentation options and a native
/// window factory so each app can retain its own dialog base class.
/// </summary>
public static class AvaloniaPrintDialogWorkflow
{
    private static readonly PrintDialogText DialogText = PrintDialogText.DefaultEnglish;

    public static async Task<PrintSelection?> ShowAsync(
        Window owner,
        PrinterDiscoveryResult discovery,
        Func<Window> createWindow,
        AvaloniaPrintDialogOptions options,
        PrintSelection? requested = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(createWindow);
        ArgumentNullException.ThrowIfNull(options);

        var dialog = createWindow();
        ArgumentNullException.ThrowIfNull(dialog);

        var session = PrintDialogSession.Start(discovery, requested);
        var controls = Configure(dialog, session, options);
        PrintSelection? result = null;

        controls.PageRange.SelectionChanged += (_, _) => UpdateRangeVisibility(controls);
        controls.Submit.Click += (_, _) =>
        {
            var submission = session.Submit(
                controls.Printer.SelectedItem as string,
                controls.Copies.Text,
                controls.PageRange.SelectedIndex,
                controls.FirstPage.Text,
                controls.LastPage.Text,
                controls.Orientation.SelectedIndex,
                options.Collation.Resolve(controls.Collation?.IsChecked));

            if (!submission.Succeeded)
            {
                controls.Status.Text = DialogText.ValidationMessage(submission.ValidationIssue);
                FocusInvalidField(controls, submission.ValidationIssue);
                return;
            }

            result = submission.Selection;
            dialog.Close();
        };
        controls.Cancel.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => controls.Submit.Focus();
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;

            dialog.Close();
            args.Handled = true;
        };

        UpdateRangeVisibility(controls);
        using var cancellationRegistration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        await dialog.ShowDialog(owner);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static PrintDialogControls Configure(
        Window dialog,
        PrintDialogSession session,
        AvaloniaPrintDialogOptions options)
    {
        var state = session.State;
        dialog.Title = "Print";
        dialog.Width = options.Width;
        dialog.SizeToContent = SizeToContent.Height;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.CanResize = false;
        dialog.ShowInTaskbar = false;

        var printer = Choice(state.PrinterNames, state.SelectedPrinterIndex, options.ChoiceMinWidth);
        var copies = Text(state.CopiesText);
        var pageRange = Choice(["All pages", "Single page", "Page range"], state.PageRangeIndex, options.ChoiceMinWidth);
        var firstPage = Text(state.FirstPageText);
        var lastPage = Text(state.LastPageText);
        var orientation = Choice(["Document", "Portrait", "Landscape"], state.OrientationIndex, options.ChoiceMinWidth);
        var collation = options.Collation.IsSelectable
            ? new CheckBox { Content = "Collate copies", IsChecked = state.Collate }
            : null;
        var status = new TextBlock
        {
            Text = state.StatusMessage(DialogText),
            TextWrapping = TextWrapping.Wrap,
        };
        var submit = new Button { Content = "Print", IsDefault = true, IsEnabled = state.CanSubmit };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        if (options.ApplyCompactActionButtonChrome)
        {
            AvaloniaCompactDialogChrome.ApplyButton(
                submit,
                AvaloniaCompactDialogChrome.WindowsStyle,
                minWidth: 72,
                isDefault: true);
            AvaloniaCompactDialogChrome.ApplyButton(
                cancel,
                AvaloniaCompactDialogChrome.WindowsStyle,
                minWidth: 72);
        }

        ApplyAutomationId(printer, options.AutomationIds.Printer);
        ApplyAutomationId(copies, options.AutomationIds.Copies);
        ApplyAutomationId(pageRange, options.AutomationIds.PageRange);
        ApplyAutomationId(orientation, options.AutomationIds.Orientation);
        ApplyAutomationId(collation, options.AutomationIds.Collation);
        ApplyAutomationId(submit, options.AutomationIds.Submit);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        if (!string.IsNullOrWhiteSpace(options.LayoutSummary))
        {
            AddRow(content, "Layout:", new TextBlock
            {
                Text = options.LayoutSummary,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        AddRow(content, "Printer:", printer);
        AddRow(content, "Copies:", copies);
        AddRow(content, "Pages:", pageRange);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "First:", VerticalAlignment = VerticalAlignment.Center }, firstPage,
                new TextBlock { Text = "Last:", VerticalAlignment = VerticalAlignment.Center }, lastPage,
            },
        });
        AddRow(content, "Orientation:", orientation);
        if (collation is not null)
            content.Children.Add(collation);
        content.Children.Add(status);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([submit, cancel]));
        dialog.Content = content;

        return new PrintDialogControls(
            printer,
            copies,
            pageRange,
            firstPage,
            lastPage,
            orientation,
            collation,
            status,
            submit,
            cancel);
    }

    private static void FocusInvalidField(PrintDialogControls controls, PrintDialogValidationIssue issue)
    {
        switch (issue)
        {
            case PrintDialogValidationIssue.CopiesOutOfRange:
                controls.Copies.Focus();
                break;
            case PrintDialogValidationIssue.FirstPageInvalid:
                controls.FirstPage.Focus();
                break;
            case PrintDialogValidationIssue.LastPageBeforeFirstPage:
                controls.LastPage.Focus();
                break;
        }
    }

    private static void UpdateRangeVisibility(PrintDialogControls controls)
    {
        var visibility = PrintDialogSession.RangeVisibility(controls.PageRange.SelectedIndex);
        controls.FirstPage.IsVisible = visibility.ShowFirstPage;
        controls.LastPage.IsVisible = visibility.ShowLastPage;
    }

    private static ComboBox Choice(IEnumerable<string> items, int selectedIndex, double minWidth)
    {
        var combo = new ComboBox
        {
            ItemsSource = items.ToArray(),
            SelectedIndex = selectedIndex,
            MinWidth = minWidth,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, AvaloniaCompactDialogChrome.WindowsStyle);
        return combo;
    }

    private static TextBox Text(string value)
    {
        var box = new TextBox { Text = value, MinWidth = 90 };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, AvaloniaCompactDialogChrome.WindowsStyle);
        return box;
    }

    private static void AddRow(StackPanel panel, string label, Control control)
    {
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, Width = 95, VerticalAlignment = VerticalAlignment.Center }, control,
            },
        });
    }

    private static void ApplyAutomationId(Control? control, string? automationId)
    {
        if (control is not null && !string.IsNullOrWhiteSpace(automationId))
            AutomationProperties.SetAutomationId(control, automationId);
    }

    private sealed record PrintDialogControls(
        ComboBox Printer,
        TextBox Copies,
        ComboBox PageRange,
        TextBox FirstPage,
        TextBox LastPage,
        ComboBox Orientation,
        CheckBox? Collation,
        TextBlock Status,
        Button Submit,
        Button Cancel);
}
