using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public class ConditionalFormatDialog : Window
{
    public ConditionalFormat? ResultRule { get; private set; }

    private string _ruleType;
    private readonly GridRange _range;
    private readonly Guid _existingId;
    private TextBox _value1Box;
    private TextBox _value2Box;
    private Label _value2Label;
    private ComboBox _colorBox;
    private TextBox? _formulaBox;
    private ComboBox _iconSetStyleBox;
    private CheckBox _iconSetShowValueBox;
    private CheckBox _iconSetReverseBox;
    private TextBox _topBottomRankBox;
    private ComboBox _dataBarMinTypeBox;
    private TextBox _dataBarMinValueBox;
    private ComboBox _dataBarMaxTypeBox;
    private TextBox _dataBarMaxValueBox;
    private CheckBox _dataBarShowValueBox;
    private TextBox _dataBarMinLengthBox;
    private TextBox _dataBarMaxLengthBox;
    private ComboBox _colorScaleMinTypeBox;
    private TextBox _colorScaleMinValueBox;
    private TextBox _colorScaleMinColorBox;
    private CheckBox _colorScaleUseThreeColorBox;
    private ComboBox _colorScaleMidTypeBox;
    private TextBox _colorScaleMidValueBox;
    private TextBox _colorScaleMidColorBox;
    private ComboBox _colorScaleMaxTypeBox;
    private TextBox _colorScaleMaxValueBox;
    private TextBox _colorScaleMaxColorBox;
    private ComboBox _dateOccurringPeriodBox;
    private ComboBox _duplicateValuesKindBox;
    private StackPanel? _descriptionHost;
    private CellStyle? _customFormatStyle;
    private ConditionalFormat? _existingRule;
    private StackPanel _iconSetThresholdPanel = new();
    private List<(ComboBox TypeBox, TextBox ValueBox)> _iconSetThresholdRows = [];

    private static readonly (string Label, Color FillColor, Color? FontColor, bool Bold)[] ColorOptions =
    [
        ("Light Red Fill with Dark Red Text", Color.FromRgb(255, 199, 206), Color.FromRgb(156, 0, 6), true),
        ("Yellow Fill with Dark Yellow Text", Color.FromRgb(255, 235, 132), Color.FromRgb(156, 101, 0), true),
        ("Green Fill with Dark Green Text", Color.FromRgb(198, 239, 206), Color.FromRgb(0, 97, 0), true),
        ("Light Red Fill",    Color.FromRgb(255, 199, 206), null, false),
        ("Yellow Fill",       Color.FromRgb(255, 235, 132), null, false),
        ("Green Fill",        Color.FromRgb(198, 239, 206), null, false),
        ("Light Blue Fill",   Color.FromRgb(189, 215, 238), null, false),
        ("Bold Red Text",     Color.FromRgb(255, 255, 255), Color.FromRgb(255, 0, 0), true),
        ("Bold Green Text",   Color.FromRgb(255, 255, 255), Color.FromRgb(0, 176, 80), true),
        ("Custom Format...",  Color.FromRgb(255, 255, 255), null, false),
    ];

    private static readonly string[] ExcelRuleShellTypes =
    [
        "Format all cells based on their values",
        "Format only cells that contain",
        "Format only top or bottom ranked values",
        "Format only values that are above or below average",
        "Format only unique or duplicate values",
        "Use a formula to determine which cells to format"
    ];

    private static readonly IReadOnlyList<string> IconSetStyles = ConditionalFormatIconSetPlanner.Styles;

    private static readonly (string Label, string Value)[] DateOccurringPeriods =
    [
        ("Yesterday", "yesterday"),
        ("Today", "today"),
        ("Tomorrow", "tomorrow"),
        ("Last 7 Days", "last7Days"),
        ("Last Week", "lastWeek"),
        ("This Week", "thisWeek"),
        ("Next Week", "nextWeek"),
        ("Last Month", "lastMonth"),
        ("This Month", "thisMonth"),
        ("Next Month", "nextMonth")
    ];

    /// <summary>Creates a new-rule dialog for the given rule type and range.</summary>
    public ConditionalFormatDialog(string ruleType, GridRange range)
    {
        _ruleType   = ruleType;
        _range      = range;
        _existingId = Guid.NewGuid();

        Title = $"Conditional Formatting — {ruleType}";
        Width = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;

        bool isFormula = ruleType is "Formula" or "Use a Formula";
        bool isIconSet = ruleType is "Icon Set";
        bool isDataBar = ruleType is "Data Bar";
        bool isColorScale = ruleType is "Color Scale";
        bool isDateOccurring = ruleType is "Date Occurring";
        bool isDuplicateValues = ruleType is "Duplicate Values";
        bool isBetween = ruleType is "Between";
        bool needsValue = ruleType is "Greater Than" or "Less Than" or "Equal To"
                                   or "Between" or "Text Contains";

        var inner = new StackPanel { Margin = new Thickness(16) };
        _iconSetStyleBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var style in IconSetStyles) _iconSetStyleBox.Items.Add(style);
        _iconSetStyleBox.SelectedIndex = 0;
        _iconSetShowValueBox = new CheckBox { Content = "_Show value", Margin = new Thickness(0, 0, 0, 6), IsChecked = true };
        _iconSetReverseBox = new CheckBox { Content = "_Reverse icon order", Margin = new Thickness(0, 0, 0, 12) };
        _topBottomRankBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "10" };
        _dataBarMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
        _dataBarMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
        _dataBarMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarShowValueBox = new CheckBox { Content = "_Show Bar Only", Margin = new Thickness(0, 0, 0, 8), IsChecked = false };
        _dataBarMinLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarMaxLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 12) };
        _colorScaleMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
        _colorScaleMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _colorScaleMinColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(99, 190, 123)) };
        _colorScaleUseThreeColorBox = new CheckBox { Content = "Use _three-color scale", Margin = new Thickness(0, 0, 0, 8) };
        _colorScaleMidTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Percentile };
        _colorScaleMidValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "50" };
        _colorScaleMidColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(255, 235, 132)) };
        _colorScaleUseThreeColorBox.Checked += (_, _) => UpdateColorScaleMidpointState();
        _colorScaleUseThreeColorBox.Unchecked += (_, _) => UpdateColorScaleMidpointState();
        _colorScaleMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
        _colorScaleMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _colorScaleMaxColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Text = FormatRgb(new RgbColor(248, 105, 107)) };
        _dateOccurringPeriodBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (label, _) in DateOccurringPeriods) _dateOccurringPeriodBox.Items.Add(label);
        _dateOccurringPeriodBox.SelectedItem = "Today";
        _duplicateValuesKindBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        _duplicateValuesKindBox.Items.Add("Duplicate");
        _duplicateValuesKindBox.Items.Add("Unique");
        _duplicateValuesKindBox.SelectedItem = "Duplicate";

        if (isFormula)
        {
            Height = 200;
            _formulaBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "=" };
            inner.Children.Add(CreateAccessLabel("_Formula:", _formulaBox));
            inner.Children.Add(_formulaBox);
            // placeholders needed by Ok_Click — never shown
            _value1Box  = new TextBox();
            _value2Box  = new TextBox();
            _value2Label = new Label();
        }
        else if (isDataBar)
        {
            Height = 430;
            inner.Children.Add(CreateAccessLabel("_Minimum type:", _dataBarMinTypeBox));
            inner.Children.Add(_dataBarMinTypeBox);
            inner.Children.Add(CreateAccessLabel("Minimum _value:", _dataBarMinValueBox));
            inner.Children.Add(_dataBarMinValueBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum type:", _dataBarMaxTypeBox));
            inner.Children.Add(_dataBarMaxTypeBox);
            inner.Children.Add(CreateAccessLabel("Maximum _value:", _dataBarMaxValueBox));
            inner.Children.Add(_dataBarMaxValueBox);
            inner.Children.Add(_dataBarShowValueBox);
            inner.Children.Add(CreateAccessLabel("_Minimum bar length (%):", _dataBarMinLengthBox));
            inner.Children.Add(_dataBarMinLengthBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum bar length (%):", _dataBarMaxLengthBox));
            inner.Children.Add(_dataBarMaxLengthBox);

            _value1Box  = new TextBox();
            _value2Box  = new TextBox();
            _value2Label = new Label();
        }
        else if (isColorScale)
        {
            Height = 520;
            inner.Children.Add(CreateAccessLabel("_Minimum type:", _colorScaleMinTypeBox));
            inner.Children.Add(_colorScaleMinTypeBox);
            inner.Children.Add(CreateAccessLabel("Minimum _value:", _colorScaleMinValueBox));
            inner.Children.Add(_colorScaleMinValueBox);
            inner.Children.Add(CreateAccessLabel("_Minimum color (R,G,B):", _colorScaleMinColorBox));
            inner.Children.Add(_colorScaleMinColorBox);
            inner.Children.Add(_colorScaleUseThreeColorBox);
            inner.Children.Add(CreateAccessLabel("_Midpoint type:", _colorScaleMidTypeBox));
            inner.Children.Add(_colorScaleMidTypeBox);
            inner.Children.Add(CreateAccessLabel("Midpoint _value:", _colorScaleMidValueBox));
            inner.Children.Add(_colorScaleMidValueBox);
            inner.Children.Add(CreateAccessLabel("Midpoint _color (R,G,B):", _colorScaleMidColorBox));
            inner.Children.Add(_colorScaleMidColorBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum type:", _colorScaleMaxTypeBox));
            inner.Children.Add(_colorScaleMaxTypeBox);
            inner.Children.Add(CreateAccessLabel("Maximum _value:", _colorScaleMaxValueBox));
            inner.Children.Add(_colorScaleMaxValueBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum color (R,G,B):", _colorScaleMaxColorBox));
            inner.Children.Add(_colorScaleMaxColorBox);

            _value1Box  = new TextBox();
            _value2Box  = new TextBox();
            _value2Label = new Label();
        }
        else if (isIconSet)
        {
            _iconSetStyleBox.SelectionChanged += (_, _) => BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string);
            _iconSetThresholdPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string ?? IconSetStyles[0]);

            inner.Children.Add(CreateAccessLabel("_Icon set:", _iconSetStyleBox));
            inner.Children.Add(_iconSetStyleBox);
            inner.Children.Add(_iconSetShowValueBox);
            inner.Children.Add(_iconSetReverseBox);
            inner.Children.Add(new TextBlock { Text = "Thresholds:", Margin = new Thickness(0, 4, 0, 2) });
            inner.Children.Add(_iconSetThresholdPanel);

            _value1Box  = new TextBox();
            _value2Box  = new TextBox();
            _value2Label = new Label();
        }
        else if (isDateOccurring)
        {
            Height = 220;
            inner.Children.Add(CreateAccessLabel("_Date period:", _dateOccurringPeriodBox));
            inner.Children.Add(_dateOccurringPeriodBox);

            _value1Box  = new TextBox();
            _value2Box  = new TextBox();
            _value2Label = new Label();
        }
        else if (isDuplicateValues)
        {
            Height = 220;
            inner.Children.Add(CreateAccessLabel("Format cells that _contain:", _duplicateValuesKindBox));
            inner.Children.Add(_duplicateValuesKindBox);

            _value1Box  = new TextBox();
            _value2Box  = new TextBox();
            _value2Label = new Label();
        }
        else
        {
            Height = needsValue ? (isBetween ? 260 : 220) : 180;

            _value1Box  = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            var lbl1 = new Label { Content = isBetween ? "_Minimum:" : "_Value:", Target = _value1Box, Padding = new Thickness(0) };
            _value2Box  = new TextBox { Margin = new Thickness(0, 4, 0, 8),
                Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed };
            _value2Label = new Label { Content = "Ma_ximum:", Target = _value2Box, Padding = new Thickness(0),
                Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed };

            if (needsValue)
            {
                inner.Children.Add(lbl1);
                inner.Children.Add(_value1Box);
                if (isBetween) { inner.Children.Add(_value2Label); inner.Children.Add(_value2Box); }
            }
            else if (ruleType is "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%")
            {
                Height = 220;
                _topBottomRankBox.Text = ruleType is "Top 10%" or "Bottom 10%" ? "10" : "10";
                inner.Children.Add(CreateAccessLabel(ruleType is "Top 10%" or "Bottom 10%" ? "_Percent:" : "_Rank:", _topBottomRankBox));
                inner.Children.Add(_topBottomRankBox);
            }
        }

        _colorBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        var colorLabel = new Label { Content = "_Format:", Target = _colorBox, Padding = new Thickness(0) };
        foreach (var (lbl, _, _, _) in ColorOptions) _colorBox.Items.Add(lbl);
        _colorBox.SelectedIndex = 0;
        var formatButton = new Button
        {
            Content = "Format...",
            Width = 84,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Choose a custom fill color for this conditional format"
        };
        formatButton.Click += FormatButton_Click;

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok     = new Button { Content = "_OK",     Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "_Cancel", Width = 80, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);

        if (!isIconSet && !isColorScale)
        {
            colorLabel.Content = isDataBar ? "_Bar color:" : "_Format:";
            inner.Children.Add(colorLabel);
            inner.Children.Add(_colorBox);
            if (!isDataBar)
                inner.Children.Add(formatButton);
        }

        if (isIconSet || isColorScale || isDataBar)
            AddVisualPreview(inner, ruleType);

        inner.Children.Add(btnRow);
        Content = BuildExcelRuleShell(ruleType, inner);
        UpdateColorScaleMidpointState();
    }

    /// <summary>Creates an edit dialog pre-populated with <paramref name="existingRule"/>.</summary>
    public ConditionalFormatDialog(ConditionalFormat existingRule)
        : this(ConditionalFormatDialogPlanner.RuleTypeLabel(existingRule), existingRule.AppliesTo)
    {
        Title = "Edit Formatting Rule";
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
                var thresholds = existingRule.IconSetThresholds.Count > 0 ? existingRule.IconSetThresholds : null;
                BuildIconSetThresholdPanel(style, thresholds);
            }
            else if (existingRule.RuleType == CfRuleType.DataBar)
            {
                _dataBarMinTypeBox.SelectedItem = existingRule.DataBarMinThresholdType;
                _dataBarMinValueBox.Text = existingRule.DataBarMinThresholdValue ?? "";
                _dataBarMaxTypeBox.SelectedItem = existingRule.DataBarMaxThresholdType;
                _dataBarMaxValueBox.Text = existingRule.DataBarMaxThresholdValue ?? "";
                _dataBarShowValueBox.IsChecked = !existingRule.DataBarShowValue;
                _dataBarMinLengthBox.Text = existingRule.DataBarMinLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
                _dataBarMaxLengthBox.Text = existingRule.DataBarMaxLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
            else if (existingRule.RuleType == CfRuleType.ColorScale)
            {
                _colorScaleMinTypeBox.SelectedItem = existingRule.MinThresholdType;
                _colorScaleMinValueBox.Text = existingRule.MinThresholdValue ?? "";
                _colorScaleMinColorBox.Text = FormatRgb(existingRule.MinColor);
                _colorScaleUseThreeColorBox.IsChecked = existingRule.UseThreeColorScale;
                _colorScaleMidTypeBox.SelectedItem = existingRule.MidThresholdType;
                _colorScaleMidValueBox.Text = existingRule.MidThresholdValue ?? "";
                _colorScaleMidColorBox.Text = FormatRgb(existingRule.MidColor);
                _colorScaleMaxTypeBox.SelectedItem = existingRule.MaxThresholdType;
                _colorScaleMaxValueBox.Text = existingRule.MaxThresholdValue ?? "";
                _colorScaleMaxColorBox.Text = FormatRgb(existingRule.MaxColor);
                UpdateColorScaleMidpointState();
            }
            else if (existingRule.RuleType is CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith)
            {
                _value1Box.Text = existingRule.TextRuleText ?? "";
            }
            else if (existingRule.RuleType == CfRuleType.DateOccurring)
            {
                _dateOccurringPeriodBox.SelectedItem = DatePeriodLabel(existingRule.DateOccurringPeriod);
            }
            else if (existingRule.RuleType is CfRuleType.DuplicateValues or CfRuleType.UniqueValues)
            {
                _duplicateValuesKindBox.SelectedItem = existingRule.RuleType == CfRuleType.UniqueValues ? "Unique" : "Duplicate";
            }
            else if (existingRule.RuleType == CfRuleType.Top10)
            {
                _topBottomRankBox.Text = existingRule.TopBottomRank.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // Pre-select the closest color option from FormatIfTrue.FillColor
        if (existingRule.RuleType == CfRuleType.DataBar)
        {
            SelectColor(new CellColor(
                existingRule.DataBarColor.R,
                existingRule.DataBarColor.G,
                existingRule.DataBarColor.B));
        }
        else if (existingRule.FormatIfTrue?.FillColor is { } fc)
        {
            SelectColor(fc);
        }
    }

    private void SelectColor(CellColor color)
    {
        var wc = Color.FromRgb(color.R, color.G, color.B);
        for (var i = 0; i < ColorOptions.Length; i++)
        {
            if (ColorOptions[i].FillColor == wc)
            {
                _colorBox.SelectedIndex = i;
                break;
            }
        }
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = SelectedColorPreset();
        var initial = _customFormatStyle?.FillColor
            ?? new CellColor(preset.FillColor.R, preset.FillColor.G, preset.FillColor.B);
        var dialog = new ColorPickerDialog(initial) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedColor is not { } color)
            return;

        _customFormatStyle = new CellStyle { FillColor = color };
        _colorBox.SelectedItem = "Custom Format...";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var cf = _existingRule is not null
            ? ConditionalFormatDialogPlanner.CloneRule(_existingRule)
            : new ConditionalFormat { Id = _existingId, AppliesTo = _range };
        cf.AppliesTo = _range;
        var selectedFormat = SelectedColorPreset();
        var fillColor = selectedFormat.FillColor;

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
                "Text Contains" => CfRuleType.ContainsText,
                "Date Occurring" => CfRuleType.DateOccurring,
                "Duplicate Values" => DuplicateValuesRuleType(_duplicateValuesKindBox.SelectedItem as string),
                "Blanks" => CfRuleType.Blanks,
                "No Blanks" => CfRuleType.NoBlanks,
                "Errors" => CfRuleType.Errors,
                "No Errors" => CfRuleType.NoErrors,
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
                cf.IconSetThresholds.Clear();
                if (_iconSetThresholdRows.Count > 0)
                {
                    foreach (var (typeBox, valueBox) in _iconSetThresholdRows)
                    {
                        var type = typeBox.SelectedItem is CfThresholdType t ? t : CfThresholdType.Percent;
                        cf.IconSetThresholds.Add(new CfThresholdModel(type, BlankToNull(valueBox.Text)));
                    }
                }
                else
                {
                    cf.IconSetThresholds.AddRange(ConditionalFormatIconSetPlanner.CreateThresholds(cf.IconSetStyle));
                }
            }
            else if (cf.RuleType == CfRuleType.DataBar)
            {
                cf.DataBarColor = new RgbColor(fillColor.R, fillColor.G, fillColor.B);
                cf.DataBarMinThresholdType = SelectedThresholdType(_dataBarMinTypeBox, CfThresholdType.Min);
                cf.DataBarMinThresholdValue = BlankToNull(_dataBarMinValueBox.Text);
                cf.DataBarMaxThresholdType = SelectedThresholdType(_dataBarMaxTypeBox, CfThresholdType.Max);
                cf.DataBarMaxThresholdValue = BlankToNull(_dataBarMaxValueBox.Text);
                cf.DataBarShowValue = _dataBarShowValueBox.IsChecked != true;
                cf.DataBarMinLength = ParseOptionalPercent(_dataBarMinLengthBox.Text);
                cf.DataBarMaxLength = ParseOptionalPercent(_dataBarMaxLengthBox.Text);
            }
            else if (cf.RuleType == CfRuleType.ColorScale)
            {
                cf.MinThresholdType = SelectedThresholdType(_colorScaleMinTypeBox, CfThresholdType.Min);
                cf.MinThresholdValue = BlankToNull(_colorScaleMinValueBox.Text);
                cf.MinColor = ParseRgbOrFallback(_colorScaleMinColorBox.Text, cf.MinColor);
                cf.UseThreeColorScale = _colorScaleUseThreeColorBox.IsChecked == true;
                cf.MidThresholdType = SelectedThresholdType(_colorScaleMidTypeBox, CfThresholdType.Percentile);
                cf.MidThresholdValue = BlankToNull(_colorScaleMidValueBox.Text);
                cf.MidColor = ParseRgbOrFallback(_colorScaleMidColorBox.Text, cf.MidColor);
                cf.MaxThresholdType = SelectedThresholdType(_colorScaleMaxTypeBox, CfThresholdType.Max);
                cf.MaxThresholdValue = BlankToNull(_colorScaleMaxValueBox.Text);
                cf.MaxColor = ParseRgbOrFallback(_colorScaleMaxColorBox.Text, cf.MaxColor);
            }
            else if (cf.RuleType is CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith)
            {
                cf.TextRuleText = _value1Box.Text.Trim();
            }
            else if (cf.RuleType == CfRuleType.DateOccurring)
            {
                cf.DateOccurringPeriod = DatePeriodValue(_dateOccurringPeriodBox.SelectedItem as string);
            }

            cf.AboveAverage = _ruleType is not ("Below Average" or "Bottom 10 Items" or "Bottom 10%");
            cf.TopBottomPercent = _ruleType is "Top 10%" or "Bottom 10%";
            if (cf.RuleType == CfRuleType.Top10)
                cf.TopBottomRank = ParseTopBottomRank(_topBottomRankBox.Text);
        }

        if (cf.RuleType is not (CfRuleType.IconSet or CfRuleType.DataBar or CfRuleType.ColorScale))
        {
            cf.FormatIfTrue = BuildSelectedCellStyle();
        }
        else
        {
            cf.FormatIfTrue = null;
        }

        ResultRule = cf;
        DialogResult = true;
    }

    private static CfThresholdType SelectedThresholdType(ComboBox comboBox, CfThresholdType fallback) =>
        comboBox.SelectedItem is CfThresholdType selected ? selected : fallback;

    private (string Label, Color FillColor, Color? FontColor, bool Bold) SelectedColorPreset()
    {
        var index = _colorBox.SelectedIndex < 0 ? 0 : _colorBox.SelectedIndex;
        return ColorOptions[index];
    }

    private CellStyle BuildSelectedCellStyle()
    {
        if (_colorBox.SelectedItem as string == "Custom Format..." && _customFormatStyle is not null)
            return _customFormatStyle.Clone();

        var selected = SelectedColorPreset();
        var style = new CellStyle
        {
            FillColor = new CellColor(selected.FillColor.R, selected.FillColor.G, selected.FillColor.B),
            Bold = selected.Bold
        };
        if (selected.FontColor is { } fontColor)
            style.FontColor = new CellColor(fontColor.R, fontColor.G, fontColor.B);

        return style;
    }

    private static Label CreateAccessLabel(string content, Control target) =>
        new() { Content = content, Target = target, Padding = new Thickness(0) };

    private static string? BlankToNull(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static int? ParseOptionalPercent(string text)
    {
        if (!int.TryParse(text.Trim(), out var value))
            return null;

        return Math.Clamp(value, 0, 100);
    }

    private static int ParseTopBottomRank(string text) =>
        int.TryParse(text.Trim(), out var value)
            ? Math.Clamp(value, 1, 1000)
            : 10;

    private void UpdateColorScaleMidpointState()
    {
        var enabled = _colorScaleUseThreeColorBox.IsChecked == true;
        _colorScaleMidTypeBox.IsEnabled = enabled;
        _colorScaleMidValueBox.IsEnabled = enabled;
        _colorScaleMidColorBox.IsEnabled = enabled;
    }

    private static string FormatRgb(RgbColor color) =>
        $"{color.R},{color.G},{color.B}";

    private static RgbColor ParseRgbOrFallback(string text, RgbColor fallback) =>
        ColorInputParser.TryParseRgbColorText(text, out var color)
            ? new RgbColor(color.R, color.G, color.B)
            : fallback;

    private static CfRuleType DuplicateValuesRuleType(string? label) =>
        string.Equals(label, "Unique", StringComparison.OrdinalIgnoreCase)
            ? CfRuleType.UniqueValues
            : CfRuleType.DuplicateValues;

    private static string DatePeriodValue(string? label) =>
        DateOccurringPeriods.FirstOrDefault(period => period.Label == label) is var period
            && period.Label is not null
                ? period.Value
                : "today";

    private static string DatePeriodLabel(string? value) =>
        DateOccurringPeriods.FirstOrDefault(period => period.Value == value) is var period
            && period.Label is not null
                ? period.Label
                : "Today";

    private Grid BuildExcelRuleShell(string ruleType, UIElement descriptionContent)
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock
        {
            Text = "Select a Rule Type:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var ruleTypeList = new ListBox
        {
            MinHeight = 182,
            ItemsSource = ExcelRuleShellTypes,
            SelectedItem = RuleTypeShellLabel(ruleType)
        };
        ruleTypeList.SelectionChanged += RuleTypeList_SelectionChanged;
        left.Children.Add(ruleTypeList);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var right = new StackPanel();
        _descriptionHost = right;
        right.Children.Add(new TextBlock
        {
            Text = "Edit the Rule Description:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        right.Children.Add(descriptionContent);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        return root;
    }

    private void RuleTypeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not string shellLabel)
            return;

        var newRuleType = DefaultRuleTypeForShellLabel(shellLabel);
        if (newRuleType == _ruleType || _descriptionHost is null)
            return;

        RefreshRuleDescription(newRuleType);
    }

    private void RefreshRuleDescription(string ruleType)
    {
        _ruleType = ruleType;
        _customFormatStyle = null;

        var inner = new StackPanel { Margin = new Thickness(16) };
        var needsValue = ruleType is "Greater Than" or "Less Than" or "Equal To" or "Between" or "Text Contains";
        var isBetween = ruleType is "Between";
        var isFormula = ruleType is "Formula" or "Use a Formula";
        var isIconSet = ruleType is "Icon Set";
        var isDataBar = ruleType is "Data Bar";
        var isColorScale = ruleType is "Color Scale";
        var isDuplicateValues = ruleType is "Duplicate Values";
        var isDateOccurring = ruleType is "Date Occurring";

        if (isFormula)
        {
            Height = 200;
            _formulaBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "=" };
            inner.Children.Add(CreateAccessLabel("_Formula:", _formulaBox));
            inner.Children.Add(_formulaBox);
            _value1Box = new TextBox();
            _value2Box = new TextBox();
            _value2Label = new Label();
        }
        else if (isDataBar)
        {
            Height = 430;
            _dataBarMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
            _dataBarMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
            _dataBarMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarShowValueBox = new CheckBox { Content = "_Show Bar Only", Margin = new Thickness(0, 0, 0, 8), IsChecked = false };
            _dataBarMinLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarMaxLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 12) };
            inner.Children.Add(CreateAccessLabel("_Minimum type:", _dataBarMinTypeBox));
            inner.Children.Add(_dataBarMinTypeBox);
            inner.Children.Add(CreateAccessLabel("Minimum _value:", _dataBarMinValueBox));
            inner.Children.Add(_dataBarMinValueBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum type:", _dataBarMaxTypeBox));
            inner.Children.Add(_dataBarMaxTypeBox);
            inner.Children.Add(CreateAccessLabel("Maximum _value:", _dataBarMaxValueBox));
            inner.Children.Add(_dataBarMaxValueBox);
            inner.Children.Add(_dataBarShowValueBox);
            inner.Children.Add(CreateAccessLabel("_Minimum bar length (%):", _dataBarMinLengthBox));
            inner.Children.Add(_dataBarMinLengthBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum bar length (%):", _dataBarMaxLengthBox));
            inner.Children.Add(_dataBarMaxLengthBox);
            _value1Box = new TextBox();
            _value2Box = new TextBox();
            _value2Label = new Label();
        }
        else if (isColorScale)
        {
            Height = 520;
            _colorScaleMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
            _colorScaleMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _colorScaleMinColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(99, 190, 123)) };
            _colorScaleUseThreeColorBox = new CheckBox { Content = "Use _three-color scale", Margin = new Thickness(0, 0, 0, 8) };
            _colorScaleUseThreeColorBox.Checked += (_, _) => UpdateColorScaleMidpointState();
            _colorScaleUseThreeColorBox.Unchecked += (_, _) => UpdateColorScaleMidpointState();
            _colorScaleMidTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Percentile };
            _colorScaleMidValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "50" };
            _colorScaleMidColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(255, 235, 132)) };
            _colorScaleMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
            _colorScaleMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _colorScaleMaxColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Text = FormatRgb(new RgbColor(248, 105, 107)) };
            inner.Children.Add(CreateAccessLabel("_Minimum type:", _colorScaleMinTypeBox));
            inner.Children.Add(_colorScaleMinTypeBox);
            inner.Children.Add(CreateAccessLabel("Minimum _value:", _colorScaleMinValueBox));
            inner.Children.Add(_colorScaleMinValueBox);
            inner.Children.Add(CreateAccessLabel("_Minimum color (R,G,B):", _colorScaleMinColorBox));
            inner.Children.Add(_colorScaleMinColorBox);
            inner.Children.Add(_colorScaleUseThreeColorBox);
            inner.Children.Add(CreateAccessLabel("_Midpoint type:", _colorScaleMidTypeBox));
            inner.Children.Add(_colorScaleMidTypeBox);
            inner.Children.Add(CreateAccessLabel("Midpoint _value:", _colorScaleMidValueBox));
            inner.Children.Add(_colorScaleMidValueBox);
            inner.Children.Add(CreateAccessLabel("Midpoint _color (R,G,B):", _colorScaleMidColorBox));
            inner.Children.Add(_colorScaleMidColorBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum type:", _colorScaleMaxTypeBox));
            inner.Children.Add(_colorScaleMaxTypeBox);
            inner.Children.Add(CreateAccessLabel("Maximum _value:", _colorScaleMaxValueBox));
            inner.Children.Add(_colorScaleMaxValueBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum color (R,G,B):", _colorScaleMaxColorBox));
            inner.Children.Add(_colorScaleMaxColorBox);
            _value1Box = new TextBox();
            _value2Box = new TextBox();
            _value2Label = new Label();
            UpdateColorScaleMidpointState();
        }
        else if (isIconSet)
        {
            _iconSetStyleBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8) };
            foreach (var style in IconSetStyles) _iconSetStyleBox.Items.Add(style);
            _iconSetStyleBox.SelectedIndex = 0;
            _iconSetStyleBox.SelectionChanged += (_, _) => BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string);
            _iconSetShowValueBox = new CheckBox { Content = "_Show value", Margin = new Thickness(0, 0, 0, 6), IsChecked = true };
            _iconSetReverseBox = new CheckBox { Content = "_Reverse icon order", Margin = new Thickness(0, 0, 0, 12) };
            _iconSetThresholdPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string ?? IconSetStyles[0]);
            inner.Children.Add(CreateAccessLabel("_Icon set:", _iconSetStyleBox));
            inner.Children.Add(_iconSetStyleBox);
            inner.Children.Add(_iconSetShowValueBox);
            inner.Children.Add(_iconSetReverseBox);
            inner.Children.Add(new TextBlock { Text = "Thresholds:", Margin = new Thickness(0, 4, 0, 2) });
            inner.Children.Add(_iconSetThresholdPanel);
            _value1Box = new TextBox();
            _value2Box = new TextBox();
            _value2Label = new Label();
        }
        else if (isDuplicateValues)
        {
            Height = 220;
            _duplicateValuesKindBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
            _duplicateValuesKindBox.Items.Add("Duplicate");
            _duplicateValuesKindBox.Items.Add("Unique");
            _duplicateValuesKindBox.SelectedItem = "Duplicate";
            inner.Children.Add(CreateAccessLabel("Format cells that _contain:", _duplicateValuesKindBox));
            inner.Children.Add(_duplicateValuesKindBox);
            _value1Box = new TextBox();
            _value2Box = new TextBox();
            _value2Label = new Label();
        }
        else if (isDateOccurring)
        {
            Height = 220;
            _dateOccurringPeriodBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
            foreach (var (label, _) in DateOccurringPeriods) _dateOccurringPeriodBox.Items.Add(label);
            _dateOccurringPeriodBox.SelectedItem = "Today";
            inner.Children.Add(CreateAccessLabel("_Date period:", _dateOccurringPeriodBox));
            inner.Children.Add(_dateOccurringPeriodBox);
            _value1Box = new TextBox();
            _value2Box = new TextBox();
            _value2Label = new Label();
        }
        else
        {
            Height = needsValue ? (isBetween ? 260 : 220) : 180;
            _value1Box = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            var lbl1 = new Label { Content = isBetween ? "_Minimum:" : "_Value:", Target = _value1Box, Padding = new Thickness(0) };
            _value2Box = new TextBox
            {
                Margin = new Thickness(0, 4, 0, 8),
                Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed
            };
            _value2Label = new Label
            {
                Content = "Ma_ximum:",
                Target = _value2Box,
                Padding = new Thickness(0),
                Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed
            };

            if (needsValue)
            {
                inner.Children.Add(lbl1);
                inner.Children.Add(_value1Box);
                if (isBetween)
                {
                    inner.Children.Add(_value2Label);
                    inner.Children.Add(_value2Box);
                }
            }
            else if (ruleType is "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%")
            {
                Height = 220;
                _topBottomRankBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "10" };
                inner.Children.Add(CreateAccessLabel(ruleType is "Top 10%" or "Bottom 10%" ? "_Percent:" : "_Rank:", _topBottomRankBox));
                inner.Children.Add(_topBottomRankBox);
            }
        }

        _colorBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (label, _, _, _) in ColorOptions) _colorBox.Items.Add(label);
        _colorBox.SelectedIndex = 0;
        var formatButton = new Button
        {
            Content = "Format...",
            Width = 84,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Choose a custom fill color for this conditional format"
        };
        formatButton.Click += FormatButton_Click;

        if (ruleType is not ("Icon Set" or "Color Scale"))
        {
            inner.Children.Add(new Label { Content = ruleType is "Data Bar" ? "_Bar color:" : "_Format with:", Target = _colorBox, Padding = new Thickness(0) });
            inner.Children.Add(_colorBox);
            if (ruleType is not "Data Bar")
                inner.Children.Add(formatButton);
        }
        if (isIconSet || isColorScale || isDataBar)
            AddVisualPreview(inner, ruleType);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var ok = new Button { Content = "_OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "_Cancel", Width = 80, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        inner.Children.Add(btnRow);

        if (_descriptionHost is null)
            return;

        _descriptionHost.Children.RemoveRange(1, _descriptionHost.Children.Count - 1);
        _descriptionHost.Children.Add(inner);
    }

    private static string DefaultRuleTypeForShellLabel(string shellLabel) =>
        shellLabel == ExcelRuleShellTypes[0] ? "Data Bar" :
        shellLabel == ExcelRuleShellTypes[2] ? "Top 10 Items" :
        shellLabel == ExcelRuleShellTypes[3] ? "Above Average" :
        shellLabel == ExcelRuleShellTypes[4] ? "Duplicate Values" :
        shellLabel == ExcelRuleShellTypes[5] ? "Formula" :
        "Greater Than";

    private static string RuleTypeShellLabel(string ruleType) => ruleType switch
    {
        "Data Bar" or "Color Scale" or "Icon Set" => ExcelRuleShellTypes[0],
        "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%" => ExcelRuleShellTypes[2],
        "Above Average" or "Below Average" => ExcelRuleShellTypes[3],
        "Duplicate Values" => ExcelRuleShellTypes[4],
        "Formula" or "Use a Formula" => ExcelRuleShellTypes[5],
        _ => ExcelRuleShellTypes[1]
    };

    private void BuildIconSetThresholdPanel(string? style, IReadOnlyList<CfThresholdModel>? existing = null)
    {
        _iconSetThresholdPanel.Children.Clear();
        _iconSetThresholdRows.Clear();

        var count = ConditionalFormatIconSetPlanner.GetIconCount(style);
        var defaults = ConditionalFormatIconSetPlanner.CreateThresholds(style);

        for (var i = 0; i < count; i++)
        {
            var threshold = existing is not null && i < existing.Count ? existing[i] : defaults[i];

            var typeBox = new ComboBox
            {
                Width = 100,
                Margin = new Thickness(6, 0, 6, 0),
                ItemsSource = Enum.GetValues<CfThresholdType>(),
                SelectedItem = threshold.Type
            };
            if (typeBox.SelectedIndex < 0) typeBox.SelectedIndex = 0;

            var valueBox = new TextBox
            {
                Width = 80,
                Text = threshold.Value ?? "",
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Padding = new Thickness(2)
            };

            _iconSetThresholdPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2),
                Children =
                {
                    new TextBlock { Text = $"Icon {i + 1}  when  ≥", Width = 110, VerticalAlignment = System.Windows.VerticalAlignment.Center },
                    typeBox,
                    valueBox
                }
            });
            _iconSetThresholdRows.Add((typeBox, valueBox));
        }
    }

    private static void AddVisualPreview(Panel panel, string ruleType)
    {
        panel.Children.Add(new TextBlock
        {
            Text = "Preview:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 6)
        });

        panel.Children.Add(ruleType switch
        {
            "Color Scale" => BuildColorScalePreview(),
            "Icon Set" => BuildIconSetPreview(),
            _ => BuildDataBarPreview()
        });
    }

    private static Border BuildDataBarPreview() =>
        new()
        {
            Name = "DataBarPreview",
            Height = 28,
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                Children =
                {
                    new Border
                    {
                        Width = 150,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                        Background = new LinearGradientBrush(Color.FromRgb(91, 155, 213), Color.FromRgb(189, 215, 238), 0)
                    },
                    new TextBlock
                    {
                        Text = "123",
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    }
                }
            }
        };

    private static Border BuildColorScalePreview() =>
        new()
        {
            Name = "ColorScalePreview",
            Height = 28,
            Margin = new Thickness(0, 0, 0, 12),
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(99, 190, 123), 0),
                    new(Color.FromRgb(255, 235, 132), 0.5),
                    new(Color.FromRgb(248, 105, 107), 1)
                })
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            }
        };

    private static StackPanel BuildIconSetPreview() =>
        new()
        {
            Name = "IconSetPreview",
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock { Text = "\u25b2", Foreground = Brushes.Green, FontSize = 18, Margin = new Thickness(0, 0, 10, 0) },
                new TextBlock { Text = "\u25b6", Foreground = Brushes.Goldenrod, FontSize = 18, Margin = new Thickness(0, 0, 10, 0) },
                new TextBlock { Text = "\u25bc", Foreground = Brushes.Red, FontSize = 18 }
            }
        };
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
            "Greater Than" or "Less Than" or "Equal To" or "Between" or "Text Contains" or "Date Occurring" or "Duplicate Values" or
            "Blanks" or "No Blanks" or "Errors" or "No Errors" =>
                new HighlightCellsRuleDialog(ruleType, range),
            "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%" or "Above Average" or "Below Average" =>
                new TopBottomRuleDialog(ruleType, range),
            "Data Bar" => new DataBarRuleDialog(range),
            "Color Scale" => new ColorScaleRuleDialog(range),
            "Icon Set" => new IconSetRuleDialog(range),
            _ => new NewConditionalFormatRuleDialog(ruleType, range)
        };
}
