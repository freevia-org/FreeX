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
    string? Submit = null,
    string? Dialog = null,
    string? Cancel = null,
    string? FirstPage = null,
    string? LastPage = null);

public sealed record AvaloniaPrintDialogCollation(bool IsSelectable, bool FixedValue)
{
    public static AvaloniaPrintDialogCollation Selectable { get; } = new(true, true);

    public static AvaloniaPrintDialogCollation Fixed(bool value) => new(false, value);

    public bool Resolve(bool? selectedValue) => IsSelectable ? selectedValue != false : FixedValue;
}

public sealed record AvaloniaPrintDialogText(
    string Title,
    string PrinterLabel,
    string CopiesLabel,
    string PagesLabel,
    string FirstPageLabel,
    string LastPageLabel,
    string OrientationLabel,
    string LayoutLabel,
    IReadOnlyList<string> PageRangeChoices,
    IReadOnlyList<string> OrientationChoices,
    string CollateCopiesLabel,
    string SubmitLabel,
    string CancelLabel,
    PrintDialogText Status)
{
    public static AvaloniaPrintDialogText DefaultEnglish { get; } = new(
        "Print",
        "Printer:",
        "Copies:",
        "Pages:",
        "First:",
        "Last:",
        "Orientation:",
        "Layout:",
        ["All pages", "Single page", "Page range"],
        ["Document", "Portrait", "Landscape"],
        "Collate copies",
        "Print",
        "Cancel",
        PrintDialogText.DefaultEnglish);
}

public sealed record AvaloniaPrintDialogOptions
{
    private static readonly PrintPageRangeKind[] DefaultPageRangeKinds =
        [PrintPageRangeKind.All, PrintPageRangeKind.Single, PrintPageRangeKind.Range];

    public double Width { get; init; } = 480;

    public double ChoiceMinWidth { get; init; } = 220;

    public string? LayoutSummary { get; init; }

    public AvaloniaPrintDialogAutomationIds AutomationIds { get; init; } = new();

    public AvaloniaPrintDialogCollation Collation { get; init; } = AvaloniaPrintDialogCollation.Fixed(true);

    /// <summary>
    /// Maps each localized page-range choice to the renderer-neutral selection kind. This lets
    /// products expose only the choices their print workflow supports without reimplementing
    /// parsing, validation, or range visibility.
    /// </summary>
    public IReadOnlyList<PrintPageRangeKind> PageRangeKinds { get; init; } = DefaultPageRangeKinds;

    /// <summary>Creates optional product-owned content, such as FreeX workbook scope selection.</summary>
    public Func<Control?>? CreateAdditionalContent { get; init; }

    /// <summary>
    /// Keeps submission available when no native printer exists and the product has a fallback
    /// destination, such as FreeX's print-ready PDF save path.
    /// </summary>
    public bool AllowSubmissionWithoutPrinter { get; init; }

    public bool ShowOrientation { get; init; } = true;

    public bool ApplyCompactActionButtonChrome { get; init; } = true;

    public AvaloniaPrintDialogText Text { get; init; } = AvaloniaPrintDialogText.DefaultEnglish;
}

/// <summary>
/// Builds and runs the shared Avalonia print-selection surface. Product-specific print planning
/// remains outside this renderer workflow; callers supply only presentation options and a native
/// window factory so each app can retain its own dialog base class.
/// </summary>
public static class AvaloniaPrintDialogWorkflow
{
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

        var pageRangeChoices = ValidatePageRangeChoices(options);

        var dialog = createWindow();
        ArgumentNullException.ThrowIfNull(dialog);

        var session = PrintDialogSession.Start(discovery, requested);
        var controls = Configure(dialog, session, options, pageRangeChoices);
        PrintSelection? result = null;

