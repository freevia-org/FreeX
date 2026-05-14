using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class ConditionalFormatDialog : Window
{
    public ConditionalFormat? ResultRule { get; private set; }

    private readonly string _ruleType;
    private readonly GridRange _range;
    private readonly TextBox _value1Box;
    private readonly TextBox _value2Box;
    private readonly Label _value2Label;
    private readonly ComboBox _colorBox;

    private static readonly (string Label, Color Color)[] ColorOptions =
    [
        ("Light Red Fill",    Color.FromRgb(255, 199, 206)),
        ("Yellow Fill",       Color.FromRgb(255, 235, 132)),
        ("Green Fill",        Color.FromRgb(198, 239, 206)),
        ("Light Blue Fill",   Color.FromRgb(189, 215, 238)),
        ("Bold Red Text",     Color.FromRgb(255, 0, 0)),
        ("Bold Green Text",   Color.FromRgb(0, 176, 80)),
    ];

    public ConditionalFormatDialog(string ruleType, GridRange range)
    {
        _ruleType = ruleType;
        _range = range;
        Title = $"Conditional Formatting — {ruleType}";
        Width = 380; Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        bool isBetween = ruleType is "Between";
        bool needsValue = ruleType is "Greater Than" or "Less Than" or "Equal To"
                                   or "Between" or "Text Contains";

        var lbl1 = new Label { Content = isBetween ? "Minimum:" : "Value:", Padding = new Thickness(0) };
        Grid.SetRow(lbl1, 0);
        _value1Box = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        Grid.SetRow(_value1Box, 1);

        _value2Label = new Label { Content = "Maximum:", Padding = new Thickness(0),
            Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed };
        Grid.SetRow(_value2Label, 2);
        _value2Box = new TextBox { Margin = new Thickness(0, 4, 0, 8),
            Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed };

        var colorLabel = new Label { Content = "Format:", Padding = new Thickness(0) };
        _colorBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (lbl, _) in ColorOptions) _colorBox.Items.Add(lbl);
        _colorBox.SelectedIndex = 0;

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);

        var inner = new StackPanel();
        if (needsValue)
        {
            inner.Children.Add(lbl1);
            inner.Children.Add(_value1Box);
            if (isBetween) { inner.Children.Add(_value2Label); inner.Children.Add(_value2Box); }
        }
        inner.Children.Add(colorLabel);
        inner.Children.Add(_colorBox);
        inner.Children.Add(btnRow);
        inner.Margin = new Thickness(16);
        Content = inner;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var cf = new ConditionalFormat { AppliesTo = _range };
        var (_, fillColor) = ColorOptions[_colorBox.SelectedIndex < 0 ? 0 : _colorBox.SelectedIndex];

        cf.RuleType = _ruleType switch
        {
            "Data Bar"    => CfRuleType.DataBar,
            "Color Scale" => CfRuleType.ColorScale,
            "Above Average" or "Below Average" => CfRuleType.AboveAverage,
            "Top 10 Items" or "Bottom 10 Items" => CfRuleType.Top10,
            _ => CfRuleType.CellValue
        };

        if (cf.RuleType == CfRuleType.CellValue)
        {
            cf.Operator = _ruleType switch
            {
                "Greater Than" => CfOperator.GreaterThan,
                "Less Than"    => CfOperator.LessThan,
                "Equal To"     => CfOperator.Equal,
                "Between"      => CfOperator.Between,
                _              => CfOperator.NotEqual
            };
            cf.Value1 = _value1Box.Text.Trim();
            cf.Value2 = _value2Box.Text.Trim();
        }

        cf.AboveAverage = _ruleType != "Below Average" && _ruleType != "Bottom 10 Items";
        cf.FormatIfTrue = new CellStyle
        {
            FillColor = new CellColor(fillColor.R, fillColor.G, fillColor.B)
        };

        ResultRule = cf;
        DialogResult = true;
    }
}
