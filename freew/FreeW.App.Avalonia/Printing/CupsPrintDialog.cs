using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Printing;

namespace FreeW.App.Avalonia.Printing;

internal sealed class CupsPrintDialog : FreeWDialogWindow
{
    private readonly ComboBox _printer;
    private readonly TextBox _copies;
    private readonly ComboBox _range;
    private readonly TextBox _firstPage;
    private readonly TextBox _lastPage;
    private readonly ComboBox _orientation;
    private readonly TextBlock _status;
    private readonly Button _ok;

    private CupsPrintDialog(PrintDialogPlan plan)
    {
        Title = "Print";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _printer = Choice(plan.Printers.Select(printer => printer.Name),
            Math.Max(0, plan.Printers.Select(printer => printer.Name)
                .ToList().FindIndex(name => string.Equals(name, plan.SelectedPrinter, StringComparison.OrdinalIgnoreCase))));
        _copies = Text(plan.Copies.ToString());
        _range = Choice(["All pages", "Single page", "Page range"], plan.PageRange.Kind switch
        {
            PrintPageRangeKind.Single => 1,
            PrintPageRangeKind.Range => 2,
            _ => 0,
        });
        _firstPage = Text((plan.PageRange.FirstPage ?? 1).ToString());
        _lastPage = Text((plan.PageRange.LastPage ?? 1).ToString());
        _orientation = Choice(["Document", "Portrait", "Landscape"], (int)plan.Orientation);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _ok = new Button { Content = "Print", IsDefault = true, IsEnabled = plan.CanSubmit };
        AvaloniaCompactDialogChrome.ApplyButton(_ok, AvaloniaCompactDialogChrome.WindowsStyle, minWidth: 72, isDefault: true);

        _range.SelectionChanged += (_, _) => UpdateRangeVisibility();
        _ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, AvaloniaCompactDialogChrome.WindowsStyle, minWidth: 72);
        cancel.Click += (_, _) => Close();
        _status.Text = plan.Message ?? (plan.CanSubmit ? "Choose the printer and print settings." : "Printing is unavailable on this host.");

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

        var plan = PrintSelectionPlanner.Build(discovery, requested);
        var dialog = new CupsPrintDialog(plan);
        using var cancellationRegistration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(dialog.Close));
        await dialog.ShowDialog(owner);
        cancellationToken.ThrowIfCancellationRequested();
        return dialog.Result;
    }

    private PrintSelection? Result { get; set; }

    private void Accept()
    {
        if (!int.TryParse(_copies.Text, out var copies) || copies is < 1 or > 999)
        {
            _status.Text = "Copies must be between 1 and 999.";
            _copies.Focus();
            return;
        }

        PrintPageRange pageRange;
        if (_range.SelectedIndex == 0)
            pageRange = PrintPageRange.All;
        else if (!int.TryParse(_firstPage.Text, out var first) || first < 1)
        {
            _status.Text = "Enter a positive first page number.";
            _firstPage.Focus();
            return;
        }
        else if (_range.SelectedIndex == 1)
            pageRange = PrintPageRange.Single(first);
        else if (!int.TryParse(_lastPage.Text, out var last) || last < first)
        {
            _status.Text = "The last page must be at least the first page.";
            _lastPage.Focus();
            return;
        }
        else
            pageRange = PrintPageRange.Between(first, last);

        Result = new PrintSelection(
            _printer.SelectedItem as string,
            copies,
            pageRange,
            (PrintOrientation)Math.Clamp(_orientation.SelectedIndex, 0, 2));
        Close();
    }

    private void UpdateRangeVisibility()
    {
        var visible = _range.SelectedIndex != 0;
        _firstPage.IsVisible = visible;
        _lastPage.IsVisible = _range.SelectedIndex == 2;
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
