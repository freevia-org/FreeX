using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public class ConditionalFormatDialog : Window
{
    public ConditionalFormat? ResultRule { get; private set; }

    private readonly string _ruleType;
    private readonly GridRange _range;
    private readonly Guid _existingId;
    private readonly TextBox _value1Box;
    private readonly TextBox _value2Box;
    private readonly Label _value2Label;
    private readonly ComboBox _colorBox;
    private readonly TextBox? _formulaBox;
    private readonly ComboBox _iconSetStyleBox;
    private readonly CheckBox _iconSetShowValueBox;
    private readonly CheckBox _iconSetReverseBox;
    private ConditionalFormat? _existingRule;

    private static readonly (string Label, Color Color)[] ColorOptions =
    [
        ("Light Red Fill",    Color.FromRgb(255, 199, 206)),
        ("Yellow Fill",       Color.FromRgb(255, 235, 132)),
        ("Green Fill",        Color.FromRgb(198, 239, 206)),
        ("Light Blue Fill",   Color.FromRgb(189, 215, 238)),
        ("Bold Red Text",     Color.FromRgb(255, 0, 0)),
        ("Bold Green Text",   Color.FromRgb(0, 176, 80)),
    ];

    private static readonly string[] IconSetStyles =
    [
        "3TrafficLights1",
        "3Arrows",
        "3Symbols",
        "4TrafficLights",
        "5Arrows"
    ];

    /// <summary>Creates a new-rule dialog for the given rule type and range.</summary>
    public ConditionalFormatDialog(string ruleType, GridRange range)
    {
        _ruleType   = ruleType;
        _range      = range;
        _existingId = Guid.NewGuid();

        Title = $"Conditional Formatting — {ruleType}";
        Width = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        bool isFormula = ruleType is "Formula" or "Use a Formula";
        bool isIconSet = ruleType is "Icon Set";
        bool isBetween = ruleType is "Between";
        bool needsValue = ruleType is "Greater Than" or "Less Than" or "Equal To"
                                   or "Between" or "Text Contains";

        var inner = new StackPanel { Margin = new Thickness(16) };
        _iconSetStyleBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var style in IconSetStyles) _iconSetStyleBox.Items.Add(style);
        _iconSetStyleBox.SelectedIndex = 0;
        _iconSetShowValueBox = new CheckBox { Content = "Show value", Margin = new Thickness(0, 0, 0, 6), IsChecked = true };
        _iconSetReverseBox = new CheckBox { Content = "Reverse icon order", Margin = new Thickness(0, 0, 0, 12) };

        if (isFormula)
        {
            Height = 200;
            inner.Children.Add(new Label { Content = "Formula:", Padding = new Thickness(0) });
            _formulaBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "=" };
            inner.Children.Add(_formulaBox);
            // placeholders needed by Ok_Click — never shown
            _value1Box  = new TextBox();
            _value2Box  = new TextBox();
            _value2Label = new Label();
        }
        else if (isIconSet)
        {
            Height = 230;
            inner.Children.Add(new Label { Content = "Icon set:", Padding = new Thickness(0) });
            inner.Children.Add(_iconSetStyleBox);
            inner.Children.Add(_iconSetShowValueBox);
            inner.Children.Add(_iconSetReverseBox);

            _value1Box  = new TextBox();
            _value2Box  = new TextBox();
            _value2Label = new Label();
        }
        else
        {
            Height = needsValue ? (isBetween ? 260 : 220) : 180;

            var lbl1 = new Label { Content = isBetween ? "Minimum:" : "Value:", Padding = new Thickness(0) };
            _value1Box  = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _value2Label = new Label { Content = "Maximum:", Padding = new Thickness(0),
                Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed };
            _value2Box  = new TextBox { Margin = new Thickness(0, 4, 0, 8),
                Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed };

            if (needsValue)
            {
                inner.Children.Add(lbl1);
                inner.Children.Add(_value1Box);
                if (isBetween) { inner.Children.Add(_value2Label); inner.Children.Add(_value2Box); }
            }
        }

        var colorLabel = new Label { Content = "Format:", Padding = new Thickness(0) };
        _colorBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (lbl, _) in ColorOptions) _colorBox.Items.Add(lbl);
        _colorBox.SelectedIndex = 0;

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok     = new Button { Content = "OK",     Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);

        if (!isIconSet)
        {
            inner.Children.Add(colorLabel);
            inner.Children.Add(_colorBox);
        }
        inner.Children.Add(btnRow);
        Content = inner;
    }

    /// <summary>Creates an edit dialog pre-populated with <paramref name="existingRule"/>.</summary>
    public ConditionalFormatDialog(ConditionalFormat existingRule)
        : this(ConditionalFormatDialogPlanner.RuleTypeLabel(existingRule), existingRule.AppliesTo)
    {
        _existingId = existingRule.Id;   // preserve Id so the command recognises it as an update
        _existingRule = ConditionalFormatDialogPlanner.CloneRule(existingRule);

        // Pre-populate value fields
        if (existingRule.RuleType == CfRuleType.Formula)
        {
            if (_formulaBox is not null)
                _formulaBox.Text = string.IsNullOrEmpty(existingRule.FormulaText)
                    ? "="
                    : $"={existingRule.FormulaText}";
        }
        else
        {
            _value1Box.Text = existingRule.Value1 ?? "";
            _value2Box.Text = existingRule.Value2 ?? "";
            if (existingRule.RuleType == CfRuleType.IconSet)
            {
                var style = string.IsNullOrWhiteSpace(existingRule.IconSetStyle)
                    ? IconSetStyles[0]
                    : existingRule.IconSetStyle;
                if (!IconSetStyles.Contains(style))
                    _iconSetStyleBox.Items.Add(style);
                _iconSetStyleBox.SelectedItem = style;
                _iconSetShowValueBox.IsChecked = existingRule.IconSetShowValue;
                _iconSetReverseBox.IsChecked = existingRule.IconSetReverse;
            }
        }

        // Pre-select the closest color option from FormatIfTrue.FillColor
        if (existingRule.FormatIfTrue?.FillColor is { } fc)
        {
            var wc = Color.FromRgb(fc.R, fc.G, fc.B);
            for (var i = 0; i < ColorOptions.Length; i++)
            {
                if (ColorOptions[i].Color == wc) { _colorBox.SelectedIndex = i; break; }
            }
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var cf = _existingRule is not null
            ? ConditionalFormatDialogPlanner.CloneRule(_existingRule)
            : new ConditionalFormat { Id = _existingId, AppliesTo = _range };
        cf.AppliesTo = _range;
        var (_, fillColor) = ColorOptions[_colorBox.SelectedIndex < 0 ? 0 : _colorBox.SelectedIndex];

        bool isFormula = _ruleType is "Formula" or "Use a Formula";

        if (isFormula)
        {
            cf.RuleType = CfRuleType.Formula;
            var raw = _formulaBox?.Text.Trim() ?? "";
            cf.FormulaText = raw.StartsWith('=') ? raw[1..] : raw;
        }
        else
        {
            cf.RuleType = _ruleType switch
            {
                "Data Bar"    => CfRuleType.DataBar,
                "Color Scale" => CfRuleType.ColorScale,
                "Icon Set"    => CfRuleType.IconSet,
                "Above Average" or "Below Average" => CfRuleType.AboveAverage,
                "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%" => CfRuleType.Top10,
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
            else if (cf.RuleType == CfRuleType.IconSet)
            {
                cf.IconSetStyle = _iconSetStyleBox.SelectedItem as string ?? IconSetStyles[0];
                cf.IconSetShowValue = _iconSetShowValueBox.IsChecked == true;
                cf.IconSetReverse = _iconSetReverseBox.IsChecked == true;
            }

            cf.AboveAverage = _ruleType is not ("Below Average" or "Bottom 10 Items" or "Bottom 10%");
            cf.TopBottomPercent = _ruleType is "Top 10%" or "Bottom 10%";
        }

        if (cf.RuleType != CfRuleType.IconSet)
        {
            cf.FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(fillColor.R, fillColor.G, fillColor.B)
            };
        }

        ResultRule = cf;
        DialogResult = true;
    }

}

