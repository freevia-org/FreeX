using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace FreeW.App.Avalonia;

/// <summary>Minimal modal editor for a table cell's text. Returns the new text, or null if cancelled.</summary>
internal sealed class CellEditDialog : Window
{
    private readonly TextBox _box;

    public CellEditDialog(string initial)
    {
        Title = "Edit cell";
        Width = 380;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _box = new TextBox { Text = initial, AcceptsReturn = false };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72 };
        ok.Click += (_, _) => Close(_box.Text ?? string.Empty);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { ok, cancel },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = "Cell text:", Margin = new Thickness(0, 0, 0, 6) },
                _box,
                buttons,
            },
        };
    }
}
