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
    private readonly CheckBox _tabBox = new() { Content = "_Tab" };
    private readonly CheckBox _semicolonBox = new() { Content = "_Semicolon" };
    private readonly CheckBox _commaBox = new() { Content = "_Comma", IsChecked = true };
    private readonly CheckBox _spaceBox = new() { Content = "S_pace" };
    private readonly CheckBox _otherBox = new() { Content = "_Other:" };
    private readonly TextBox _customBox = new() { Width = 48, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ListView _previewGrid = new() { Height = 88 };

    public TextToColumnsDialogResult? Result { get; private set; }

    public TextToColumnsDialog()
    {
        Title = "Text to Columns";
        Width = 500;
        Height = 300;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _previewGrid.ItemsSource = new[]
        {
            new { Column1 = "East", Column2 = "42", Column3 = "Open" },
            new { Column1 = "West", Column2 = "7", Column3 = "Closed" },
            new { Column1 = "North", Column2 = "18", Column3 = "Ready" }
        };
        _previewGrid.View = new GridView
        {
            Columns =
            {
                new GridViewColumn { Header = "Column 1", DisplayMemberBinding = new System.Windows.Data.Binding("Column1"), Width = 130 },
                new GridViewColumn { Header = "Column 2", DisplayMemberBinding = new System.Windows.Data.Binding("Column2"), Width = 100 },
                new GridViewColumn { Header = "Column 3", DisplayMemberBinding = new System.Windows.Data.Binding("Column3"), Width = 130 }
            }
        };

        _otherBox.Checked += (_, _) => _customBox.Focus();

        var root = new DockPanel { Margin = new Thickness(12) };
        var body = new StackPanel();
        DockPanel.SetDock(body, Dock.Top);
        root.Children.Add(body);

        body.Children.Add(new TextBlock
        {
            Text = "Choose the delimiter that separates the selected text.",
            Margin = new Thickness(0, 0, 0, 10)
        });
        body.Children.Add(CreateDelimiterPanel());
        body.Children.Add(new TextBlock { Text = "Data preview", Margin = new Thickness(0, 10, 0, 4) });
        body.Children.Add(_previewGrid);

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

    private GroupBox CreateDelimiterPanel()
    {
        var panel = new WrapPanel();
        panel.Children.Add(_tabBox);
        panel.Children.Add(_semicolonBox);
        panel.Children.Add(_commaBox);
        panel.Children.Add(_spaceBox);

        var otherPanel = new StackPanel { Orientation = Orientation.Horizontal };
        otherPanel.Children.Add(_otherBox);
        otherPanel.Children.Add(_customBox);
        panel.Children.Add(otherPanel);

        return new GroupBox
        {
            Header = "Delimiters",
            Content = panel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private TextToColumnsDelimiterKind SelectedDelimiterKind()
    {
        if (_otherBox.IsChecked == true)
            return TextToColumnsDelimiterKind.Custom;
        if (_tabBox.IsChecked == true)
            return TextToColumnsDelimiterKind.Tab;
        if (_semicolonBox.IsChecked == true)
            return TextToColumnsDelimiterKind.Semicolon;
        if (_spaceBox.IsChecked == true)
            return TextToColumnsDelimiterKind.Space;

        return TextToColumnsDelimiterKind.Comma;
    }

    private void Accept()
    {
        try
        {
            Result = CreateResult(SelectedDelimiterKind(), _customBox.Text);
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
