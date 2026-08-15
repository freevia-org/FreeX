using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF delimiter-picker adapter over <see cref="TableTextConversionDialogPlanner"/>.
/// </summary>
internal sealed class TableTextConversionDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ListBox _choices;

    // Kept parameterless so the visual-evidence harness can construct the production surface.
    internal TableTextConversionDialog()
        : this(TableTextConversionDialogPlanner.ResolveText(UiText.Get).TextToTableTitle)
    {
    }

    private TableTextConversionDialog(string title)
    {
        var text = TableTextConversionDialogPlanner.ResolveText(UiText.Get);
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        _choices = new ListBox
        {
            MinWidth = 240,
            MinHeight = 90,
            Margin = new Thickness(0, 0, 0, 12),
            ItemsSource = text.Choices.Select(choice => choice.Label).ToArray(),
            SelectedIndex = TableTextConversionDialogPlanner.DefaultChoiceIndex,
        };
        _choices.MouseDoubleClick += (_, _) => Accept();

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = text.PromptLabel, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(_choices);
        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72));
        Content = panel;

        Loaded += (_, _) => _choices.Focus();
    }

    public char? Result { get; private set; }

    public static char? Ask(Window? owner, string title)
    {
        var dialog = new TableTextConversionDialog(title) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Accept()
    {
        if (TableTextConversionDialogPlanner.DelimiterAt(_choices.SelectedIndex) is not { } delimiter)
            return;

        Result = delimiter;
        DialogResult = true;
    }
}
