using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

internal sealed class PasteSpecialDialog : FreeWDialogWindow
{
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
        Title = "Paste Special";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _list.ItemsSource = PasteSpecialOptionCatalog.Options;
        _list.SelectedIndex = 0;
        _description.Text = PasteSpecialOptionCatalog.Options[0].Description;
        _list.SelectionChanged += (_, _) => RefreshDescription();
        _list.DoubleTapped += (_, _) => Accept();

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Paste As:",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(_list);
        panel.Children.Add(_description);

        var ok = Button("OK", isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = Button("Cancel", isCancel: true);
        cancel.Click += (_, _) => Close(null);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, cancel },
        });

        Content = panel;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    public static Task<PasteSpecialOption?> ShowAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new PasteSpecialDialog().ShowDialog<PasteSpecialOption?>(owner);
    }

    private void RefreshDescription()
    {
        if (_list.SelectedItem is PasteSpecialOptionChoice row)
            _description.Text = row.Description;
    }

    private void Accept()
    {
        if (_list.SelectedItem is PasteSpecialOptionChoice row)
            Close(row.Option);
    }

    private static Button Button(string label, bool isDefault = false, bool isCancel = false) => new()
    {
        Content = label,
        MinWidth = 72,
        Padding = new Thickness(10, 4),
        IsDefault = isDefault,
        IsCancel = isCancel,
    };

}
