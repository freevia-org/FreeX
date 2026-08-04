using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartTextOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartTextOptionsPlanner _planner;
    private readonly TextBox _fontFamilyBox;
    private readonly TextBox _fontSizeBox;
    private readonly ComboBox _boldCombo;
    private readonly ComboBox _italicCombo;
    private readonly TextBox _colorBox;

    internal ChartTextOptionsDialog(EditingSession editor, ChartTextTarget target = ChartTextTarget.Chart)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartTextOptionsPlanner.FromChart(chart, target);
        var surface = ChartTextOptionsPlanner.BuildSurfacePlan(target);

        Title = surface.Title;
        Width = ChartTextOptionsPlanner.DefaultDialogWidth;
        Height = ChartTextOptionsPlanner.DefaultDialogHeight;
        MinWidth = 380;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _fontFamilyBox = new TextBox { Text = _planner.FontFamily ?? string.Empty, MinWidth = 180 };
        _fontSizeBox = new TextBox { Text = Format(_planner.FontSizePt), MinWidth = 180 };
        _boldCombo = BuildBooleanCombo(_planner.Bold);
        _italicCombo = BuildBooleanCombo(_planner.Italic);
        _colorBox = new TextBox { Text = _planner.ColorText, MinWidth = 180 };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                MakeButton(surface.OkLabel, true, OnOk),
                MakeButton(surface.CancelLabel, false, () => Close(false)),
            },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                MakeRow(surface.FontFamilyLabel, _fontFamilyBox),
                MakeRow(surface.FontSizeLabel, _fontSizeBox),
                MakeRow(surface.BoldLabel, _boldCombo),
                MakeRow(surface.ItalicLabel, _italicCombo),
                MakeRow(surface.ColorLabel, _colorBox),
                new TextBlock { Text = surface.AutoHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartTextOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(string? fontFamily, double? fontSizePt, bool? bold, bool? italic, string? color)
    {
        _fontFamilyBox.Text = fontFamily ?? string.Empty;
        _fontSizeBox.Text = Format(fontSizePt);
        _boldCombo.SelectedIndex = FindBooleanIndex(bold);
        _italicCombo.SelectedIndex = FindBooleanIndex(italic);
        _colorBox.Text = color ?? string.Empty;
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartTextOptions(BuildCommitPlanForTests());
            Close(true);
        }
        catch (FormatException)
        {
            Close(false);
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetFontFamily(_fontFamilyBox.Text);
        _planner.SetFontSizePt(ChartTextOptionsPlanner.ParseOptionalFontSize(_fontSizeBox.Text));
        _planner.SetBold(ReadBoolean(_boldCombo));
        _planner.SetItalic(ReadBoolean(_italicCombo));
        _planner.SetColor(_colorBox.Text);
    }

    private static ComboBox BuildBooleanCombo(bool? value) => new()
    {
        ItemsSource = ChartTextOptionsPlanner.BooleanOptions.Select(option => option.Label).ToArray(),
        SelectedIndex = FindBooleanIndex(value),
        MinWidth = 180,
    };

    private static bool? ReadBoolean(ComboBox combo)
    {
        var index = combo.SelectedIndex;
        return index >= 0 && index < ChartTextOptionsPlanner.BooleanOptions.Count
            ? ChartTextOptionsPlanner.BooleanOptions[index].Value
            : null;
    }

    private static int FindBooleanIndex(bool? value) =>
        ChartTextOptionsPlanner.BooleanOptions
            .Select((option, index) => (option, index))
            .First(item => item.option.Value == value).index;

    private static string Format(double? value) => value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
