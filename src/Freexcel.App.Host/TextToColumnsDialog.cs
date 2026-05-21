using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

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
    private readonly TextBlock _stepOneIndicator = CreateStepIndicator("Step 1 of 3", true);
    private readonly TextBlock _stepTwoIndicator = CreateStepIndicator("Step 2 of 3", false);
    private readonly TextBlock _stepThreeIndicator = CreateStepIndicator("Step 3 of 3", false);
    private readonly RadioButton _delimitedButton = new() { Content = "Delimited", IsChecked = true, GroupName = "TextToColumnsSourceMode" };
    private readonly RadioButton _fixedWidthButton = new() { Content = "Fixed width", GroupName = "TextToColumnsSourceMode" };
    private readonly CheckBox _tabBox = new() { Content = "_Tab" };
    private readonly CheckBox _semicolonBox = new() { Content = "_Semicolon" };
    private readonly CheckBox _commaBox = new() { Content = "_Comma", IsChecked = true };
    private readonly CheckBox _spaceBox = new() { Content = "S_pace" };
    private readonly CheckBox _otherBox = new() { Content = "_Other:" };
    private readonly TextBox _customBox = new() { Width = 48, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox _textQualifierBox = new() { Width = 120 };
    private readonly ListView _previewGrid = new() { Height = 88 };
    private readonly TextBox _destinationBox = new() { Text = "$A$1" };

    public TextToColumnsDialogResult? Result { get; private set; }

    public TextToColumnsDialog()
    {
        Title = "Text to Columns";
        Width = 520;
        Height = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _textQualifierBox.ItemsSource = new[] { "\"", "'", "{none}" };
        _textQualifierBox.SelectedIndex = 0;
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

        body.Children.Add(CreateStepRow());
        body.Children.Add(new TextBlock
        {
            Text = "Choose the file type that best describes your data:",
            Margin = new Thickness(0, 10, 0, 6)
        });
        body.Children.Add(_delimitedButton);
        body.Children.Add(new TextBlock
        {
            Text = "Characters such as commas or tabs separate each field.",
            Margin = new Thickness(24, 0, 0, 6),
            Foreground = Brushes.DimGray
        });
        body.Children.Add(_fixedWidthButton);
        body.Children.Add(new TextBlock
        {
            Text = "Fields are aligned in columns with spaces between each field.",
            Margin = new Thickness(24, 0, 0, 12),
            Foreground = Brushes.DimGray
        });
        body.Children.Add(CreateDelimiterPanel());
        body.Children.Add(CreateQualifierPanel());
        body.Children.Add(new TextBlock { Text = "Data preview", Margin = new Thickness(0, 10, 0, 4) });
        body.Children.Add(_previewGrid);
        body.Children.Add(new TextBlock { Text = "_Destination:", Margin = new Thickness(0, 10, 0, 4) });
        body.Children.Add(CreateReferenceEditor(_destinationBox, "Select destination cell"));

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

    private static TextBlock CreateStepIndicator(string text, bool isCurrent) =>
        new()
        {
            Text = text,
            FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isCurrent ? Brushes.Black : Brushes.DimGray,
            Margin = new Thickness(0, 0, 14, 0)
        };

    private StackPanel CreateStepRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_stepOneIndicator);
        row.Children.Add(_stepTwoIndicator);
        row.Children.Add(_stepThreeIndicator);
        return row;
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

    private DockPanel CreateQualifierPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var label = new TextBlock
        {
            Text = "Text _qualifier:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(label, Dock.Left);
        panel.Children.Add(label);
        panel.Children.Add(_textQualifierBox);
        return panel;
    }

    private static DockPanel CreateReferenceEditor(TextBox textBox, string automationName)
    {
        var panel = new DockPanel();
        var pickerButton = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = textBox
        };
        AutomationProperties.SetName(pickerButton, automationName);
        pickerButton.Click += ReferencePickerButton_Click;
        panel.Children.Add(pickerButton);
        panel.Children.Add(textBox);
        return panel;
    }

    private static void ReferencePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox textBox })
            return;

        textBox.Focus();
        textBox.SelectAll();
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
