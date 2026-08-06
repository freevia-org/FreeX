using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia.Printing;

/// <summary>
/// Portable printer/settings surface. It owns only the in-app selection model; queue discovery and
/// submission remain in the injected shared platform adapter.
/// </summary>
internal sealed class CupsPrintDialog : Window
{
    private static readonly PrintDialogText DialogText = PrintDialogText.DefaultEnglish;

    private readonly PrintDialogSession _session;
    private readonly ComboBox _printer;
    private readonly TextBox _copies;
    private readonly ComboBox _range;
    private readonly TextBox _firstPage;
    private readonly TextBox _lastPage;
    private readonly ComboBox _orientation;
    private readonly CheckBox _collate;
    private readonly TextBlock _status;
    private readonly Button _ok;
    private readonly string? _layoutSummary;

    private CupsPrintDialog(PrintDialogSession session, string? layoutSummary)
    {
        _session = session;
        _layoutSummary = layoutSummary;
        var state = session.State;
        Title = "Print";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _printer = Choice(
            state.PrinterNames,
            state.SelectedPrinterIndex);
        _copies = Text(state.CopiesText);
        _range = Choice(["All pages", "Single page", "Page range"], state.PageRangeIndex);
        _firstPage = Text(state.FirstPageText);
        _lastPage = Text(state.LastPageText);
        _orientation = Choice(["Document", "Portrait", "Landscape"], state.OrientationIndex);
        _collate = new CheckBox { Content = "Collate copies", IsChecked = state.Collate };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _ok = new Button { Content = "Print", IsDefault = true, IsEnabled = state.CanSubmit };

        AutomationProperties.SetAutomationId(_printer, "FreePPortablePrinterPicker");
        AutomationProperties.SetAutomationId(_copies, "FreePPortablePrintCopies");
        AutomationProperties.SetAutomationId(_range, "FreePPortablePrintPageRange");
        AutomationProperties.SetAutomationId(_orientation, "FreePPortablePrintOrientation");
        AutomationProperties.SetAutomationId(_collate, "FreePPortablePrintCollation");
        AutomationProperties.SetAutomationId(_ok, "FreePPortablePrintSubmit");

        _range.SelectionChanged += (_, _) => UpdateRangeVisibility();
        _ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();
        _status.Text = state.StatusMessage(DialogText);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        if (!string.IsNullOrWhiteSpace(_layoutSummary))
            AddRow(content, "Layout:", new TextBlock
            {
                Text = _layoutSummary,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        AddRow(content, "Printer:", _printer);
        AddRow(content, "Copies:", _copies);
        AddRow(content, "Pages:", _range);
        var pageNumbers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "First:", VerticalAlignment = VerticalAlignment.Center }, _firstPage,
                new TextBlock { Text = "Last:", VerticalAlignment = VerticalAlignment.Center }, _lastPage,
            },
        };
        content.Children.Add(pageNumbers);
        AddRow(content, "Orientation:", _orientation);
        content.Children.Add(_collate);
        content.Children.Add(_status);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _ok, cancel },
        });
        Content = content;
        UpdateRangeVisibility();
        Opened += (_, _) => _ok.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;

            Close();
            args.Handled = true;
        };
    }

    public static async Task<PrintSelection?> ShowAsync(
        Window owner,
        PrinterDiscoveryResult discovery,
        PrintSelection? requested = null,
        string? layoutSummary = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(discovery);

        var dialog = new CupsPrintDialog(PrintDialogSession.Start(discovery, requested), layoutSummary);
        using var cancellationRegistration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        await dialog.ShowDialog(owner);
        cancellationToken.ThrowIfCancellationRequested();
        return dialog.Result;
    }

    internal PrintSelection? Result { get; private set; }

    private void Accept()
    {
        var submission = _session.Submit(
            _printer.SelectedItem as string,
            _copies.Text,
            _range.SelectedIndex,
            _firstPage.Text,
            _lastPage.Text,
            _orientation.SelectedIndex,
            _collate.IsChecked != false);

        if (!submission.Succeeded)
        {
            _status.Text = DialogText.ValidationMessage(submission.ValidationIssue);
            FocusInvalidField(submission.ValidationIssue);
            return;
        }

        Result = submission.Selection;
        Close();
    }

    private void FocusInvalidField(PrintDialogValidationIssue issue)
    {
        switch (issue)
        {
            case PrintDialogValidationIssue.CopiesOutOfRange:
                _copies.Focus();
                break;
            case PrintDialogValidationIssue.FirstPageInvalid:
                _firstPage.Focus();
                break;
            case PrintDialogValidationIssue.LastPageBeforeFirstPage:
                _lastPage.Focus();
                break;
        }
    }

    private void UpdateRangeVisibility()
    {
        var visibility = PrintDialogSession.RangeVisibility(_range.SelectedIndex);
        _firstPage.IsVisible = visibility.ShowFirstPage;
        _lastPage.IsVisible = visibility.ShowLastPage;
    }

    private static ComboBox Choice(IEnumerable<string> items, int selectedIndex)
    {
        var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = selectedIndex, MinWidth = 240 };
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
}
