using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Create New Theme Fonts" dialog (Design &gt; Document Formatting &gt; Fonts &gt; Customize Fonts…).
/// Lets the user pick a heading font and a body font, creating and applying a custom
/// <see cref="DocumentFontSet"/>. Returns a <see cref="DocumentFontSet"/> on OK, or null on Cancel.
/// </summary>
internal sealed class CustomizeThemeFontsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // Common fonts to list in each combo (heading / body). The same list for both — user can type anything.
    private static readonly string[] CommonFonts =
    [
        "Arial", "Calibri", "Calibri Light", "Cambria", "Century Gothic",
        "Comic Sans MS", "Consolas", "Constantia", "Corbel", "Courier New",
        "Garamond", "Georgia", "Gill Sans MT", "Impact", "Lucida Sans",
        "Palatino Linotype", "Segoe UI", "Tahoma", "Times New Roman",
        "Trebuchet MS", "Verdana"
    ];

    private readonly ComboBox _headingCombo;
    private readonly ComboBox _bodyCombo;
    private readonly TextBox _nameBox;
    private DocumentFontSet? _result;

    private CustomizeThemeFontsDialog(Window? owner, DocumentFontSet current)
    {
        Owner = owner;
        Title = "Create New Theme Fonts";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _headingCombo = FontCombo(current.HeadingFont);
        _bodyCombo    = FontCombo(current.BodyFont);
        _nameBox      = new TextBox { Text = "Custom", MinWidth = 200 };

        Content = BuildContent();
        Loaded += (_, _) => _headingCombo.Focus();
    }

    private UIElement BuildContent()
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = "Type a font name or select one from the list.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(hint, 0);
        Grid.SetColumnSpan(hint, 2);
        grid.Children.Add(hint);

        void AddRow(int row, string label, UIElement field)
        {
            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4)
            };
            if (field is FrameworkElement fe)
                fe.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            Grid.SetRow(field, row); Grid.SetColumn(field, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(field);
        }

        AddRow(1, "Heading font:", _headingCombo);
        AddRow(2, "Body font:",    _bodyCombo);

        var sep = new Separator { Margin = new Thickness(0, 6, 0, 2) };
        Grid.SetRow(sep, 3); Grid.SetColumnSpan(sep, 2);
        grid.Children.Add(sep);

        AddRow(4, "Name:", _nameBox);

        // Append button row below the grid.
        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72,
            rowMargin: new Thickness(14, 8, 14, 14)));
        return panel;
    }

    private static ComboBox FontCombo(string current)
    {
        var combo = new ComboBox { IsEditable = true, MinWidth = 200 };
        foreach (var f in CommonFonts)
            combo.Items.Add(f);
        combo.Text = current;
        return combo;
    }

    private void Accept()
    {
        var heading = (_headingCombo.Text ?? string.Empty).Trim();
        var body    = (_bodyCombo.Text ?? string.Empty).Trim();
        var name    = _nameBox.Text.Trim();

        if (string.IsNullOrEmpty(heading))
        {
            DialogMessageHelper.ShowWarning(this, "Enter a heading font name.", Title);
            _headingCombo.Focus();
            return;
        }
        if (string.IsNullOrEmpty(body))
        {
            DialogMessageHelper.ShowWarning(this, "Enter a body font name.", Title);
            _bodyCombo.Focus();
            return;
        }
        if (string.IsNullOrEmpty(name))
            name = "Custom";

        _result = new DocumentFontSet(name, heading, body);
        Close();
    }

    /// <summary>
    /// Show the Customize Fonts dialog seeded with the current font set. Returns a new
    /// <see cref="DocumentFontSet"/> on OK, or null on Cancel.
    /// </summary>
    public static DocumentFontSet? Prompt(Window? owner, DocumentFontSet current)
    {
        var dialog = new CustomizeThemeFontsDialog(owner, current);
        dialog.ShowDialog();
        return dialog._result;
    }
}
