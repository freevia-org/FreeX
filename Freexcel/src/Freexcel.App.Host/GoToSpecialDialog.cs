using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public sealed record GoToSpecialChoice(GoToSpecialKind Kind, string Label);

public sealed class GoToSpecialDialog : Window
{
    private readonly List<RadioButton> _buttons = [];
    private readonly CheckBox _numbersBox = new() { Content = "_Numbers", IsChecked = true, Margin = new Thickness(0, 0, 18, 4) };
    private readonly CheckBox _textBox = new() { Content = "_Text", IsChecked = true, Margin = new Thickness(0, 0, 18, 4) };
    private readonly CheckBox _logicalsBox = new() { Content = "_Logicals", IsChecked = true, Margin = new Thickness(0, 0, 18, 4) };
    private readonly CheckBox _errorsBox = new() { Content = "_Errors", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };

    public GoToSpecialKind SelectedKind { get; private set; } = GoToSpecialKind.Blanks;
    public GoToSpecialOptions SelectedOptions { get; private set; } = new();

    public GoToSpecialDialog()
    {
        Title = "Go To Special";
        Width = 430;
        Height = 438;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var content = new StackPanel();
        DockPanel.SetDock(content, Dock.Top);
        root.Children.Add(content);

        content.Children.Add(new TextBlock
        {
            Text = "Select",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var availableGroup = new GroupBox { Header = "Go to special", Margin = new Thickness(0, 0, 0, 10) };
        var optionGrid = CreateChoiceGrid();
        availableGroup.Content = optionGrid;
        content.Children.Add(availableGroup);

        var choiceRow = 0;
        foreach (var choice in GetChoices())
        {
            var button = new RadioButton
            {
                Content = choice.Label,
                Tag = choice.Kind,
                Margin = new Thickness(0, 0, 12, 6)
            };
            button.Checked += (_, _) => RefreshValueTypeOptions();
            _buttons.Add(button);
            AddChoice(optionGrid, button, choiceRow++);
        }

        content.Children.Add(CreateValueTypeGroup());

        if (_buttons.Count > 0)
            _buttons[0].IsChecked = true;
        RefreshValueTypeOptions();

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static IReadOnlyList<GoToSpecialChoice> GetChoices() =>
        [
            new(GoToSpecialKind.Blanks, "_Blanks"),
            new(GoToSpecialKind.Constants, "_Constants"),
            new(GoToSpecialKind.Formulas, "_Formulas"),
            new(GoToSpecialKind.Comments, "Co_mments"),
            new(GoToSpecialKind.CurrentRegion, "Current _region"),
            new(GoToSpecialKind.RowDifferences, "Row _differences"),
            new(GoToSpecialKind.ColumnDifferences, "Column dif_ferences"),
            new(GoToSpecialKind.LastCell, "_Last cell"),
            new(GoToSpecialKind.ConditionalFormats, "Conditional _formats"),
            new(GoToSpecialKind.Objects, "_Objects"),
            new(GoToSpecialKind.Precedents, "_Precedents"),
            new(GoToSpecialKind.Dependents, "_Dependents"),
            new(GoToSpecialKind.DataValidation, "_Data validation"),
            new(GoToSpecialKind.VisibleCellsOnly, "_Visible cells only")
        ];

    public static bool TryParseChoice(string text, out GoToSpecialKind kind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            kind = default;
            return false;
        }

        kind = GoToSpecialInputParser.Parse(text);
        return true;
    }

    private static Grid CreateChoiceGrid()
    {
        var grid = new Grid { Margin = new Thickness(8, 6, 8, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private GroupBox CreateValueTypeGroup()
    {
        var panel = new WrapPanel { Margin = new Thickness(8, 6, 8, 4) };
        panel.Children.Add(_numbersBox);
        panel.Children.Add(_textBox);
        panel.Children.Add(_logicalsBox);
        panel.Children.Add(_errorsBox);
        return new GroupBox
        {
            Header = "Values for constants and formulas",
            Margin = new Thickness(0, 0, 0, 10),
            Content = panel
        };
    }

    private static void AddChoice(Grid grid, RadioButton button, int index)
    {
        var row = index / 2;
        while (grid.RowDefinitions.Count <= row)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(button, row);
        Grid.SetColumn(button, index % 2);
        grid.Children.Add(button);
    }

    private void FocusInitialKeyboardTarget()
    {
        var firstButton = _buttons.FirstOrDefault();
        _buttons.FirstOrDefault()?.Focus();
        if (firstButton is not null)
            Keyboard.Focus(firstButton);
    }

    private void Accept()
    {
        var selected = _buttons.FirstOrDefault(button => button.IsChecked == true);
        SelectedKind = selected?.Tag is GoToSpecialKind kind ? kind : GoToSpecialKind.Blanks;
        SelectedOptions = new GoToSpecialOptions(GetSelectedValueTypes());
        DialogResult = true;
    }

    private GoToSpecialValueTypes GetSelectedValueTypes()
    {
        var valueTypes = GoToSpecialValueTypes.None;
        if (_numbersBox.IsChecked == true)
            valueTypes |= GoToSpecialValueTypes.Numbers;
        if (_textBox.IsChecked == true)
            valueTypes |= GoToSpecialValueTypes.Text;
        if (_logicalsBox.IsChecked == true)
            valueTypes |= GoToSpecialValueTypes.Logicals;
        if (_errorsBox.IsChecked == true)
            valueTypes |= GoToSpecialValueTypes.Errors;
        return valueTypes == GoToSpecialValueTypes.None ? GoToSpecialValueTypes.All : valueTypes;
    }

    private void RefreshValueTypeOptions()
    {
        var selected = _buttons.FirstOrDefault(button => button.IsChecked == true);
        var enabled = selected?.Tag is GoToSpecialKind.Constants or GoToSpecialKind.Formulas;
        _numbersBox.IsEnabled = enabled;
        _textBox.IsEnabled = enabled;
        _logicalsBox.IsEnabled = enabled;
        _errorsBox.IsEnabled = enabled;
    }
}
