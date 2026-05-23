using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public sealed record GoToSpecialChoice(GoToSpecialKind Kind, string Label);

public sealed class GoToSpecialDialog : Window
{
    private readonly List<RadioButton> _buttons = [];

    public GoToSpecialKind SelectedKind { get; private set; } = GoToSpecialKind.Blanks;

    public GoToSpecialDialog()
    {
        Title = "Go To Special";
        Width = 430;
        Height = 390;
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
            _buttons.Add(button);
            AddChoice(optionGrid, button, choiceRow++);
        }

        if (_buttons.Count > 0)
            _buttons[0].IsChecked = true;

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        Content = root;
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

    private static void AddChoice(Grid grid, RadioButton button, int index)
    {
        var row = index / 2;
        while (grid.RowDefinitions.Count <= row)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(button, row);
        Grid.SetColumn(button, index % 2);
        grid.Children.Add(button);
    }

    private void Accept()
    {
        var selected = _buttons.FirstOrDefault(button => button.IsChecked == true);
        SelectedKind = selected?.Tag is GoToSpecialKind kind ? kind : GoToSpecialKind.Blanks;
        DialogResult = true;
    }
}
