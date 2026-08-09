using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Printing;

internal sealed class CupsPrintDialog : FreeWDialogWindow
{
    private static readonly PrintDialogText DialogText = PrintDialogText.DefaultEnglish;

    private readonly PrintDialogSession _session;
    private readonly ComboBox _printer;
    private readonly TextBox _copies;
    private readonly ComboBox _range;
    private readonly TextBox _firstPage;
    private readonly TextBox _lastPage;
    private readonly ComboBox _orientation;
    private readonly TextBlock _status;
    private readonly Button _ok;

    private CupsPrintDialog(PrintDialogSession session)
    {
        _session = session;
        var state = session.State;
        Title = "Print";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _printer = Choice(state.PrinterNames, state.SelectedPrinterIndex);
        _copies = Text(state.CopiesText);
        _range = Choice(["All pages", "Single page", "Page range"], state.PageRangeIndex);
        _firstPage = Text(state.FirstPageText);
        _lastPage = Text(state.LastPageText);
        _orientation = Choice(["Document", "Portrait", "Landscape"], state.OrientationIndex);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _ok = new Button { Content = "Print", IsDefault = true, IsEnabled = state.CanSubmit };
        AvaloniaCompactDialogChrome.ApplyButton(_ok, AvaloniaCompactDialogChrome.WindowsStyle, minWidth: 72, isDefault: true);

        _range.SelectionChanged += (_, _) => UpdateRangeVisibility();
        _ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, AvaloniaCompactDialogChrome.WindowsStyle, minWidth: 72);
        cancel.Click += (_, _) => Close();
        _status.Text = state.StatusMessage(DialogText);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
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
        content.Children.Add(_status);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([_ok, cancel]));
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(discovery);

        var dialog = new CupsPrintDialog(PrintDialogSession.Start(discovery, requested));
        using var cancellationRegistration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(dialog.Close));
        await dialog.ShowDialog(owner);
        cancellationToken.ThrowIfCancellationRequested();
        return dialog.Result;
    }

    private PrintSelection? Result { get; set; }

    private void Accept()
    {
        var submission = _session.Submit(
            _printer.SelectedItem as string,
            _copies.Text,
            _range.SelectedIndex,
            _firstPage.Text,
            _lastPage.Text,
            _orientation.SelectedIndex,
            collate: true);

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
        var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = selectedIndex, MinWidth = 220 };
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
