using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public enum SparklineKindChoice
{
    Line,
    Column,
    WinLoss
}

public sealed record SparklineDialogResult(string DataRangeText, string LocationText, SparklineKindChoice Kind);

public sealed class SparklineDialog : Window
{
    private readonly TextBox _dataRangeBox = new();
    private readonly TextBox _locationBox = new();
    private readonly ComboBox _kindBox = new();
    private readonly Button _dataRangePickerButton = new() { Content = "_Select Data Range", Width = 132, ToolTip = "Select Data Range" };
    private readonly Button _locationPickerButton = new() { Content = "Select _Location Range", Width = 152, ToolTip = "Select Location Range" };

    public SparklineDialogResult Result { get; private set; }

    public SparklineDialog(string dataRangeText, string locationText, SparklineKindChoice kind)
    {
        Result = CreateResult(dataRangeText, locationText, kind);
        Title = "Insert Sparkline";
        Width = 380;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _dataRangePickerButton.Click += (_, _) => FocusRangeBox(_dataRangeBox);
        _locationPickerButton.Click += (_, _) => FocusRangeBox(_locationBox);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label { Content = "_Data range:", Target = _dataRangeBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _dataRangeBox.Text = Result.DataRangeText;
        stack.Children.Add(CreateRangePickerRow(_dataRangeBox, _dataRangePickerButton));
        stack.Children.Add(new Label { Content = "_Location:", Target = _locationBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _locationBox.Text = Result.LocationText;
        stack.Children.Add(CreateRangePickerRow(_locationBox, _locationPickerButton));
        stack.Children.Add(new Label { Content = "Sparkline _type:", Target = _kindBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        _kindBox.ItemsSource = Enum.GetValues<SparklineKindChoice>();
        _kindBox.SelectedItem = kind;
        _kindBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_kindBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static SparklineDialogResult CreateResult(string dataRangeText, string locationText, SparklineKindChoice kind) =>
        new(dataRangeText.Trim(), locationText.Trim(), kind);

    private void Accept()
    {
        Result = CreateResult(
            _dataRangeBox.Text,
            _locationBox.Text,
            _kindBox.SelectedItem is SparklineKindChoice kind ? kind : SparklineKindChoice.Line);
        DialogResult = true;
    }

    private static StackPanel CreateRangePickerRow(TextBox textBox, Button pickerButton)
    {
        textBox.Height = 24;
        textBox.Width = 190;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(textBox);
        pickerButton.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(pickerButton);
        return row;
    }

    private static void FocusRangeBox(TextBox textBox)
    {
        textBox.Focus();
        textBox.SelectAll();
    }
}