        controls.PageRange.SelectionChanged += (_, _) => UpdateRangeVisibility(controls);
        controls.Submit.Click += (_, _) =>
        {
            var submission = session.Submit(
                controls.Printer.SelectedItem as string,
                controls.Copies.Text,
                controls.SelectedPageRangeKind,
                controls.FirstPage.Text,
                controls.LastPage.Text,
                controls.Orientation.SelectedIndex,
                options.Collation.Resolve(controls.Collation?.IsChecked));

            if (!submission.Succeeded)
            {
                controls.Status.Text = options.Text.Status.ValidationMessage(submission.ValidationIssue);
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
        AvaloniaPrintDialogOptions options,
        PrintPageRangeChoiceMap pageRangeChoices)
    {
        var state = session.State;
        var text = options.Text;
        dialog.Title = text.Title;
        dialog.Width = options.Width;
        dialog.SizeToContent = SizeToContent.Height;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.CanResize = false;
        dialog.ShowInTaskbar = false;

        var printer = Choice(state.PrinterNames, state.SelectedPrinterIndex, options.ChoiceMinWidth);
        printer.IsEnabled = state.PrinterNames.Count > 0;
        var copies = Text(state.CopiesText);
        var selectedPageRangeIndex = pageRangeChoices.ChoiceIndexFor(
            (PrintPageRangeKind)state.PageRangeIndex);
        var pageRange = Choice(text.PageRangeChoices, selectedPageRangeIndex, options.ChoiceMinWidth);
        var firstPage = Text(state.FirstPageText);
        var lastPage = Text(state.LastPageText);
        var orientation = Choice(text.OrientationChoices, state.OrientationIndex, options.ChoiceMinWidth);
        var collation = options.Collation.IsSelectable
            ? new CheckBox { Content = text.CollateCopiesLabel, IsChecked = state.Collate }
            : null;
        var status = new TextBlock
        {
            Text = state.StatusMessage(text.Status),
            TextWrapping = TextWrapping.Wrap,
        };
        var submit = new Button
        {
            Content = text.SubmitLabel,
            IsDefault = true,
            IsEnabled = state.CanSubmit || options.AllowSubmissionWithoutPrinter,
        };
        var cancel = new Button { Content = text.CancelLabel, IsCancel = true };

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

        ApplyAutomationId(dialog, options.AutomationIds.Dialog);
        ApplyAutomationId(printer, options.AutomationIds.Printer);
        ApplyAutomationId(copies, options.AutomationIds.Copies);
        ApplyAutomationId(pageRange, options.AutomationIds.PageRange);
        ApplyAutomationId(firstPage, options.AutomationIds.FirstPage);
        ApplyAutomationId(lastPage, options.AutomationIds.LastPage);
        ApplyAutomationId(orientation, options.AutomationIds.Orientation);
        ApplyAutomationId(collation, options.AutomationIds.Collation);
        ApplyAutomationId(submit, options.AutomationIds.Submit);
        ApplyAutomationId(cancel, options.AutomationIds.Cancel);
        ApplyAutomationName(printer, text.PrinterLabel);
        ApplyAutomationName(copies, text.CopiesLabel);
        ApplyAutomationName(pageRange, text.PagesLabel);
        ApplyAutomationName(firstPage, text.FirstPageLabel);
        ApplyAutomationName(lastPage, text.LastPageLabel);
        ApplyAutomationName(orientation, text.OrientationLabel);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        if (!string.IsNullOrWhiteSpace(options.LayoutSummary))
        {
            AddRow(content, text.LayoutLabel, new TextBlock
            {
                Text = options.LayoutSummary,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        AddRow(content, text.PrinterLabel, printer);
        var additionalContent = options.CreateAdditionalContent?.Invoke();
        if (additionalContent is not null)
            content.Children.Add(additionalContent);
        AddRow(content, text.CopiesLabel, copies);
        AddRow(content, text.PagesLabel, pageRange);
        var firstPageLabel = new TextBlock
        {
            Text = text.FirstPageLabel,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var lastPageLabel = new TextBlock
        {
            Text = text.LastPageLabel,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var rangePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                firstPageLabel, firstPage,
                lastPageLabel, lastPage,
            },
        };
        content.Children.Add(rangePanel);
        if (options.ShowOrientation)
            AddRow(content, text.OrientationLabel, orientation);
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
            cancel,
            pageRangeChoices,
            rangePanel,
            lastPageLabel);
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
        var visibility = PrintDialogSession.RangeVisibility(controls.SelectedPageRangeKind);
        controls.RangePanel.IsVisible = visibility.ShowFirstPage;
        controls.FirstPage.IsVisible = visibility.ShowFirstPage;
        controls.LastPageLabel.IsVisible = visibility.ShowLastPage;
        controls.LastPage.IsVisible = visibility.ShowLastPage;
    }

    private static PrintPageRangeChoiceMap ValidatePageRangeChoices(AvaloniaPrintDialogOptions options)
    {
        var map = new PrintPageRangeChoiceMap(options.PageRangeKinds);
        if (map.Kinds.Count != options.Text.PageRangeChoices.Count)
        {
            throw new ArgumentException(
                "Each page-range label must have exactly one page-range kind.",
                nameof(options));
        }

        return map;
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

    private static void ApplyAutomationName(Control control, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            AutomationProperties.SetName(control, name);
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
        Button Cancel,
        PrintPageRangeChoiceMap PageRangeChoices,
        StackPanel RangePanel,
        TextBlock LastPageLabel)
    {
        public int SelectedPageRangeKind =>
            PageRangeChoices.KindIndexAt(PageRange.SelectedIndex);
    }
}