public sealed class HighlightCellsRuleDialog : ConditionalFormatDialog
{
    public HighlightCellsRuleDialog(string ruleType, GridRange range)
        : base(ruleType, range)
    {
        Title = $"Highlight Cells Rule - {ruleType}";
    }
}

public sealed class TopBottomRuleDialog : ConditionalFormatDialog
{
    public TopBottomRuleDialog(string ruleType, GridRange range)
        : base(ruleType, range)
    {
        Title = $"Top/Bottom Rule - {ruleType}";
    }
}

public sealed class DataBarRuleDialog : ConditionalFormatDialog
{
    public DataBarRuleDialog(GridRange range)
        : base("Data Bar", range)
    {
        Title = "Data Bar Rule";
    }
}

public sealed class ColorScaleRuleDialog : ConditionalFormatDialog
{
    public ColorScaleRuleDialog(GridRange range)
        : base("Color Scale", range)
    {
        Title = "Color Scale Rule";
    }
}

public sealed class IconSetRuleDialog : ConditionalFormatDialog
{
    public IconSetRuleDialog(GridRange range)
        : base("Icon Set", range)
    {
        Title = "Icon Set Rule";
    }
}

public sealed class NewConditionalFormatRuleDialog : ConditionalFormatDialog
{
    public NewConditionalFormatRuleDialog(string ruleType, GridRange range)
        : base(ruleType, range)
    {
        Title = "New Formatting Rule";
    }
}

public static class ConditionalFormatDialogFactory
{
    public static ConditionalFormatDialog Create(string ruleType, GridRange range) =>
        ruleType switch
        {
            "Greater Than" or "Less Than" or "Equal To" or "Between" or "Text Contains" =>
                new HighlightCellsRuleDialog(ruleType, range),
            "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%" or "Above Average" or "Below Average" =>
                new TopBottomRuleDialog(ruleType, range),
            "Data Bar" => new DataBarRuleDialog(range),
            "Color Scale" => new ColorScaleRuleDialog(range),
            "Icon Set" => new IconSetRuleDialog(range),
            _ => new NewConditionalFormatRuleDialog(ruleType, range)
        };
}
