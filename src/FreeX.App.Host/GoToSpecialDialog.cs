using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public sealed record GoToSpecialChoice(GoToSpecialKind Kind, string Label);

public sealed class GoToSpecialDialog : Window
{
    private readonly List<RadioButton> _buttons = [];
    private readonly CheckBox _numbersBox = new() { Content = UiText.Get("GoToSpecial_Numbers"), IsChecked = true, Margin = new Thickness(0, 0, 18, 4) };
    private readonly CheckBox _textBox = new() { Content = UiText.Get("GoToSpecial_Text"), IsChecked = true, Margin = new Thickness(0, 0, 18, 4) };
    private readonly CheckBox _logicalsBox = new() { Content = UiText.Get("GoToSpecial_Logicals"), IsChecked = true, Margin = new Thickness(0, 0, 18, 4) };
    private readonly CheckBox _errorsBox = new() { Content = UiText.Get("GoToSpecial_Errors"), IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };

    public GoToSpecialKind SelectedKind { get; private set; } = GoToSpecialKind.Blanks;
    public GoToSpecialOptions SelectedOptions { get; private set; } = new();

    public GoToSpecialDialog()
    {
        Title = UiText.Get("GoToSpecial_GoToSpecial");
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
            Text = UiText.Get("GoToSpecial_Select"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var availableGroup = new GroupBox { Header = UiText.Get("GoToSpecial_GoToSpecial"), Margin = new Thickness(0, 0, 0, 10) };
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
            new(GoToSpecialKind.Blanks, UiText.Get("GoToSpecial_Blanks")),
            new(GoToSpecialKind.Constants, UiText.Get("GoToSpecial_Constants")),
            new(GoToSpecialKind.Formulas, UiText.Get("GoToSpecial_Formulas")),
            new(GoToSpecialKind.Comments, UiText.Get("GoToSpecial_Comments")),
            new(GoToSpecialKind.CurrentRegion, UiText.Get("GoToSpecial_CurrentRegion")),
            new(GoToSpecialKind.RowDifferences, UiText.Get("GoToSpecial_RowDifferences")),
            new(GoToSpecialKind.ColumnDifferences, UiText.Get("GoToSpecial_ColumnDifferences")),
            new(GoToSpecialKind.LastCell, UiText.Get("GoToSpecial_LastCell")),
            new(GoToSpecialKind.ConditionalFormats, UiText.Get("GoToSpecial_ConditionalFormats")),
            new(GoToSpecialKind.Objects, UiText.Get("GoToSpecial_Objects")),
            new(GoToSpecialKind.Precedents, UiText.Get("GoToSpecial_Precedents")),
            new(GoToSpecialKind.Dependents, UiText.Get("GoToSpecial_Dependents")),
            new(GoToSpecialKind.DataValidation, UiText.Get("GoToSpecial_DataValidation")),
            new(GoToSpecialKind.VisibleCellsOnly, UiText.Get("GoToSpecial_VisibleCellsOnly"))
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
            Header = UiText.Get("GoToSpecial_ValuesForConstantsAndFormulas"),
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
        SelectedOptions = UsesValueTypeOptions(SelectedKind)
            ? new GoToSpecialOptions(GetSelectedValueTypes())
            : new GoToSpecialOptions();
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
        return valueTypes;
    }

    private void RefreshValueTypeOptions()
    {
        var selected = _buttons.FirstOrDefault(button => button.IsChecked == true);
        var enabled = selected?.Tag is GoToSpecialKind kind && UsesValueTypeOptions(kind);
        _numbersBox.IsEnabled = enabled;
        _textBox.IsEnabled = enabled;
        _logicalsBox.IsEnabled = enabled;
        _errorsBox.IsEnabled = enabled;
    }

    private static bool UsesValueTypeOptions(GoToSpecialKind kind) =>
        kind is GoToSpecialKind.Constants or GoToSpecialKind.Formulas;
}
