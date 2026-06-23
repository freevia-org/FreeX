using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host;

public sealed class PageBreakDialog : Window
{
    private readonly RadioButton _insertRowButton = new() { Content = UiText.Get("PageBreak_InsertRowPageBreak"), IsChecked = true };
    private readonly RadioButton _insertColumnButton = new() { Content = UiText.Get("PageBreak_InsertColumnPageBreak") };
    private readonly RadioButton _resetAllButton = new() { Content = UiText.Get("PageBreak_ResetAllPageBreaks") };
    private readonly TextBox _rowBreakBox = new();
    private readonly TextBox _columnBreakBox = new();

    public PageBreakDialogResult Result { get; private set; } = PageBreakDialogPlanner.CreateClearResult();

    public PageBreakDialog(string defaultValue)
    {
        Title = UiText.Get("PageBreak_PageBreaks");
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

    private void Accept()
    {
        var action = _resetAllButton.IsChecked == true
            ? PageBreakDialogAction.Clear
            : _insertColumnButton.IsChecked == true
                ? PageBreakDialogAction.AddColumn
                : PageBreakDialogAction.AddRow;

        if (!PageBreakDialogPlanner.TryCreateResult(action, _rowBreakBox.Text, _columnBreakBox.Text, out var result))
        {
            if (action == PageBreakDialogAction.AddColumn)
            {
                DialogMessageHelper.ShowWarning(this, UiText.Get("PageBreak_EnterAColumnNumberOrLetterWithinTheWorksheetForThePageBreak"), Title);
                FocusInvalidBreakInput(_columnBreakBox);
                return;
            }

            DialogMessageHelper.ShowWarning(this, UiText.Get("PageBreak_EnterARowNumberWithinTheWorksheetForThePageBreak"), Title);
            FocusInvalidBreakInput(_rowBreakBox);
            return;
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
        if (!PageBreakDialogPlanner.TryCreateResult(defaultValue, out var result))
            result = PageBreakDialogPlanner.CreateRowResult(2);

        _insertRowButton.IsChecked = result.Action == PageBreakDialogAction.AddRow;
        _insertColumnButton.IsChecked = result.Action == PageBreakDialogAction.AddColumn;
        _resetAllButton.IsChecked = result.Action == PageBreakDialogAction.Clear;
        _rowBreakBox.Text = (result.RowBreak ?? 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _columnBreakBox.Text = (result.ColumnBreak ?? 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        AutomationProperties.SetName(_rowBreakBox, UiText.Get("PageBreak_RowPageBreak"));
        AutomationProperties.SetAutomationId(_rowBreakBox, "PageBreakRowBreakBox");
        AutomationProperties.SetHelpText(_rowBreakBox, UiText.Get("PageBreak_EnterTheRowNumberWhereTheHorizontalPageBreakShouldBeInserted"));
        AutomationProperties.SetName(_columnBreakBox, UiText.Get("PageBreak_ColumnPageBreak"));
        AutomationProperties.SetAutomationId(_columnBreakBox, "PageBreakColumnBreakBox");
        AutomationProperties.SetHelpText(_columnBreakBox, UiText.Get("PageBreak_EnterTheColumnNumberOrLetterWhereTheVerticalPageBreakShouldBeInserted"));
    }

    private UIElement CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_insertRowButton);
        stack.Children.Add(CreateNumberRow(UiText.Get("PageBreak_RowLabel"), _rowBreakBox));
        stack.Children.Add(_insertColumnButton);
        stack.Children.Add(CreateNumberRow(UiText.Get("PageBreak_ColumnLabel"), _columnBreakBox));
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
