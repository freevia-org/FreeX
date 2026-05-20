using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class GoToDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _addressBox = new();

    public CellAddress SelectedAddress { get; private set; }

    public GoToDialog(SheetId sheetId, string defaultAddress = "A1")
    {
        _sheetId = sheetId;
        Title = "Go To";
        Width = 320;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(new Label
        {
            Content = "_Reference:",
            Target = _addressBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 12)
        });

        _addressBox.Text = defaultAddress;
        _addressBox.Margin = new Thickness(0, 0, 0, 12);
        Grid.SetColumn(_addressBox, 1);
        root.Children.Add(_addressBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        var ok = new Button { Content = "_OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "_Cancel", Width = 72, IsCancel = true });

        Content = root;
    }

    public static bool TryParseAddress(string text, SheetId sheetId, out CellAddress address)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                address = default;
                return false;
            }

            address = CellAddress.Parse(text.Trim(), sheetId);
            return true;
        }
        catch
        {
            address = default;
            return false;
        }
    }

    private void Accept()
    {
        if (!TryParseAddress(_addressBox.Text, _sheetId, out var address))
        {
            MessageBox.Show(this, "Enter a valid cell reference, for example B5.", "Go To", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedAddress = address;
        DialogResult = true;
    }
}
