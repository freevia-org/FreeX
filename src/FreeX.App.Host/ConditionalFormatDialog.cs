using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class ConditionalFormatDialog : Window
{
    public ConditionalFormat? ResultRule { get; private set; }

    private string _ruleType;
    private readonly GridRange _range;
    private readonly Guid _existingId;
    private TextBox _value1Box = new();
    private TextBox _value2Box = new();
    private Label _value2Label = new();
    private ComboBox _colorBox;
    private TextBox? _formulaBox;
    private ComboBox _conditionKindBox = new();
    private ComboBox _cellValueOperatorBox = new();
    private ComboBox _specificTextOperatorBox = new();
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
    private CheckBox _dataBarBorderBox;
    private ComboBox _dataBarAxisPositionBox;
    private TextBox _dataBarAxisColorBox;
    private Button _dataBarAxisColorButton;
    private TextBox _dataBarNegativeFillColorBox;
    private Button _dataBarNegativeFillColorButton;
    private TextBox _dataBarNegativeBorderColorBox;
    private Button _dataBarNegativeBorderColorButton;
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
    private List<(ComboBox TypeBox, TextBox ValueBox, ComboBox? OverrideBox)> _iconSetThresholdRows = [];
    private ComboBox? _formatStyleBox;
    private bool _ignoreFormatStyleChange;

    /// <summary>Creates a new-rule dialog for the given rule type and range.</summary>
    public ConditionalFormatDialog(string ruleType, GridRange range)
    {
        _ruleType   = ruleType;
        _range      = range;
        _existingId = Guid.NewGuid();

        Title = UiText.Format("ConditionalFormatDialog_TitleFormat", RuleTypeDisplayName(ruleType));
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
        var inner = new StackPanel { Margin = new Thickness(16) };
        _iconSetStyleBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var style in IconSetStyles) _iconSetStyleBox.Items.Add(style);
        _iconSetStyleBox.SelectedIndex = 0;
        _iconSetShowValueBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_ShowValue"), Margin = new Thickness(0, 0, 0, 6), IsChecked = true };
        _iconSetReverseBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_ReverseIconOrder"), Margin = new Thickness(0, 0, 0, 12) };
        _topBottomRankBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "10" };
        _dataBarMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
        _dataBarMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
        _dataBarMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarShowValueBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_ShowBarOnly"), Margin = new Thickness(0, 0, 0, 8), IsChecked = false };
        _dataBarGradientBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_GradientFill"), Margin = new Thickness(0, 0, 0, 8), IsChecked = true };
        _dataBarColorButton = CreateDataBarColorButton();
        _dataBarMinLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarMaxLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarBorderBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_ShowBorder"), Margin = new Thickness(0, 0, 0, 6) };
        _dataBarAxisPositionBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var p in DataBarAxisPositionLabels()) _dataBarAxisPositionBox.Items.Add(p);
        _dataBarAxisPositionBox.SelectedItem = UiText.Get("ConditionalFormatDialog_AxisPosition_Automatic");
        _dataBarAxisColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarAxisColorButton = CreateDataBarOptionalColorButton(_dataBarAxisColorBox, UiText.Get("ConditionalFormatDialog_ChooseAxisColorToolTip"));
        _dataBarNegativeFillColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _dataBarNegativeFillColorButton = CreateDataBarOptionalColorButton(_dataBarNegativeFillColorBox, UiText.Get("ConditionalFormatDialog_ChooseNegativeBarColorToolTip"));
        _dataBarNegativeBorderColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 12) };
        _dataBarNegativeBorderColorButton = CreateDataBarOptionalColorButton(_dataBarNegativeBorderColorBox, UiText.Get("ConditionalFormatDialog_ChooseNegativeBarBorderColorToolTip"));
        _colorScaleMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
        _colorScaleMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _colorScaleMinColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(99, 190, 123)) };
        _colorScaleMinColorButton = CreateColorScaleColorButton(_colorScaleMinColorBox, UiText.Get("ConditionalFormatDialog_ChooseMinimumColorToolTip"));
        _colorScaleUseThreeColorBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_UseThreeColorScale"), Margin = new Thickness(0, 0, 0, 8) };
        _colorScaleMidTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Percentile };
        _colorScaleMidValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "50" };
        _colorScaleMidColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(255, 235, 132)) };
        _colorScaleMidColorButton = CreateColorScaleColorButton(_colorScaleMidColorBox, UiText.Get("ConditionalFormatDialog_ChooseMidpointColorToolTip"));
        _colorScaleUseThreeColorBox.Checked += (_, _) => UpdateColorScaleMidpointState();
        _colorScaleUseThreeColorBox.Unchecked += (_, _) => UpdateColorScaleMidpointState();
        _colorScaleMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
        _colorScaleMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _colorScaleMaxColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Text = FormatRgb(new RgbColor(248, 105, 107)) };
        _colorScaleMaxColorButton = CreateColorScaleColorButton(_colorScaleMaxColorBox, UiText.Get("ConditionalFormatDialog_ChooseMaximumColorToolTip"));
        _dateOccurringPeriodBox = CreateDateOccurringPeriodBox();
        _duplicateValuesKindBox = CreateDuplicateValuesKindBox();

        if (isFormula)
        {
            Height = 200;
            _formulaBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "=" };
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_FormulaLabel"), _formulaBox));
            inner.Children.Add(_formulaBox);
            // placeholders needed by Ok_Click — never shown
            ResetValueInputs();
        }
        else if (isDataBar)
        {
            Height = 600;
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumTypeLabel"), _dataBarMinTypeBox));
            inner.Children.Add(_dataBarMinTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumValueLabel"), _dataBarMinValueBox));
            inner.Children.Add(_dataBarMinValueBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumTypeLabel"), _dataBarMaxTypeBox));
            inner.Children.Add(_dataBarMaxTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumValueLabel"), _dataBarMaxValueBox));
            inner.Children.Add(_dataBarMaxValueBox);
            inner.Children.Add(_dataBarShowValueBox);
            inner.Children.Add(_dataBarGradientBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumBarLengthLabel"), _dataBarMinLengthBox));
            inner.Children.Add(_dataBarMinLengthBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumBarLengthLabel"), _dataBarMaxLengthBox));
            inner.Children.Add(_dataBarMaxLengthBox);
            inner.Children.Add(_dataBarBorderBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_AxisPositionLabel"), _dataBarAxisPositionBox));
            inner.Children.Add(_dataBarAxisPositionBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_AxisColorLabel"), _dataBarAxisColorBox));
            inner.Children.Add(CreateDataBarOptionalColorEditor(_dataBarAxisColorBox, _dataBarAxisColorButton));
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_NegativeBarColorLabel"), _dataBarNegativeFillColorBox));
            inner.Children.Add(CreateDataBarOptionalColorEditor(_dataBarNegativeFillColorBox, _dataBarNegativeFillColorButton));
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_NegativeBorderColorLabel"), _dataBarNegativeBorderColorBox));
            inner.Children.Add(CreateDataBarOptionalColorEditor(_dataBarNegativeBorderColorBox, _dataBarNegativeBorderColorButton));

            ResetValueInputs();
        }
        else if (isColorScale)
        {
            Height = 520;
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumTypeLabel"), _colorScaleMinTypeBox));
            inner.Children.Add(_colorScaleMinTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumValueLabel"), _colorScaleMinValueBox));
            inner.Children.Add(_colorScaleMinValueBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumColorLabel"), _colorScaleMinColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMinColorBox, _colorScaleMinColorButton));
            inner.Children.Add(_colorScaleUseThreeColorBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MidpointTypeLabel"), _colorScaleMidTypeBox));
            inner.Children.Add(_colorScaleMidTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MidpointValueLabel"), _colorScaleMidValueBox));
            inner.Children.Add(_colorScaleMidValueBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MidpointColorLabel"), _colorScaleMidColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMidColorBox, _colorScaleMidColorButton));
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumTypeLabel"), _colorScaleMaxTypeBox));
            inner.Children.Add(_colorScaleMaxTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumValueLabel"), _colorScaleMaxValueBox));
            inner.Children.Add(_colorScaleMaxValueBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumColorLabel"), _colorScaleMaxColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMaxColorBox, _colorScaleMaxColorButton));

            ResetValueInputs();
        }
        else if (isIconSet)
        {
            _iconSetStyleBox.SelectionChanged += (_, _) => BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string);
            _iconSetThresholdPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string ?? IconSetStyles[0]);

            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_IconSetLabel"), _iconSetStyleBox));
            inner.Children.Add(_iconSetStyleBox);
            inner.Children.Add(_iconSetShowValueBox);
            inner.Children.Add(_iconSetReverseBox);
            inner.Children.Add(new TextBlock { Text = UiText.Get("ConditionalFormatDialog_ThresholdsHeader"), Margin = new Thickness(0, 4, 0, 2) });
            inner.Children.Add(_iconSetThresholdPanel);

            ResetValueInputs();
        }
        else if (isDateOccurring && !IsContainsShellRuleType(ruleType))
        {
            Height = 220;
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_DatePeriodLabel"), _dateOccurringPeriodBox));
            inner.Children.Add(_dateOccurringPeriodBox);

            ResetValueInputs();
        }
        else if (isDuplicateValues)
        {
            Height = 220;
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_FormatCellsThatContainLabel"), _duplicateValuesKindBox));
            inner.Children.Add(_duplicateValuesKindBox);

            ResetValueInputs();
        }
        else
        {
            if (IsContainsShellRuleType(ruleType))
            {
                AddContainsShellEditor(inner, ruleType);
            }
            else if (ruleType is "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%")
            {
                Height = 220;
                _topBottomRankBox.Text = ruleType is "Top 10%" or "Bottom 10%" ? "10" : "10";
                inner.Children.Add(CreateAccessLabel(ruleType is "Top 10%" or "Bottom 10%" ? UiText.Get("ConditionalFormatDialog_PercentLabel") : UiText.Get("ConditionalFormatDialog_RankLabel"), _topBottomRankBox));
                inner.Children.Add(_topBottomRankBox);
            }
            else
            {
                Height = 180;
                ResetValueInputs();
            }
        }

        _colorBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        var colorLabel = new Label { Content = UiText.Get("ConditionalFormatDialog_FormatLabel"), Target = _colorBox, Padding = new Thickness(0) };
        foreach (var (lbl, _, _, _) in ColorOptions) _colorBox.Items.Add(lbl);
        _colorBox.SelectedIndex = 0;
        var formatButton = new Button
        {
            Content = UiText.Get("ConditionalFormatDialog_FormatButton"),
            Width = 84,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = UiText.Get("ConditionalFormatDialog_CustomFillColorToolTip")
        };
        formatButton.Click += FormatButton_Click;

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok     = new Button { Content = UiText.Ok,     Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = UiText.Cancel, Width = 80, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);

        if (!isIconSet && !isColorScale)
        {
            colorLabel.Content = isDataBar ? UiText.Get("ConditionalFormatDialog_BarColorLabel") : UiText.Get("ConditionalFormatDialog_FormatLabel");
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
        Title = UiText.Get("ConditionalFormatDialog_EditTitle");
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
                var overrides = existingRule.IconOverrides.Count > 0 ? existingRule.IconOverrides : null;
                BuildIconSetThresholdPanel(style, thresholds, overrides);
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
                _dataBarBorderBox.IsChecked = existingRule.DataBarBorder;
                _dataBarAxisPositionBox.SelectedItem = AxisPositionToLabel(existingRule.DataBarAxisPosition);
                _dataBarAxisColorBox.Text = existingRule.DataBarAxisColor is { } ac ? FormatRgb(ac) : "";
                _dataBarNegativeFillColorBox.Text = existingRule.DataBarNegativeFillColor is { } nf ? FormatRgb(nf) : "";
                _dataBarNegativeBorderColorBox.Text = existingRule.DataBarNegativeBorderColor is { } nb ? FormatRgb(nb) : "";
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
                _duplicateValuesKindBox.SelectedItem = existingRule.RuleType == CfRuleType.UniqueValues
                    ? UiText.Get("ConditionalFormatDialog_DuplicateKind_Unique")
                    : UiText.Get("ConditionalFormatDialog_DuplicateKind_Duplicate");
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
            _conditionKindBox.IsVisible ? _conditionKindBox :
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

    private void FormatStyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreFormatStyleChange || sender is not ComboBox cb || cb.SelectedItem is not string label)
            return;

        var newType = label switch
        {
            var value when value == UiText.Get("ConditionalFormatDialog_FormatStyle_2ColorScale") => "Color Scale",
            var value when value == UiText.Get("ConditionalFormatDialog_FormatStyle_3ColorScale") => "Color Scale",
            var value when value == UiText.Get("ConditionalFormatDialog_FormatStyle_IconSet") => "Icon Set",
            _               => "Data Bar"
        };
        RefreshRuleDescription(newType);
        if (label == UiText.Get("ConditionalFormatDialog_FormatStyle_3ColorScale") && _colorScaleUseThreeColorBox is not null)
        {
            _ignoreFormatStyleChange = true;
            _colorScaleUseThreeColorBox.IsChecked = true;
            _ignoreFormatStyleChange = false;
        }
    }

    private static Label CreateAccessLabel(string content, Control target) =>
        new() { Content = content, Target = target, Padding = new Thickness(0) };

    private static ComboBox CreateDuplicateValuesKindBox()
    {
        var comboBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        comboBox.Items.Add(UiText.Get("ConditionalFormatDialog_DuplicateKind_Duplicate"));
        comboBox.Items.Add(UiText.Get("ConditionalFormatDialog_DuplicateKind_Unique"));
        comboBox.SelectedItem = UiText.Get("ConditionalFormatDialog_DuplicateKind_Duplicate");
        return comboBox;
    }

    private static ComboBox CreateDateOccurringPeriodBox()
    {
        var comboBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (label, _) in DateOccurringPeriods)
            comboBox.Items.Add(label);
        comboBox.SelectedItem = UiText.Get("ConditionalFormatDialog_DatePeriod_Today");
        return comboBox;
    }

    private void ResetValueInputs()
    {
        _value1Box = new TextBox();
        _value2Box = new TextBox();
        _value2Label = new Label();
    }

    private void RefreshRuleDescription(string ruleType)
    {
        _ruleType = ruleType;
        _customFormatStyle = null;

        var inner = new StackPanel { Margin = new Thickness(16) };
        var isFormula = ruleType is "Formula" or "Use a Formula";
        var isIconSet = ruleType is "Icon Set";
        var isDataBar = ruleType is "Data Bar";
        var isColorScale = ruleType is "Color Scale";
        var isDuplicateValues = ruleType is "Duplicate Values";
        var isDateOccurring = ruleType is "Date Occurring";

        if (isDataBar || isColorScale || isIconSet)
        {
            _formatStyleBox = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 12),
                ItemsSource = FormatStyleLabels,
                SelectedItem = CurrentFormatStyleLabel
            };
            _formatStyleBox.SelectionChanged += FormatStyleBox_SelectionChanged;
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_FormatStyleLabel"), _formatStyleBox));
            inner.Children.Add(_formatStyleBox);
        }
        else
        {
            _formatStyleBox = null;
        }

        if (isFormula)
        {
            Height = 200;
            _formulaBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "=" };
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_FormulaLabel"), _formulaBox));
            inner.Children.Add(_formulaBox);
            ResetValueInputs();
        }
        else if (isDataBar)
        {
            Height = 600;
            _dataBarMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
            _dataBarMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
            _dataBarMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarShowValueBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_ShowBarOnly"), Margin = new Thickness(0, 0, 0, 8), IsChecked = false };
            _dataBarGradientBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_GradientFill"), Margin = new Thickness(0, 0, 0, 8), IsChecked = true };
            _dataBarColorButton = CreateDataBarColorButton();
            _dataBarMinLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarMaxLengthBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarBorderBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_ShowBorder"), Margin = new Thickness(0, 0, 0, 6) };
            _dataBarAxisPositionBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8) };
            foreach (var p in DataBarAxisPositionLabels()) _dataBarAxisPositionBox.Items.Add(p);
            _dataBarAxisPositionBox.SelectedItem = UiText.Get("ConditionalFormatDialog_AxisPosition_Automatic");
            _dataBarAxisColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarAxisColorButton = CreateDataBarOptionalColorButton(_dataBarAxisColorBox, UiText.Get("ConditionalFormatDialog_ChooseAxisColorToolTip"));
            _dataBarNegativeFillColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _dataBarNegativeFillColorButton = CreateDataBarOptionalColorButton(_dataBarNegativeFillColorBox, UiText.Get("ConditionalFormatDialog_ChooseNegativeBarColorToolTip"));
            _dataBarNegativeBorderColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 12) };
            _dataBarNegativeBorderColorButton = CreateDataBarOptionalColorButton(_dataBarNegativeBorderColorBox, UiText.Get("ConditionalFormatDialog_ChooseNegativeBarBorderColorToolTip"));
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumTypeLabel"), _dataBarMinTypeBox));
            inner.Children.Add(_dataBarMinTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumValueLabel"), _dataBarMinValueBox));
            inner.Children.Add(_dataBarMinValueBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumTypeLabel"), _dataBarMaxTypeBox));
            inner.Children.Add(_dataBarMaxTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumValueLabel"), _dataBarMaxValueBox));
            inner.Children.Add(_dataBarMaxValueBox);
            inner.Children.Add(_dataBarShowValueBox);
            inner.Children.Add(_dataBarGradientBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumBarLengthLabel"), _dataBarMinLengthBox));
            inner.Children.Add(_dataBarMinLengthBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumBarLengthLabel"), _dataBarMaxLengthBox));
            inner.Children.Add(_dataBarMaxLengthBox);
            inner.Children.Add(_dataBarBorderBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_AxisPositionLabel"), _dataBarAxisPositionBox));
            inner.Children.Add(_dataBarAxisPositionBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_AxisColorLabel"), _dataBarAxisColorBox));
            inner.Children.Add(CreateDataBarOptionalColorEditor(_dataBarAxisColorBox, _dataBarAxisColorButton));
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_NegativeBarColorLabel"), _dataBarNegativeFillColorBox));
            inner.Children.Add(CreateDataBarOptionalColorEditor(_dataBarNegativeFillColorBox, _dataBarNegativeFillColorButton));
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_NegativeBorderColorLabel"), _dataBarNegativeBorderColorBox));
            inner.Children.Add(CreateDataBarOptionalColorEditor(_dataBarNegativeBorderColorBox, _dataBarNegativeBorderColorButton));
            ResetValueInputs();
        }
        else if (isColorScale)
        {
            Height = 520;
            _colorScaleMinTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Min };
            _colorScaleMinValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _colorScaleMinColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(99, 190, 123)) };
            _colorScaleMinColorButton = CreateColorScaleColorButton(_colorScaleMinColorBox, UiText.Get("ConditionalFormatDialog_ChooseMinimumColorToolTip"));
            _colorScaleUseThreeColorBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_UseThreeColorScale"), Margin = new Thickness(0, 0, 0, 8) };
            _colorScaleUseThreeColorBox.Checked += (_, _) => UpdateColorScaleMidpointState();
            _colorScaleUseThreeColorBox.Unchecked += (_, _) => UpdateColorScaleMidpointState();
            _colorScaleMidTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Percentile };
            _colorScaleMidValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "50" };
            _colorScaleMidColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = FormatRgb(new RgbColor(255, 235, 132)) };
            _colorScaleMidColorButton = CreateColorScaleColorButton(_colorScaleMidColorBox, UiText.Get("ConditionalFormatDialog_ChooseMidpointColorToolTip"));
            _colorScaleMaxTypeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = Enum.GetValues<CfThresholdType>(), SelectedItem = CfThresholdType.Max };
            _colorScaleMaxValueBox = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
            _colorScaleMaxColorBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Text = FormatRgb(new RgbColor(248, 105, 107)) };
            _colorScaleMaxColorButton = CreateColorScaleColorButton(_colorScaleMaxColorBox, UiText.Get("ConditionalFormatDialog_ChooseMaximumColorToolTip"));
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumTypeLabel"), _colorScaleMinTypeBox));
            inner.Children.Add(_colorScaleMinTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumValueLabel"), _colorScaleMinValueBox));
            inner.Children.Add(_colorScaleMinValueBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MinimumColorLabel"), _colorScaleMinColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMinColorBox, _colorScaleMinColorButton));
            inner.Children.Add(_colorScaleUseThreeColorBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MidpointTypeLabel"), _colorScaleMidTypeBox));
            inner.Children.Add(_colorScaleMidTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MidpointValueLabel"), _colorScaleMidValueBox));
            inner.Children.Add(_colorScaleMidValueBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MidpointColorLabel"), _colorScaleMidColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMidColorBox, _colorScaleMidColorButton));
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumTypeLabel"), _colorScaleMaxTypeBox));
            inner.Children.Add(_colorScaleMaxTypeBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumValueLabel"), _colorScaleMaxValueBox));
            inner.Children.Add(_colorScaleMaxValueBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_MaximumColorLabel"), _colorScaleMaxColorBox));
            inner.Children.Add(CreateColorScaleColorEditor(_colorScaleMaxColorBox, _colorScaleMaxColorButton));
            ResetValueInputs();
            UpdateColorScaleMidpointState();
        }
        else if (isIconSet)
        {
            _iconSetStyleBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8) };
            foreach (var style in IconSetStyles) _iconSetStyleBox.Items.Add(style);
            _iconSetStyleBox.SelectedIndex = 0;
            _iconSetStyleBox.SelectionChanged += (_, _) => BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string);
            _iconSetShowValueBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_ShowValue"), Margin = new Thickness(0, 0, 0, 6), IsChecked = true };
            _iconSetReverseBox = new CheckBox { Content = UiText.Get("ConditionalFormatDialog_ReverseIconOrder"), Margin = new Thickness(0, 0, 0, 12) };
            _iconSetThresholdPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string ?? IconSetStyles[0]);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_IconSetLabel"), _iconSetStyleBox));
            inner.Children.Add(_iconSetStyleBox);
            inner.Children.Add(_iconSetShowValueBox);
            inner.Children.Add(_iconSetReverseBox);
            inner.Children.Add(new TextBlock { Text = UiText.Get("ConditionalFormatDialog_ThresholdsHeader"), Margin = new Thickness(0, 4, 0, 2) });
            inner.Children.Add(_iconSetThresholdPanel);
            ResetValueInputs();
        }
        else if (isDuplicateValues)
        {
            Height = 220;
            _duplicateValuesKindBox = CreateDuplicateValuesKindBox();
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_FormatCellsThatContainLabel"), _duplicateValuesKindBox));
            inner.Children.Add(_duplicateValuesKindBox);
            ResetValueInputs();
        }
        else if (isDateOccurring && !IsContainsShellRuleType(ruleType))
        {
            Height = 220;
            _dateOccurringPeriodBox = CreateDateOccurringPeriodBox();
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_DatePeriodLabel"), _dateOccurringPeriodBox));
            inner.Children.Add(_dateOccurringPeriodBox);
            ResetValueInputs();
        }
        else
        {
            if (IsContainsShellRuleType(ruleType))
            {
                AddContainsShellEditor(inner, ruleType);
            }
            else if (ruleType is "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%")
            {
                Height = 220;
                _topBottomRankBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Text = "10" };
                inner.Children.Add(CreateAccessLabel(ruleType is "Top 10%" or "Bottom 10%" ? UiText.Get("ConditionalFormatDialog_PercentLabel") : UiText.Get("ConditionalFormatDialog_RankLabel"), _topBottomRankBox));
                inner.Children.Add(_topBottomRankBox);
            }
            else
            {
                Height = 180;
                ResetValueInputs();
            }
        }

        _colorBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (label, _, _, _) in ColorOptions) _colorBox.Items.Add(label);
        _colorBox.SelectedIndex = 0;
        var formatButton = new Button
        {
            Content = UiText.Get("ConditionalFormatDialog_FormatButton"),
            Width = 84,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = UiText.Get("ConditionalFormatDialog_CustomFillColorToolTip")
        };
        formatButton.Click += FormatButton_Click;

        if (ruleType is not ("Icon Set" or "Color Scale"))
        {
            inner.Children.Add(new Label { Content = ruleType is "Data Bar" ? UiText.Get("ConditionalFormatDialog_BarColorLabel") : UiText.Get("ConditionalFormatDialog_FormatWithLabel"), Target = _colorBox, Padding = new Thickness(0) });
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
        var ok = new Button { Content = UiText.Ok, Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = UiText.Cancel, Width = 80, IsCancel = true };
        ok.Click += Ok_Click;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        inner.Children.Add(btnRow);

        if (_descriptionHost is null)
            return;

        _descriptionHost.Children.RemoveRange(1, _descriptionHost.Children.Count - 1);
        _descriptionHost.Children.Add(inner);
    }

    private void AddContainsShellEditor(StackPanel inner, string ruleType)
    {
        Height = ruleType is "Between" or "Not Between" ? 320 : 280;
        _conditionKindBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = ConditionKindLabels };
        _conditionKindBox.SelectedItem = ConditionKindLabelForRuleType(ruleType);
        _conditionKindBox.SelectionChanged += ConditionKindBox_SelectionChanged;
        inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_FormatOnlyCellsWithLabel"), _conditionKindBox));
        inner.Children.Add(_conditionKindBox);

        _value1Box = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _value2Box = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        _value2Label = new Label { Content = UiText.Get("ConditionalFormatDialog_MaximumLabel"), Target = _value2Box, Padding = new Thickness(0) };
        _cellValueOperatorBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = CellValueOperatorLabels.Select(item => item.Label).ToArray() };
        _specificTextOperatorBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), ItemsSource = SpecificTextOperatorLabels.Select(item => item.Label).ToArray() };

        var kind = _conditionKindBox.SelectedItem as string;
        if (kind == UiText.Get("ConditionalFormatDialog_ConditionKind_CellValue"))
        {
            _cellValueOperatorBox.SelectedItem = CellValueOperatorLabelForRuleType(ruleType);
            _cellValueOperatorBox.SelectionChanged += CellValueOperatorBox_SelectionChanged;
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_OperatorLabel"), _cellValueOperatorBox));
            inner.Children.Add(_cellValueOperatorBox);
            AddValueBoxes(inner, ruleType is "Between" or "Not Between");
        }
        else if (kind == UiText.Get("ConditionalFormatDialog_ConditionKind_SpecificText"))
        {
            _specificTextOperatorBox.SelectedItem = SpecificTextOperatorLabelForRuleType(ruleType);
            _specificTextOperatorBox.SelectionChanged += SpecificTextOperatorBox_SelectionChanged;
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_TextOperatorLabel"), _specificTextOperatorBox));
            inner.Children.Add(_specificTextOperatorBox);
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_TextLabel"), _value1Box));
            inner.Children.Add(_value1Box);
        }
        else if (kind == UiText.Get("ConditionalFormatDialog_ConditionKind_DatesOccurring"))
        {
            _dateOccurringPeriodBox = CreateDateOccurringPeriodBox();
            inner.Children.Add(CreateAccessLabel(UiText.Get("ConditionalFormatDialog_DatePeriodLabel"), _dateOccurringPeriodBox));
            inner.Children.Add(_dateOccurringPeriodBox);
        }
    }

    private void AddValueBoxes(StackPanel inner, bool isBetween)
    {
        inner.Children.Add(new Label { Content = isBetween ? UiText.Get("ConditionalFormatDialog_MinimumLabel") : UiText.Get("ConditionalFormatDialog_ValueLabel"), Target = _value1Box, Padding = new Thickness(0) });
        inner.Children.Add(_value1Box);
        if (isBetween)
        {
            inner.Children.Add(_value2Label);
            inner.Children.Add(_value2Box);
        }
    }

    private void ConditionKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string label })
            RefreshRuleDescription(DefaultRuleTypeForConditionKind(label));
    }

    private void CellValueOperatorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string label } &&
            CellValueOperatorLabels.FirstOrDefault(item => item.Label == label) is var match &&
            match.RuleType is not null)
            RefreshRuleDescription(match.RuleType);
    }

    private void SpecificTextOperatorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string label } &&
            SpecificTextOperatorLabels.FirstOrDefault(item => item.Label == label) is var match &&
            match.RuleType is not null)
            RefreshRuleDescription(match.RuleType);
    }

    private static bool IsContainsShellRuleType(string ruleType) =>
        ruleType is "Greater Than" or "Less Than" or "Equal To" or "Between" or "Not Equal To"
            or "Greater Than Or Equal To" or "Less Than Or Equal To" or "Not Between"
            or "Text Contains" or "Text Does Not Contain" or "Text Begins With" or "Text Ends With"
            or "Date Occurring" or "Blanks" or "No Blanks" or "Errors" or "No Errors";

}
