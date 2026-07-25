using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

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
        Width = GoToSpecialDialogPlanner.Width;
        Height = GoToSpecialDialogPlanner.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(GoToSpecialDialogPlanner.ContentMargin) };
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
        buttons.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static IReadOnlyList<GoToSpecialChoice> GetChoices() =>
        GoToSpecialDialogPlanner.BuildChoices(CreateDialogText());

    public static bool TryParseChoice(string text, out GoToSpecialKind kind)
        => GoToSpecialDialogPlanner.TryParseChoice(text, out kind, CreateDialogText());

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
        var firstButton = FirstButton();
        firstButton?.Focus();
        if (firstButton is not null)
            Keyboard.Focus(firstButton);
    }

    private void Accept()
    {
        var selected = SelectedButton();
        SelectedKind = selected?.Tag is GoToSpecialKind kind ? kind : GoToSpecialKind.Blanks;
        SelectedOptions = GoToSpecialDialogPlanner.BuildOptions(SelectedKind, GetSelectedValueTypes());
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
        var selected = SelectedButton();
        var enabled = selected?.Tag is GoToSpecialKind kind && GoToSpecialDialogPlanner.UsesValueTypeOptions(kind);
        _numbersBox.IsEnabled = enabled;
        _textBox.IsEnabled = enabled;
        _logicalsBox.IsEnabled = enabled;
        _errorsBox.IsEnabled = enabled;
    }

    private RadioButton? FirstButton()
    {
        foreach (var button in _buttons)
            return button;

        return null;
    }

    private RadioButton? SelectedButton()
    {
        foreach (var button in _buttons)
        {
            if (button.IsChecked == true)
                return button;
        }

        return null;
    }

    private static GoToSpecialDialogText CreateDialogText() =>
        new(
            UiText.Get("GoToSpecial_Blanks"),
            UiText.Get("GoToSpecial_Constants"),
            UiText.Get("GoToSpecial_Formulas"),
            UiText.Get("GoToSpecial_Comments"),
            UiText.Get("GoToSpecial_CurrentRegion"),
            UiText.Get("GoToSpecial_RowDifferences"),
            UiText.Get("GoToSpecial_ColumnDifferences"),
            UiText.Get("GoToSpecial_LastCell"),
            UiText.Get("GoToSpecial_ConditionalFormats"),
            UiText.Get("GoToSpecial_Objects"),
            UiText.Get("GoToSpecial_Precedents"),
            UiText.Get("GoToSpecial_Dependents"),
            UiText.Get("GoToSpecial_DataValidation"),
            UiText.Get("GoToSpecial_VisibleCellsOnly"));
}
