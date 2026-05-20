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
        Width = 300;
        Height = 260;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var optionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(optionPanel, Dock.Top);
        root.Children.Add(optionPanel);

        foreach (var choice in GetChoices())
        {
            var button = new RadioButton
            {
                Content = choice.Label,
                Tag = choice.Kind,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _buttons.Add(button);
            optionPanel.Children.Add(button);
        }

        if (_buttons.Count > 0)
            _buttons[0].IsChecked = true;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var ok = new Button { Content = "_OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "_Cancel", Width = 72, IsCancel = true });

        Content = root;
    }

    public static IReadOnlyList<GoToSpecialChoice> GetChoices() =>
        [
            new(GoToSpecialKind.Blanks, "_Blanks"),
            new(GoToSpecialKind.Constants, "_Constants"),
            new(GoToSpecialKind.Formulas, "_Formulas"),
            new(GoToSpecialKind.Comments, "Co_mments"),
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

    private void Accept()
    {
        var selected = _buttons.FirstOrDefault(button => button.IsChecked == true);
        SelectedKind = selected?.Tag is GoToSpecialKind kind ? kind : GoToSpecialKind.Blanks;
        DialogResult = true;
    }
}
