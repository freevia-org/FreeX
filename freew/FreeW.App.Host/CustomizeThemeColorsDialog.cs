using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Create New Theme Colors" dialog (Design &gt; Document Formatting &gt; Colors &gt; Customize Colors…).
/// Lets the user author a custom <see cref="DocumentTheme"/> color scheme by picking all 12 DrawingML
/// <c>a:clrScheme</c> slots. Returns a <see cref="DocumentTheme"/> whose palette carries the chosen
/// values (Name = "Custom", fonts preserved from the current theme) on OK, or null on Cancel.
/// </summary>
internal sealed class CustomizeThemeColorsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // The 12 a:clrScheme slot labels in order (matching ThemeColorScheme's constructor).
    private static readonly (string Label, string FieldName)[] Slots =
    [
        ("Dark 1 (Text/Background)",   "Dark1"),
        ("Light 1 (Background/Text)",  "Light1"),
        ("Dark 2 (Text/Background)",   "Dark2"),
        ("Light 2 (Background/Text)",  "Light2"),
        ("Accent 1",                   "Accent1"),
        ("Accent 2",                   "Accent2"),
        ("Accent 3",                   "Accent3"),
        ("Accent 4",                   "Accent4"),
        ("Accent 5",                   "Accent5"),
        ("Accent 6",                   "Accent6"),
        ("Hyperlink",                  "Hyperlink"),
        ("Followed Hyperlink",         "FollowedHyperlink"),
    ];

    private readonly TextBox[] _hexBoxes = new TextBox[12];
    private readonly TextBox _nameBox;
    private readonly DocumentTheme _currentTheme;
    private DocumentTheme? _result;

    private CustomizeThemeColorsDialog(Window? owner, DocumentTheme currentTheme)
    {
        Owner = owner;
        _currentTheme = currentTheme;
        Title = "Create New Theme Colors";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // Seed the boxes from the current theme's color scheme.
        var scheme = currentTheme.ColorScheme;
        var schemeValues = new[] {
            scheme.Dark1, scheme.Light1, scheme.Dark2, scheme.Light2,
            scheme.Accent1, scheme.Accent2, scheme.Accent3,
            scheme.Accent4, scheme.Accent5, scheme.Accent6,
            scheme.Hyperlink, scheme.FollowedHyperlink
        };

        for (var i = 0; i < 12; i++)
            _hexBoxes[i] = new TextBox { Text = "#" + schemeValues[i], MinWidth = 120 };

        _nameBox = new TextBox { Text = "Custom", MinWidth = 200 };

        Content = BuildContent();
        Loaded += (_, _) => _nameBox.Focus();
    }

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock
        {
            Text = "Enter RRGGBB hex values (with or without #) for each color slot.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 10)
        });

        // Color slot rows.
        for (var i = 0; i < 12; i++)
        {
            var grid = SlotRow(Slots[i].Label, _hexBoxes[i]);
            panel.Children.Add(grid);
        }

        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(SlotRow("Name:", _nameBox));

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        panel.Children.Add(buttons);

        return panel;
    }

    private static Grid SlotRow(string label, UIElement field)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(field, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(field);
        return grid;
    }

    private static bool TryParseHex(string text, out string rrggbb)
    {
        var trimmed = text.Trim().TrimStart('#');
        if (trimmed.Length == 6)
        {
            try
            {
                ColorConverter.ConvertFromString("#" + trimmed);
                rrggbb = trimmed.ToUpperInvariant();
                return true;
            }
            catch { }
        }
        rrggbb = string.Empty;
        return false;
    }

    private void Accept()
    {
        var values = new string[12];
        for (var i = 0; i < 12; i++)
        {
            if (!TryParseHex(_hexBoxes[i].Text, out values[i]))
            {
                DialogMessageHelper.ShowWarning(this,
                    $"Enter a valid 6-digit hex colour for '{Slots[i].Label}' (e.g. #2F5496 or 2F5496).", Title);
                _hexBoxes[i].Focus();
                return;
            }
        }

        var name = _nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
            name = "Custom";

        var scheme = new ThemeColorScheme(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11]);

        // Infer the best-matching preset; falls back to a Custom theme carrying the chosen colours.
        _result = DocumentTheme.InferPreset(
            scheme,
            _currentTheme.HeadingFont,
            _currentTheme.BodyFont,
            _currentTheme.EffectSetName);

        // If InferPreset returned a named preset but user typed a custom name, keep as Custom.
        if (!string.Equals(_result.Name, name, System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "Custom", System.StringComparison.OrdinalIgnoreCase))
        {
            _result = _result with { Name = name };
        }

        Close();
    }

    /// <summary>
    /// Show the Customize Colors dialog seeded with the current theme's color scheme. Returns a
    /// <see cref="DocumentTheme"/> carrying the chosen 12-slot scheme on OK, or null on Cancel.
    /// </summary>
    public static DocumentTheme? Prompt(Window? owner, DocumentTheme currentTheme)
    {
        var dialog = new CustomizeThemeColorsDialog(owner, currentTheme);
        dialog.ShowDialog();
        return dialog._result;
    }
}
