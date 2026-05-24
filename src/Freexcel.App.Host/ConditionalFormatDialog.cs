using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog : Window
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
    private CheckBox _dataBarGradientBox;
    private Button _dataBarColorButton;
    private TextBox _dataBarMinLengthBox;
    private TextBox _dataBarMaxLengthBox;
    private ComboBox _colorScaleMinTypeBox;
    private TextBox _colorScaleMinValueBox;
    private TextBox _colorScaleMinColorBox;
    private Button _colorScaleMinColorButton;
    private CheckBox _colorScaleUseThreeColorBox;
    private ComboBox _colorScaleMidTypeBox;
    private TextBox _colorScaleMidValueBox;
    private TextBox _colorScaleMidColorBox;
    private Button _colorScaleMidColorButton;
    private ComboBox _colorScaleMaxTypeBox;
    private TextBox _colorScaleMaxValueBox;
    private TextBox _colorScaleMaxColorBox;
    private Button _colorScaleMaxColorButton;
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
        _dataBarGradientBox = new CheckBox { Content = "_Gradient fill", Margin = new Thickness(0, 0, 0, 8), IsChecked = true };
        _dataBarColorButton = CreateDataBarColorButton();
        _dataBarMinLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarMaxLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 12) };
        _colorScaleMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
        _colorScaleMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _colorScaleMinColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(99, 190, 123)) };
        _colorScaleMinColorButton = CreateColorScaleColorButton(_colorScaleMinColorBox, "Choose minimum color");
        _colorScaleUseThreeColorBox = new CheckBox { Content = "Use _three-color scale", Margin = new Thickness(0, 0, 0, 8) };
        _colorScaleMidTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Percentile };
        _colorScaleMidValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "50" };
        _colorScaleMidColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(255, 235, 132)) };
        _colorScaleMidColorButton = CreateColorScaleColorButton(_colorScaleMidColorBox, "Choose midpoint color");
        _colorScaleUseThreeColorBox.Checked += (_, _) => UpdateColorScaleMidpointState();
        _colorScaleUseThreeColorBox.Unchecked += (_, _) => UpdateColorScaleMidpointState();
        _colorScaleMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
        _colorScaleMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _colorScaleMaxColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Text = FormatRgb(new RgbColor(248, 105, 107)) };
        _colorScaleMaxColorButton = CreateColorScaleColorButton(_colorScaleMaxColorBox, "Choose maximum color");
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
            inner.Children.Add(_dataBarGradientBox);
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
            inner.Children.Add(CreateAccessLabel("_Minimum color:", _colorScaleMinColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMinColorBox, _colorScaleMinColorButton));
            inner.Children.Add(_colorScaleUseThreeColorBox);
            inner.Children.Add(CreateAccessLabel("_Midpoint type:", _colorScaleMidTypeBox));
            inner.Children.Add(_colorScaleMidTypeBox);
            inner.Children.Add(CreateAccessLabel("Midpoint _value:", _colorScaleMidValueBox));
            inner.Children.Add(_colorScaleMidValueBox);
            inner.Children.Add(CreateAccessLabel("Midpoint _color:", _colorScaleMidColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMidColorBox, _colorScaleMidColorButton));
            inner.Children.Add(CreateAccessLabel("Ma_ximum type:", _colorScaleMaxTypeBox));
            inner.Children.Add(_colorScaleMaxTypeBox);
            inner.Children.Add(CreateAccessLabel("Maximum _value:", _colorScaleMaxValueBox));
            inner.Children.Add(_colorScaleMaxValueBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum color:", _colorScaleMaxColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMaxColorBox, _colorScaleMaxColorButton));

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
            inner.Children.Add(isDataBar ? CreateDataBarColorEditor(_colorBox, _dataBarColorButton) : _colorBox);
            if (!isDataBar)
                inner.Children.Add(formatButton);
        }

        if (isIconSet || isColorScale || isDataBar)
            AddVisualPreview(inner, ruleType);

        inner.Children.Add(btnRow);
        Content = BuildExcelRuleShell(ruleType, inner);
        UpdateColorScaleMidpointState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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
                _dataBarGradientBox.IsChecked = existingRule.DataBarGradient;
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

    private void FocusInitialKeyboardTarget()
    {
        Control target =
            _formulaBox is { IsVisible: true } formulaBox ? formulaBox :
            _value1Box.IsVisible ? _value1Box :
            _topBottomRankBox.IsVisible ? _topBottomRankBox :
            _dataBarMinTypeBox.IsVisible ? _dataBarMinTypeBox :
            _colorScaleMinTypeBox.IsVisible ? _colorScaleMinTypeBox :
            _iconSetStyleBox.IsVisible ? _iconSetStyleBox :
            _dateOccurringPeriodBox.IsVisible ? _dateOccurringPeriodBox :
            _duplicateValuesKindBox.IsVisible ? _duplicateValuesKindBox :
            _colorBox;

        target.Focus();
        if (target is TextBox textBox)
            textBox.SelectAll();
        Keyboard.Focus(target);
    }

    private static Label CreateAccessLabel(string content, Control target) =>
        new() { Content = content, Target = target, Padding = new Thickness(0) };

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
            _dataBarGradientBox = new CheckBox { Content = "_Gradient fill", Margin = new Thickness(0, 0, 0, 8), IsChecked = true };
            _dataBarColorButton = CreateDataBarColorButton();
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
            inner.Children.Add(_dataBarGradientBox);
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
            _colorScaleMinColorButton = CreateColorScaleColorButton(_colorScaleMinColorBox, "Choose minimum color");
            _colorScaleUseThreeColorBox = new CheckBox { Content = "Use _three-color scale", Margin = new Thickness(0, 0, 0, 8) };
            _colorScaleUseThreeColorBox.Checked += (_, _) => UpdateColorScaleMidpointState();
            _colorScaleUseThreeColorBox.Unchecked += (_, _) => UpdateColorScaleMidpointState();
            _colorScaleMidTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Percentile };
            _colorScaleMidValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "50" };
            _colorScaleMidColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(255, 235, 132)) };
            _colorScaleMidColorButton = CreateColorScaleColorButton(_colorScaleMidColorBox, "Choose midpoint color");
            _colorScaleMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
            _colorScaleMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _colorScaleMaxColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Text = FormatRgb(new RgbColor(248, 105, 107)) };
            _colorScaleMaxColorButton = CreateColorScaleColorButton(_colorScaleMaxColorBox, "Choose maximum color");
            inner.Children.Add(CreateAccessLabel("_Minimum type:", _colorScaleMinTypeBox));
            inner.Children.Add(_colorScaleMinTypeBox);
            inner.Children.Add(CreateAccessLabel("Minimum _value:", _colorScaleMinValueBox));
            inner.Children.Add(_colorScaleMinValueBox);
            inner.Children.Add(CreateAccessLabel("_Minimum color:", _colorScaleMinColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMinColorBox, _colorScaleMinColorButton));
            inner.Children.Add(_colorScaleUseThreeColorBox);
            inner.Children.Add(CreateAccessLabel("_Midpoint type:", _colorScaleMidTypeBox));
            inner.Children.Add(_colorScaleMidTypeBox);
            inner.Children.Add(CreateAccessLabel("Midpoint _value:", _colorScaleMidValueBox));
            inner.Children.Add(_colorScaleMidValueBox);
            inner.Children.Add(CreateAccessLabel("Midpoint _color:", _colorScaleMidColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMidColorBox, _colorScaleMidColorButton));
            inner.Children.Add(CreateAccessLabel("Ma_ximum type:", _colorScaleMaxTypeBox));
            inner.Children.Add(_colorScaleMaxTypeBox);
            inner.Children.Add(CreateAccessLabel("Maximum _value:", _colorScaleMaxValueBox));
            inner.Children.Add(_colorScaleMaxValueBox);
            inner.Children.Add(CreateAccessLabel("Ma_ximum color:", _colorScaleMaxColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMaxColorBox, _colorScaleMaxColorButton));
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
            inner.Children.Add(ruleType is "Data Bar" ? CreateDataBarColorEditor(_colorBox, _dataBarColorButton) : _colorBox);
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

}
