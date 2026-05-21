using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class GoToDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _addressBox = new();
    private readonly ListBox _historyList = new();

    public CellAddress SelectedAddress { get; private set; }
    public GoToSpecialKind? SelectedSpecialKind { get; private set; }

    public GoToDialog(SheetId sheetId, string defaultAddress = "A1")
    {
        _sheetId = sheetId;
        Title = "Go To";
        Width = 420;
        Height = 340;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(new TextBlock
        {
            Text = "Select a named or recently used reference, or type a cell reference.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var historyLabel = new Label
        {
            Content = "_Go to:",
            Target = _historyList,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 4)
        };
        Grid.SetRow(historyLabel, 0);
        Grid.SetColumnSpan(historyLabel, 2);
        root.Children.Add(historyLabel);

        _historyList.Items.Add(defaultAddress);
        _historyList.ToolTip = "Selection history";
        _historyList.MinHeight = 130;
        _historyList.Margin = new Thickness(0, 22, 0, 0);
        _historyList.SelectionChanged += (_, _) =>
        {
            if (_historyList.SelectedItem is string reference)
                _addressBox.Text = reference;
        };
        _historyList.SelectedIndex = 0;
        Grid.SetRow(_historyList, 1);
        Grid.SetColumnSpan(_historyList, 2);
        root.Children.Add(_historyList);

        var referenceLabel = new Label
        {
            Content = "_Reference:",
            Target = _addressBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 8, 12)
        };
        Grid.SetRow(referenceLabel, 2);
        root.Children.Add(referenceLabel);

        _addressBox.Text = defaultAddress;
        _addressBox.Margin = new Thickness(0, 8, 0, 12);
        Grid.SetRow(_addressBox, 2);
        Grid.SetColumn(_addressBox, 1);
        root.Children.Add(_addressBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 3);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        var special = new Button { Content = "S_pecial...", Width = 86, Margin = new Thickness(0, 0, 8, 0) };
        special.Click += (_, _) => OpenSpecialDialog();
        buttons.Children.Add(special);
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

    private void OpenSpecialDialog()
    {
        var dialog = new GoToSpecialDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        SelectedSpecialKind = dialog.SelectedKind;
        DialogResult = true;
    }

    private void Accept()
    {
        if (!TryParseAddress(_addressBox.Text, _sheetId, out var address))
        {
            MessageBox.Show(this, "Enter a valid cell reference, for example B5.", "Go To", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedAddress = address;
        SelectedSpecialKind = null;
        DialogResult = true;
    }
}
