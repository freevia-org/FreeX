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
    private readonly Guid _existingId;
    private readonly TextBox _value1Box;
    private readonly TextBox _value2Box;
    private readonly Label _value2Label;
    private readonly ComboBox _colorBox;
    private readonly TextBox? _formulaBox;

    private static readonly (string Label, Color Color)[] ColorOptions =
    [
        ("Light Red Fill",    Color.FromRgb(255, 199, 206)),
        ("Yellow Fill",       Color.FromRgb(255, 235, 132)),
        ("Green Fill",        Color.FromRgb(198, 239, 206)),
        ("Light Blue Fill",   Color.FromRgb(189, 215, 238)),
        ("Bold Red Text",     Color.FromRgb(255, 0, 0)),
        ("Bold Green Text",   Color.FromRgb(0, 176, 80)),
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
        bool isBetween = ruleType is "Between";
        bool needsValue = ruleType is "Greater Than" or "Less Than" or "Equal To"
                                   or "Between" or "Text Contains";

        var inner = new StackPanel { Margin = new Thickness(16) };

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

        inner.Children.Add(colorLabel);
        inner.Children.Add(_colorBox);
        inner.Children.Add(btnRow);
        Content = inner;
    }

    /// <summary>Creates an edit dialog pre-populated with <paramref name="existingRule"/>.</summary>
    public ConditionalFormatDialog(ConditionalFormat existingRule)
        : this(RuleTypeLabel(existingRule), existingRule.AppliesTo)
    {
        _existingId = existingRule.Id;   // preserve Id so the command recognises it as an update

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
        var cf = new ConditionalFormat { Id = _existingId, AppliesTo = _range };
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
        }

        cf.FormatIfTrue = new CellStyle
        {
            FillColor = new CellColor(fillColor.R, fillColor.G, fillColor.B)
        };

        ResultRule = cf;
        DialogResult = true;
    }

    /// <summary>Returns the human-readable rule type label for a <see cref="ConditionalFormat"/>.</summary>
    private static string RuleTypeLabel(ConditionalFormat cf) => cf.RuleType switch
    {
        CfRuleType.Formula     => "Formula",
        CfRuleType.DataBar     => "Data Bar",
        CfRuleType.ColorScale  => "Color Scale",
        CfRuleType.AboveAverage => cf.AboveAverage ? "Above Average" : "Below Average",
        CfRuleType.Top10       => cf.AboveAverage ? "Top 10 Items" : "Bottom 10 Items",
        CfRuleType.CellValue   => cf.Operator switch
        {
            CfOperator.GreaterThan => "Greater Than",
            CfOperator.LessThan    => "Less Than",
            CfOperator.Equal       => "Equal To",
            CfOperator.Between     => "Between",
            _                      => "Greater Than"
        },
        _ => "Greater Than"
    };
}
