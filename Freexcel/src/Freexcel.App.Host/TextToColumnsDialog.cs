using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public enum TextToColumnsDelimiterKind
{
    Comma,
    Semicolon,
    Tab,
    Space,
    Custom
}

public sealed record TextToColumnsDialogResult(TextToColumnsDelimiterKind DelimiterKind, string Delimiter);

public sealed class TextToColumnsDialog : Window
{
    private readonly ComboBox _delimiterBox = new();
    private readonly TextBox _customBox = new();

    public TextToColumnsDialogResult? Result { get; private set; }

    public TextToColumnsDialog()
    {
        Title = "Text to Columns";
        Width = 320;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "Delimiter:", Margin = new Thickness(0, 0, 0, 4) });
        _delimiterBox.ItemsSource = Enum.GetValues<TextToColumnsDelimiterKind>();
        _delimiterBox.SelectedItem = TextToColumnsDelimiterKind.Comma;
        root.Children.Add(_delimiterBox);
        root.Children.Add(new TextBlock { Text = "Custom delimiter:", Margin = new Thickness(0, 10, 0, 4) });
        root.Children.Add(_customBox);
        root.Children.Add(CreateButtonRow(Accept));
        Content = root;
    }

    public static TextToColumnsDialogResult CreateResult(TextToColumnsDelimiterKind delimiterKind, string? customDelimiter = null)
    {
        var delimiter = delimiterKind switch
        {
            TextToColumnsDelimiterKind.Comma => ",",
            TextToColumnsDelimiterKind.Semicolon => ";",
            TextToColumnsDelimiterKind.Tab => "\t",
            TextToColumnsDelimiterKind.Space => " ",
            TextToColumnsDelimiterKind.Custom => string.IsNullOrEmpty(customDelimiter)
                ? throw new ArgumentException("Custom delimiter is required.", nameof(customDelimiter))
                : customDelimiter,
            _ => throw new ArgumentOutOfRangeException(nameof(delimiterKind), delimiterKind, "Unsupported delimiter.")
        };

        return new TextToColumnsDialogResult(delimiterKind, delimiter);
    }

    private void Accept()
    {
        try
        {
            Result = CreateResult((TextToColumnsDelimiterKind)_delimiterBox.SelectedItem, _customBox.Text);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
}
