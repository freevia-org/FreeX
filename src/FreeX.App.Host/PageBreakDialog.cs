using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public enum PageBreakDialogAction
{
    Clear,
    AddRow,
    AddColumn
}

public sealed record PageBreakDialogResult(PageBreakDialogAction Action, uint? RowBreak, uint? ColumnBreak);

public sealed class PageBreakDialog : Window
{
    private readonly RadioButton _insertRowButton = new() { Content = "Insert _row page break", IsChecked = true };
    private readonly RadioButton _insertColumnButton = new() { Content = "Insert _column page break" };
    private readonly RadioButton _resetAllButton = new() { Content = "_Reset all page breaks" };
    private readonly TextBox _rowBreakBox = new();
    private readonly TextBox _columnBreakBox = new();

    public PageBreakDialogResult Result { get; private set; } = CreateClearResult();

    public PageBreakDialog(string defaultValue)
    {
        Title = "Page Breaks";
        Width = 360;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SeedDefault(defaultValue);
        _insertRowButton.Checked += (_, _) => UpdateBreakInputAvailability();
        _insertColumnButton.Checked += (_, _) => UpdateBreakInputAvailability();
        _resetAllButton.Checked += (_, _) => UpdateBreakInputAvailability();
        Content = CreateContent();
        UpdateBreakInputAvailability();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PageBreakDialogResult CreateClearResult() =>
        new(PageBreakDialogAction.Clear, null, null);

    public static bool TryCreateResult(string input, out PageBreakDialogResult result)
    {
        result = CreateClearResult();
        var trimmed = input.Trim();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
            return true;

        if (PageLayoutInputParser.TryParseBreakInput(trimmed, "row", out var rowBreak) &&
            PageLayoutInputParser.IsValidRowBreak(rowBreak))
        {
            result = new PageBreakDialogResult(PageBreakDialogAction.AddRow, rowBreak, null);
            return true;
        }

        if ((PageLayoutInputParser.TryParseColumnBreakInput(trimmed, "col", out var columnBreak) ||
             PageLayoutInputParser.TryParseColumnBreakInput(trimmed, "column", out columnBreak)) &&
            PageLayoutInputParser.IsValidColumnBreak(columnBreak))
        {
            result = new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, columnBreak);
            return true;
        }

        return false;
    }

    private void Accept()
    {
        PageBreakDialogResult result;
        if (_resetAllButton.IsChecked == true)
            result = CreateClearResult();
        else if (_insertColumnButton.IsChecked == true)
        {
            if (!PageLayoutInputParser.TryParseColumnBreakValue(_columnBreakBox.Text, out var columnBreak))
            {
                DialogMessageHelper.ShowWarning(this, "Enter a column number or letter within the worksheet for the page break.", Title);
                FocusInvalidBreakInput(_columnBreakBox);
                return;
            }

            result = new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, columnBreak);
        }
        else
        {
            if (!uint.TryParse(_rowBreakBox.Text.Trim(), out var rowBreak) ||
                !PageLayoutInputParser.IsValidRowBreak(rowBreak))
            {
                DialogMessageHelper.ShowWarning(this, "Enter a row number within the worksheet for the page break.", Title);
                FocusInvalidBreakInput(_rowBreakBox);
                return;
            }

            result = new PageBreakDialogResult(PageBreakDialogAction.AddRow, rowBreak, null);
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_resetAllButton.IsChecked == true)
        {
            _resetAllButton.Focus();
            Keyboard.Focus(_resetAllButton);
        }
        else if (_insertColumnButton.IsChecked == true)
        {
            DialogFocus.FocusAndSelect(_columnBreakBox);
        }
        else
        {
            DialogFocus.FocusAndSelect(_rowBreakBox);
        }
    }

    private static void FocusInvalidBreakInput(TextBox textBox)
    {
        DialogFocus.FocusAndSelect(textBox);
    }

    private void UpdateBreakInputAvailability()
    {
        _rowBreakBox.IsEnabled = _insertRowButton.IsChecked == true;
        _columnBreakBox.IsEnabled = _insertColumnButton.IsChecked == true;
    }

    private void SeedDefault(string defaultValue)
    {
        if (!TryCreateResult(defaultValue, out var result))
            result = new PageBreakDialogResult(PageBreakDialogAction.AddRow, 2, null);

        _insertRowButton.IsChecked = result.Action == PageBreakDialogAction.AddRow;
        _insertColumnButton.IsChecked = result.Action == PageBreakDialogAction.AddColumn;
        _resetAllButton.IsChecked = result.Action == PageBreakDialogAction.Clear;
        _rowBreakBox.Text = (result.RowBreak ?? 2).ToString(CultureInfo.InvariantCulture);
        _columnBreakBox.Text = (result.ColumnBreak ?? 2).ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_rowBreakBox, "Row page break");
        AutomationProperties.SetName(_columnBreakBox, "Column page break");
    }

    private UIElement CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_insertRowButton);
        stack.Children.Add(CreateNumberRow("_Row:", _rowBreakBox));
        stack.Children.Add(_insertColumnButton);
        stack.Children.Add(CreateNumberRow("_Column:", _columnBreakBox));
        _resetAllButton.Margin = new Thickness(0, 4, 0, 12);
        stack.Children.Add(_resetAllButton);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        return stack;
    }

    private static StackPanel CreateNumberRow(string label, TextBox box)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 2, 0, 8) };
        row.Children.Add(new Label { Content = label, Target = box, Width = 72, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center });
        box.Width = 96;
        row.Children.Add(box);
        return row;
    }
}
