using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

internal sealed class PasteSpecialDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            ButtonHeight = 21,
            ButtonPadding = new Thickness(10, 1),
            ListBoxItemMinHeight = 21,
            ListBoxItemPadding = new Thickness(4, 0),
        };
    private readonly PasteSpecialDialogSession _session = new();

    private readonly ListBox _list = new()
    {
        MinWidth = 340,
        MinHeight = 92,
        Margin = new Thickness(0, 0, 0, 8),
        SelectionMode = SelectionMode.Single,
    };

    private readonly TextBlock _description = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.DarkGray,
        FontSize = 11,
        MinHeight = 32,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private PasteSpecialDialog()
    {
        Title = PasteSpecialDialogSession.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _list.ItemsSource = _session.Options;
        _list.SelectedIndex = 0;
        _description.Text = _session.State.Description;
        _list.SelectionChanged += (_, _) => RefreshDescription();
        _list.DoubleTapped += (_, _) => Accept();

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = PasteSpecialDialogSession.PasteAsLabel,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(_list);
        panel.Children.Add(_description);

        var actionRow = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            Accept,
            () => Close(null),
            buttonWidth: 72,
            margin: new Thickness(0, 4, 0, 0),
            style: DialogChromeStyle);
        panel.Children.Add(actionRow);

        Content = panel;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
        Opened += (_, _) =>
        {
            AvaloniaCompactDialogChrome.ApplyListBox(_list, DialogChromeStyle);
            foreach (var button in actionRow.Children.OfType<Button>())
                AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, 72, button.IsDefault);
            _list.Focus(NavigationMethod.Tab);
        };
    }

    public static Task<PasteSpecialOption?> ShowAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new PasteSpecialDialog().ShowDialog<PasteSpecialOption?>(owner);
    }

    private void RefreshDescription()
    {
        _description.Text = _session.UpdateSelection(_list.SelectedIndex).Description;
    }

    private void Accept()
    {
        _session.UpdateSelection(_list.SelectedIndex);
        if (_session.PlanAcceptance() is { } option)
            Close(option);
    }

}
