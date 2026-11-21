using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// The Excel-style conditional-format rule editor: a port of the WPF host's
    /// <c>ConditionalFormatDialog</c> (the base of the Highlight Cells, Top/Bottom, Data Bar, Color
    /// Scale, Icon Set and New/Edit Formatting Rule dialogs). A "Select a Rule Type" list on the left
    /// drives a rule-type-specific description editor on the right that is rebuilt on every change,
    /// exactly like WPF's <c>RefreshRuleDescription</c>, and the OK handler collects the same
    /// <see cref="CfRuleInput"/> WPF does, so every stored data-bar, color-scale and icon-set setting
    /// round-trips through an edit.
    /// </summary>
    private sealed class ConditionalFormatRuleEditor
    {
        // Threshold types offered by the color-scale and icon-set pickers. Excludes the data-bar-only
        // "Automatic" endpoints, matching WPF's ClassicThresholdTypes.
        private static readonly CfThresholdType[] ClassicThresholdTypes =
        [
            CfThresholdType.Min,
            CfThresholdType.Max,
            CfThresholdType.Number,
            CfThresholdType.Percent,
            CfThresholdType.Percentile,
            CfThresholdType.Formula,
        ];

        private static readonly IReadOnlyList<string> IconSetStyles = ConditionalFormatIconSetCatalog.GalleryStyles;
        private static readonly string[] IconOverrideChoices = BuildIconOverrideChoices();

        private readonly MainWindow _owner;
        private readonly Window _dialog;
        private readonly GridRange _range;
        private readonly Guid _existingId;
        private readonly ConditionalFormat? _existingRule;
        private readonly StackPanel _descriptionHost;
        private readonly ListBox _shellList;

        private string _ruleType;
        private bool _ignoreShellChange;
        private bool _ignoreFormatStyleChange;
        private ConditionalFormat? _result;
        private CellStyle? _customFormatStyle;
        private Button _okButton = new();
        private Button _cancelButton = new();

        private TextBox _value1Box = new();
        private TextBox _value2Box = new();
        private TextBlock _value2Label = new();
        private ComboBox _colorBox = new();
        private TextBox? _formulaBox;
        private ComboBox _conditionKindBox = new();
        private ComboBox _cellValueOperatorBox = new();
        private ComboBox _specificTextOperatorBox = new();
        private ComboBox? _formatStyleBox;
        private ComboBox _iconSetStyleBox = new();
        private CheckBox _iconSetShowValueBox = new();
        private CheckBox _iconSetReverseBox = new();
        private StackPanel _iconSetThresholdPanel = new();
        private readonly List<(ComboBox TypeBox, TextBox ValueBox, ComboBox OverrideBox)> _iconSetThresholdRows = [];
        private TextBox _topBottomRankBox = new();
        private ComboBox _dataBarMinTypeBox = new();
        private TextBox _dataBarMinValueBox = new();
        private ComboBox _dataBarMaxTypeBox = new();
        private TextBox _dataBarMaxValueBox = new();
        private CheckBox _dataBarShowValueBox = new();
        private CheckBox _dataBarGradientBox = new();
        private Button _dataBarColorButton = new();
        private TextBox _dataBarMinLengthBox = new();
        private TextBox _dataBarMaxLengthBox = new();
        private CheckBox _dataBarBorderBox = new();
        private ComboBox _dataBarAxisPositionBox = new();
        private TextBox _dataBarAxisColorBox = new();
        private Button _dataBarAxisColorButton = new();
        private TextBox _dataBarNegativeFillColorBox = new();
        private Button _dataBarNegativeFillColorButton = new();
        private TextBox _dataBarNegativeBorderColorBox = new();
        private Button _dataBarNegativeBorderColorButton = new();
        private ComboBox _colorScaleMinTypeBox = new();
        private TextBox _colorScaleMinValueBox = new();
        private TextBox _colorScaleMinColorBox = new();
        private Button _colorScaleMinColorButton = new();
        private CheckBox _colorScaleUseThreeColorBox = new();
        private ComboBox _colorScaleMidTypeBox = new();
        private TextBox _colorScaleMidValueBox = new();
        private TextBox _colorScaleMidColorBox = new();
        private Button _colorScaleMidColorButton = new();
        private ComboBox _colorScaleMaxTypeBox = new();
        private TextBox _colorScaleMaxValueBox = new();
        private TextBox _colorScaleMaxColorBox = new();
        private Button _colorScaleMaxColorButton = new();
        private ComboBox _dateOccurringPeriodBox = new();
        private ComboBox _duplicateValuesKindBox = new();

        public ConditionalFormatRuleEditor(
            MainWindow owner,
            string ruleType,
            GridRange range,
            ConditionalFormat? existingRule,
            string title)
        {
            _owner = owner;
            _ruleType = ruleType;
            _range = range;
            _existingRule = existingRule is null ? null : ConditionalFormatDialogPlanner.CloneRule(existingRule);
            _existingId = existingRule?.Id ?? Guid.NewGuid();

            _dialog = new Window
            {
                Title = title,
                // WPF ConditionalFormatDialog is a 650 *outer* wide NoResize window that sizes its height
                // to the content (SizeToContent.Height), so the colour-scale and top/bottom editors grow
                // the window instead of scrolling under the OK/Cancel row. The capture height stays the
                // floor for the shorter rule types.
                Width = ConditionalFormatDialogCatalog.RuleEditorCaptureWidth,
                MinWidth = ConditionalFormatDialogCatalog.RuleEditorMinWidth,
                MinHeight = ConditionalFormatDialogCatalog.RuleEditorCaptureHeight,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                // WPF ConditionalFormatDialog labels use the 12px dialog font; without this the field
                // labels fall back to the 14px Avalonia window default.
                FontFamily = FormulaBarFontFamily,
                FontSize = 12,
            };
            AutomationProperties.SetAutomationId(_dialog, "ConditionalFormatRuleDialog");

            _descriptionHost = new StackPanel();
            _shellList = BuildShellList();

            InitializeRuleSpecificControls();
            BuildDescription(isInitialBuild: true);
            if (existingRule is not null)
                PopulateFromExistingRule(existingRule);

            Control root = BuildRoot();
            ConfigureDialogTabCycle(_dialog, root);
            ConfigureNativeDialogInitialFocus(_dialog, root, ResolveInitialFocusTarget());
            _dialog.Content = root;

            _owner.AttachOptionalConditionalFormatRuleDialogObservation(
                _dialog,
                FindControl,
                SelectRuleType,
                _shellList,
                _okButton,
                _cancelButton);
        }

        public async Task<ConditionalFormat?> ShowAsync()
        {
            await _dialog.ShowDialog(_owner);
            return _result;
        }

        // ── Shell (left rule-type list, right description host) ────────────────────

        private ListBox BuildShellList()
        {
            var list = new ListBox
            {
                ItemsSource = ConditionalFormatDialogCatalog.RuleShellOptions
                    .Select(option => StripDisplayMnemonic(UiText.Get(option.LabelKey)))
                    .ToList(),
                MinHeight = 182,
                Background = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = Brush(171, 173, 179),
            };
            // The 218px column truncates the longer Excel rule-type captions; wrap them so each stays readable.
            list.ItemTemplate = new FuncDataTemplate<string>((label, _) => new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
            });
            // WPF ConditionalFormatDialog.RuleShell.cs: a default-template ListBox whose 12pt rows are 20px.
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters = { new Setter(Layoutable.MinHeightProperty, 20d) },
            });
            AutomationProperties.SetAutomationId(list, "ConditionalFormatRuleShellList");
            AutomationProperties.SetName(list, UiText.Get("ConditionalFormatDialog_RuleTypeAutomationName"));
            list.SelectedIndex = ShellIndexForRuleType(_ruleType);
            list.SelectionChanged += (_, _) => RuleTypeList_SelectionChanged();
            return list;
        }

        private static int ShellIndexForRuleType(string ruleType)
        {
            var key = ConditionalFormatDialogCatalog.ShellKeyForRuleType(ruleType);
            var options = ConditionalFormatDialogCatalog.RuleShellOptions;
            for (var i = 0; i < options.Count; i++)
            {
                if (options[i].LabelKey == key)
                    return i;
            }

            return 1;
        }

        private void RuleTypeList_SelectionChanged()
        {
            if (_ignoreShellChange || _shellList.SelectedIndex < 0)
                return;

            var shellKey = ConditionalFormatDialogCatalog.RuleShellOptions[_shellList.SelectedIndex].LabelKey;
            var newRuleType = ConditionalFormatDialogCatalog.DefaultRuleTypeForShellKey(shellKey, _ruleType);
            if (newRuleType == _ruleType)
                return;

            RefreshRuleDescription(newRuleType);
        }

        private Control BuildRoot()
        {
            // Left column: "Select a Rule Type:" header + the Excel rule-type list.
            var leftColumn = new StackPanel
            {
                Width = 218,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 12, 0),
                Children =
                {
                    new TextBlock
                    {
                        Text = StripDisplayMnemonic(UiText.Get("ConditionalFormatDialog_SelectRuleTypeHeader")),
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 6),
                    },
                    _shellList,
                },
            };
            AvaloniaGrid.SetColumn(leftColumn, 0);

            // ConditionalFormatDialog.xaml: the first label sits 18 px under the header.
            _descriptionHost.Margin = new Thickness(0, 18, 0, 0);
            var descriptionEditor = new ScrollViewer { Content = _descriptionHost };

            var rightColumn = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = StripDisplayMnemonic(UiText.Get("ConditionalFormatDialog_EditRuleDescriptionHeader")),
                        FontWeight = FontWeight.SemiBold,
                        // WPF outdents the heading 16px to the left of the description controls.
                        Margin = new Thickness(-16, 0, 0, 6),
                        [DockPanel.DockProperty] = Dock.Top,
                    },
                    descriptionEditor,
                },
            };
            AvaloniaGrid.SetColumn(rightColumn, 1);

            var root = new AvaloniaGrid
            {
                Margin = new Thickness(16, 16, 29, 29),
                ColumnDefinitions = new ColumnDefinitions("244,*"),
                Children =
                {
                    leftColumn,
                    rightColumn,
                },
            };
            // The WPF capture manifest normalises the 650x371 parity window to the 634x334 client area
            // (RuleEditorCaptureWidth/Height), so 334 is a client height. SizeToContent sizes the window
            // from its content and does not honour the window MinHeight, so the content carries the
            // floor; the button row then sits at the WPF position for the shorter rule types.
            root.MinHeight = Math.Max(0, ConditionalFormatDialogCatalog.RuleEditorCaptureHeight - root.Margin.Top - root.Margin.Bottom);
            return root;
        }

        private Control ResolveInitialFocusTarget()
        {
            var ruleType = _ruleType;
            if (ConditionalFormatDialogCatalog.IsFormulaRuleType(ruleType) && _formulaBox is not null)
                return _formulaBox;
            if (ConditionalFormatDialogCatalog.IsContainsShellRuleType(ruleType))
                return _conditionKindBox;
            if (ConditionalFormatDialogCatalog.IsTopBottomRuleType(ruleType))
                return _topBottomRankBox;

            return ruleType switch
            {
                ConditionalFormatDialogCatalog.DataBarRule => _dataBarMinTypeBox,
                ConditionalFormatDialogCatalog.ColorScaleRule => _colorScaleMinTypeBox,
                ConditionalFormatDialogCatalog.IconSetRule => _iconSetStyleBox,
                ConditionalFormatDialogCatalog.DuplicateValuesRule => _duplicateValuesKindBox,
                _ => _colorBox,
            };
        }

        // ── Control factories ──────────────────────────────────────────────────────

        private static TextBlock Label(string resourceKey, Control? target = null)
        {
            var label = new TextBlock { Text = StripDisplayMnemonic(UiText.Get(resourceKey)) };
            if (target is not null)
                AutomationProperties.SetLabeledBy(target, label);
            return label;
        }

        private static ComboBox NewCombo(Thickness margin, string? automationId = null)
        {
            var combo = new ComboBox { Margin = margin, HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
            ApplyCfComboBoxChrome(combo);
            combo.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
            // ConditionalFormatDialog.xaml: default WPF combo boxes are 22 px at the 12 px dialog font.
            combo.Height = 22;
            combo.MinHeight = 22;
            combo.MaxHeight = 22;
            if (automationId is not null)
                AutomationProperties.SetAutomationId(combo, automationId);
            return combo;
        }

        private static TextBox NewTextBox(Thickness margin, string? automationId = null, string? text = null)
        {
            var box = new TextBox { Margin = margin, HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
            if (text is not null)
                box.Text = text;
            ApplyCfTextBoxChrome(box);
            box.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
            // ...and default WPF text boxes are 18 px.
            box.Height = 18;
            box.MinHeight = 18;
            box.MaxHeight = 18;
            if (automationId is not null)
                AutomationProperties.SetAutomationId(box, automationId);
            return box;
        }

        private static CheckBox NewCheckBox(string resourceKey, Thickness margin, bool isChecked, string? automationId = null)
        {
            var box = new CheckBox { Content = UiText.Get(resourceKey), Margin = margin, IsChecked = isChecked };
            ApplyCfCheckBoxChrome(box);
            if (automationId is not null)
                AutomationProperties.SetAutomationId(box, automationId);
            return box;
        }

        private static Button NewPickerButton(string tooltipKey, Thickness margin, string automationId)
        {
            var button = new Button { Content = "...", Margin = margin };
            ApplyCfButtonChrome(button, 28);
            button.Height = 21;
            button.MinHeight = 21;
            button.MaxHeight = 21;
            ToolTip.SetTip(button, UiText.Get(tooltipKey));
            AutomationProperties.SetAutomationId(button, automationId);
            return button;
        }

        private static DockPanel DockedPickerRow(Control fill, Button picker, Thickness? margin = null)
        {
            var panel = new DockPanel();
            if (margin is { } m)
                panel.Margin = m;
            DockPanel.SetDock(picker, Dock.Right);
            panel.Children.Add(picker);
            panel.Children.Add(fill);
            return panel;
        }

        private static string[] Localized(IReadOnlyList<ConditionalFormatDialogOption> options) =>
            options.Select(option => UiText.Get(option.LabelKey)).ToArray();

        private static (string Label, string RuleType)[] LocalizedWithRuleType(
            IReadOnlyList<ConditionalFormatDialogOption> options) =>
            options.Select(option => (UiText.Get(option.LabelKey), option.RuleType)).ToArray();

        private static string[] FormatStyleLabels =>
            ConditionalFormatDialogCatalog.FormatStyleOptions.Select(option => UiText.Get(option.LabelKey)).ToArray();

        private static IReadOnlyList<ConditionalFormatDialogColorPreset> ColorOptions =>
            ConditionalFormatDialogCatalog.ColorPresets;

        private static string CustomFormatLabel => UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat");

        // ── Control construction (WPF InitializeRuleSpecificControls) ───────────────

        private void InitializeRuleSpecificControls()
        {
            CreateIconSetControls();
            _topBottomRankBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatRankBox", "10");
            CreateDataBarControls();
            CreateColorScaleControls();
            _dateOccurringPeriodBox = CreateDateOccurringPeriodBox();
            _duplicateValuesKindBox = CreateDuplicateValuesKindBox();
        }

        private void CreateIconSetControls()
        {
            _iconSetStyleBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatIconSetBox");
            _iconSetStyleBox.ItemsSource = IconSetStyles.ToList();
            _iconSetStyleBox.SelectedIndex = 0;
            _iconSetShowValueBox = NewCheckBox("ConditionalFormatDialog_ShowValue", new Thickness(0, 0, 0, 6), true, "ConditionalFormatIconSetShowValueBox");
            _iconSetReverseBox = NewCheckBox("ConditionalFormatDialog_ReverseIconOrder", new Thickness(0, 0, 0, 12), false, "ConditionalFormatIconSetReverseBox");
        }

        private void CreateDataBarControls()
        {
            static FuncDataTemplate<CfThresholdType> ThresholdTemplate() =>
                new((value, _) => new TextBlock
                {
                    Text = value is CfThresholdType.AutoMin or CfThresholdType.AutoMax
                        ? UiText.Get("ConditionalFormatDialog_ThresholdType_Automatic")
                        : value.ToString(),
                });

            _dataBarMinTypeBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarMinTypeBox");
            _dataBarMinTypeBox.ItemsSource = Enum.GetValues<CfThresholdType>();
            _dataBarMinTypeBox.ItemTemplate = ThresholdTemplate();
            _dataBarMinTypeBox.SelectedItem = CfThresholdType.AutoMin;
            _dataBarMinValueBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarMinValueBox");
            _dataBarMaxTypeBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarMaxTypeBox");
            _dataBarMaxTypeBox.ItemsSource = Enum.GetValues<CfThresholdType>();
            _dataBarMaxTypeBox.ItemTemplate = ThresholdTemplate();
            _dataBarMaxTypeBox.SelectedItem = CfThresholdType.AutoMax;
            _dataBarMaxValueBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarMaxValueBox");
            _dataBarShowValueBox = NewCheckBox("ConditionalFormatDialog_ShowBarOnly", new Thickness(0, 0, 0, 8), false, "ConditionalFormatDataBarShowBarOnlyBox");
            _dataBarGradientBox = NewCheckBox("ConditionalFormatDialog_GradientFill", new Thickness(0, 0, 0, 8), true, "ConditionalFormatDataBarGradientBox");
            _dataBarColorButton = NewPickerButton("ConditionalFormatDialog_ChooseDataBarColorToolTip", new Thickness(6, 4, 0, 12), "ConditionalFormatDataBarColorButton");
            _dataBarColorButton.Click += (_, _) => _owner.RunGuarded(FormatButton_ClickAsync);
            _dataBarMinLengthBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarMinLengthBox");
            _dataBarMaxLengthBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarMaxLengthBox");
            _dataBarBorderBox = NewCheckBox("ConditionalFormatDialog_ShowBorder", new Thickness(0, 0, 0, 6), false, "ConditionalFormatDataBarBorderBox");
            _dataBarAxisPositionBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarAxisPositionBox");
            _dataBarAxisPositionBox.ItemsSource = DataBarAxisPositionLabels();
            _dataBarAxisPositionBox.SelectedItem = UiText.Get("ConditionalFormatDialog_AxisPosition_Automatic");
            _dataBarAxisColorBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarAxisColorBox");
            _dataBarAxisColorButton = CreateOptionalColorButton(_dataBarAxisColorBox, "ConditionalFormatDialog_ChooseAxisColorToolTip", "ConditionalFormatDataBarAxisColorButton");
            _dataBarNegativeFillColorBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatDataBarNegativeFillColorBox");
            _dataBarNegativeFillColorButton = CreateOptionalColorButton(_dataBarNegativeFillColorBox, "ConditionalFormatDialog_ChooseNegativeBarColorToolTip", "ConditionalFormatDataBarNegativeFillColorButton");
            _dataBarNegativeBorderColorBox = NewTextBox(new Thickness(0, 4, 0, 12), "ConditionalFormatDataBarNegativeBorderColorBox");
            _dataBarNegativeBorderColorButton = CreateOptionalColorButton(_dataBarNegativeBorderColorBox, "ConditionalFormatDialog_ChooseNegativeBarBorderColorToolTip", "ConditionalFormatDataBarNegativeBorderColorButton");
        }

        private void CreateColorScaleControls()
        {
            _colorScaleMinTypeBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatColorScaleMinTypeBox");
            _colorScaleMinTypeBox.ItemsSource = ClassicThresholdTypes;
            _colorScaleMinTypeBox.SelectedItem = CfThresholdType.Min;
            _colorScaleMinValueBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatColorScaleMinValueBox");
            _colorScaleMinColorBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatMinColorBox", FormatRgb(new RgbColor(99, 190, 123)));
            _colorScaleMinColorButton = CreateColorScaleColorButton(_colorScaleMinColorBox, "ConditionalFormatDialog_ChooseMinimumColorToolTip", "ConditionalFormatColorScaleMinColorButton");
            _colorScaleUseThreeColorBox = NewCheckBox("ConditionalFormatDialog_UseThreeColorScale", new Thickness(0, 0, 0, 8), false, "ConditionalFormatThreeColorBox");
            _colorScaleMidTypeBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatColorScaleMidTypeBox");
            _colorScaleMidTypeBox.ItemsSource = ClassicThresholdTypes;
            _colorScaleMidTypeBox.SelectedItem = CfThresholdType.Percentile;
            _colorScaleMidValueBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatColorScaleMidValueBox", "50");
            _colorScaleMidColorBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatMidColorBox", FormatRgb(new RgbColor(255, 235, 132)));
            _colorScaleMidColorButton = CreateColorScaleColorButton(_colorScaleMidColorBox, "ConditionalFormatDialog_ChooseMidpointColorToolTip", "ConditionalFormatColorScaleMidColorButton");
            _colorScaleUseThreeColorBox.IsCheckedChanged += (_, _) => UpdateColorScaleMidpointState();
            _colorScaleMaxTypeBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatColorScaleMaxTypeBox");
            _colorScaleMaxTypeBox.ItemsSource = ClassicThresholdTypes;
            _colorScaleMaxTypeBox.SelectedItem = CfThresholdType.Max;
            _colorScaleMaxValueBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatColorScaleMaxValueBox");
            _colorScaleMaxColorBox = NewTextBox(new Thickness(0, 4, 0, 12), "ConditionalFormatMaxColorBox", FormatRgb(new RgbColor(248, 105, 107)));
            _colorScaleMaxColorButton = CreateColorScaleColorButton(_colorScaleMaxColorBox, "ConditionalFormatDialog_ChooseMaximumColorToolTip", "ConditionalFormatColorScaleMaxColorButton");
        }

        private static ComboBox CreateDuplicateValuesKindBox()
        {
            var comboBox = NewCombo(new Thickness(0, 4, 0, 12), "ConditionalFormatDuplicateKindBox");
            comboBox.ItemsSource = new[]
            {
                UiText.Get("ConditionalFormatDialog_DuplicateKind_Duplicate"),
                UiText.Get("ConditionalFormatDialog_DuplicateKind_Unique"),
            };
            comboBox.SelectedItem = UiText.Get("ConditionalFormatDialog_DuplicateKind_Duplicate");
            return comboBox;
        }

        private static ComboBox CreateDateOccurringPeriodBox()
        {
            var comboBox = NewCombo(new Thickness(0, 4, 0, 12), "ConditionalFormatDatePeriodBox");
            comboBox.ItemsSource = ConditionalFormatDialogCatalog.DatePeriodOptions
                .Select(option => UiText.Get(option.LabelKey))
                .ToList();
            comboBox.SelectedItem = UiText.Get("ConditionalFormatDialog_DatePeriod_Today");
            return comboBox;
        }

        private void ResetValueInputs()
        {
            _value1Box = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatValue1Box");
            _value2Box = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatValue2Box");
            _value2Label = new TextBlock();
        }

        // ── Description rebuild (WPF constructor body / RefreshRuleDescription) ─────

        private void RefreshRuleDescription(string ruleType)
        {
            _ruleType = ruleType;
            _customFormatStyle = null;
            BuildDescription(isInitialBuild: false);
        }

        private void BuildDescription(bool isInitialBuild)
        {
            var ruleType = _ruleType;
            var inner = new StackPanel();
            var isFormula = ConditionalFormatDialogCatalog.IsFormulaRuleType(ruleType);
            var isIconSet = ruleType is ConditionalFormatDialogCatalog.IconSetRule;
            var isDataBar = ruleType is ConditionalFormatDialogCatalog.DataBarRule;
            var isColorScale = ruleType is ConditionalFormatDialogCatalog.ColorScaleRule;
            var isDateOccurring = ruleType is ConditionalFormatDialogCatalog.DateOccurringRule;
            var isDuplicateValues = ruleType is ConditionalFormatDialogCatalog.DuplicateValuesRule;

            // WPF only inserts the "Format Style" selector when the description is rebuilt from the
            // rule-type list; the first build (constructor) opens the visual families without it.
            if (!isInitialBuild && (isDataBar || isColorScale || isIconSet))
            {
                // Fresh visual controls per rebuild keep stale state from leaking between families.
                _formatStyleBox = NewCombo(new Thickness(0, 4, 0, 12), "ConditionalFormatFormatStyleBox");
                _formatStyleBox.ItemsSource = FormatStyleLabels;
                _formatStyleBox.SelectionChanged += FormatStyleBox_SelectionChanged;
                inner.Children.Add(Label("ConditionalFormatDialog_FormatStyleLabel", _formatStyleBox));
                inner.Children.Add(_formatStyleBox);
            }
            else
            {
                _formatStyleBox = null;
            }

            if (isFormula)
            {
                _formulaBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatFormulaBox", "=");
                inner.Children.Add(Label("ConditionalFormatDialog_FormulaLabel", _formulaBox));
                inner.Children.Add(_formulaBox);
                // placeholders needed by OK - never shown
                ResetValueInputs();
            }
            else
            {
                _formulaBox = null;
                if (isDataBar)
                {
                    if (!isInitialBuild)
                        CreateDataBarControls();
                    BuildDataBarEditor(inner);
                }
                else if (isColorScale)
                {
                    if (!isInitialBuild)
                        CreateColorScaleControls();
                    BuildColorScaleEditor(inner);
                }
                else if (isIconSet)
                {
                    if (!isInitialBuild)
                        CreateIconSetControls();
                    BuildIconSetEditor(inner);
                }
                else if (isDuplicateValues)
                {
                    if (!isInitialBuild)
                        _duplicateValuesKindBox = CreateDuplicateValuesKindBox();
                    inner.Children.Add(Label("ConditionalFormatDialog_FormatCellsThatContainLabel", _duplicateValuesKindBox));
                    inner.Children.Add(_duplicateValuesKindBox);
                    ResetValueInputs();
                }
                else if (ConditionalFormatDialogCatalog.IsContainsShellRuleType(ruleType))
                {
                    AddContainsShellEditor(inner, ruleType);
                }
                else if (ConditionalFormatDialogCatalog.IsTopBottomRuleType(ruleType))
                {
                    _topBottomRankBox = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatRankBox", "10");
                    inner.Children.Add(Label(
                        ConditionalFormatDialogCatalog.IsTopBottomPercentRuleType(ruleType)
                            ? "ConditionalFormatDialog_PercentLabel"
                            : "ConditionalFormatDialog_RankLabel",
                        _topBottomRankBox));
                    inner.Children.Add(_topBottomRankBox);
                    ResetValueInputs();
                }
                else
                {
                    ResetValueInputs();
                }
            }

            _colorBox = NewCombo(new Thickness(0, 4, 0, 12), "ConditionalFormatHighlightBox");
            _colorBox.ItemsSource = ColorOptions.Select(option => UiText.Get(option.LabelKey)).ToList();
            _colorBox.SelectedIndex = 0;
            var formatButton = new Button { Content = StripDisplayMnemonic(UiText.Get("ConditionalFormatDialog_FormatButton")) };
            ApplyCfButtonChrome(formatButton, 84);
            formatButton.Height = 21;
            formatButton.MinHeight = 21;
            formatButton.MaxHeight = 21;
            formatButton.Margin = new Thickness(0, 0, 0, 12);
            formatButton.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
            ToolTip.SetTip(formatButton, UiText.Get("ConditionalFormatDialog_CustomFillColorToolTip"));
            AutomationProperties.SetAutomationId(formatButton, "ConditionalFormatFormatButton");
            formatButton.Click += (_, _) => _owner.RunGuarded(FormatButton_ClickAsync);

            if (!isIconSet && !isColorScale)
            {
                var colorLabelKey = isDataBar
                    ? "ConditionalFormatDialog_BarColorLabel"
                    : isInitialBuild
                        ? "ConditionalFormatDialog_FormatLabel"
                        : "ConditionalFormatDialog_FormatWithLabel";
                inner.Children.Add(Label(colorLabelKey, _colorBox));
                inner.Children.Add(isDataBar ? DockedPickerRow(_colorBox, _dataBarColorButton) : _colorBox);
                if (!isDataBar)
                    inner.Children.Add(formatButton);
            }

            if (isIconSet || isColorScale || isDataBar)
                AddVisualPreview(inner, ruleType);

            _okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
            ApplyCfButtonChrome(_okButton, 84, isDefault: true);
            _okButton.Height = 21;
            _okButton.MinHeight = 21;
            _okButton.MaxHeight = 21;
            AutomationProperties.SetAutomationId(_okButton, "ConditionalFormatOkButton");
            _cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
            ApplyCfButtonChrome(_cancelButton, 84);
            _cancelButton.Height = 21;
            _cancelButton.MinHeight = 21;
            _cancelButton.MaxHeight = 21;
            AutomationProperties.SetAutomationId(_cancelButton, "ConditionalFormatCancelButton");
            _okButton.Click += (_, _) => _owner.RunGuarded(OkClickAsync);
            _cancelButton.Click += (_, _) => _dialog.Close();
            // WPF button order: [OK][Cancel] - primary on left
            inner.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([_okButton, _cancelButton], new Thickness(0, 10, 0, 0)));

            _descriptionHost.Children.Clear();
            _descriptionHost.Children.Add(inner);

            if (_formatStyleBox is not null)
                SyncFormatStyleBox(ruleType);
            UpdateColorScaleMidpointState();
        }

        private void SyncFormatStyleBox(string ruleType)
        {
            if (_formatStyleBox is null)
                return;

            _ignoreFormatStyleChange = true;
            _formatStyleBox.SelectedItem = UiText.Get(
                ConditionalFormatDialogCatalog.FormatStyleKeyForRuleType(
                    ruleType,
                    _colorScaleUseThreeColorBox.IsChecked == true));
            _ignoreFormatStyleChange = false;
        }

        private void FormatStyleBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_ignoreFormatStyleChange || sender is not ComboBox { SelectedItem: string label })
                return;

            var styleKey = ConditionalFormatDialogCatalog.FormatStyleOptions
                .FirstOrDefault(option => UiText.Get(option.LabelKey) == label)?.LabelKey;
            var newType = ConditionalFormatDialogCatalog.RuleTypeForFormatStyleKey(styleKey);
            RefreshRuleDescription(newType);
            if (ConditionalFormatDialogCatalog.UseThreeColorScaleForFormatStyleKey(styleKey))
                _colorScaleUseThreeColorBox.IsChecked = true;
            SyncFormatStyleBox(newType);
        }

        private void BuildDataBarEditor(StackPanel inner)
        {
            inner.Children.Add(Label("ConditionalFormatDialog_MinimumTypeLabel", _dataBarMinTypeBox));
            inner.Children.Add(_dataBarMinTypeBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MinimumValueLabel", _dataBarMinValueBox));
            inner.Children.Add(_dataBarMinValueBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MaximumTypeLabel", _dataBarMaxTypeBox));
            inner.Children.Add(_dataBarMaxTypeBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MaximumValueLabel", _dataBarMaxValueBox));
            inner.Children.Add(_dataBarMaxValueBox);
            inner.Children.Add(_dataBarShowValueBox);
            inner.Children.Add(_dataBarGradientBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MinimumBarLengthLabel", _dataBarMinLengthBox));
            inner.Children.Add(_dataBarMinLengthBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MaximumBarLengthLabel", _dataBarMaxLengthBox));
            inner.Children.Add(_dataBarMaxLengthBox);
            inner.Children.Add(_dataBarBorderBox);
            inner.Children.Add(Label("ConditionalFormatDialog_AxisPositionLabel", _dataBarAxisPositionBox));
            inner.Children.Add(_dataBarAxisPositionBox);
            inner.Children.Add(Label("ConditionalFormatDialog_AxisColorLabel", _dataBarAxisColorBox));
            inner.Children.Add(DockedPickerRow(_dataBarAxisColorBox, _dataBarAxisColorButton, new Thickness(0, 0, 0, 8)));
            inner.Children.Add(Label("ConditionalFormatDialog_NegativeBarColorLabel", _dataBarNegativeFillColorBox));
            inner.Children.Add(DockedPickerRow(_dataBarNegativeFillColorBox, _dataBarNegativeFillColorButton, new Thickness(0, 0, 0, 8)));
            inner.Children.Add(Label("ConditionalFormatDialog_NegativeBorderColorLabel", _dataBarNegativeBorderColorBox));
            inner.Children.Add(DockedPickerRow(_dataBarNegativeBorderColorBox, _dataBarNegativeBorderColorButton, new Thickness(0, 0, 0, 8)));
            ResetValueInputs();
        }

        private void BuildColorScaleEditor(StackPanel inner)
        {
            inner.Children.Add(Label("ConditionalFormatDialog_MinimumTypeLabel", _colorScaleMinTypeBox));
            inner.Children.Add(_colorScaleMinTypeBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MinimumValueLabel", _colorScaleMinValueBox));
            inner.Children.Add(_colorScaleMinValueBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MinimumColorLabel", _colorScaleMinColorBox));
            inner.Children.Add(DockedPickerRow(_colorScaleMinColorBox, _colorScaleMinColorButton));
            inner.Children.Add(_colorScaleUseThreeColorBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MidpointTypeLabel", _colorScaleMidTypeBox));
            inner.Children.Add(_colorScaleMidTypeBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MidpointValueLabel", _colorScaleMidValueBox));
            inner.Children.Add(_colorScaleMidValueBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MidpointColorLabel", _colorScaleMidColorBox));
            inner.Children.Add(DockedPickerRow(_colorScaleMidColorBox, _colorScaleMidColorButton));
            inner.Children.Add(Label("ConditionalFormatDialog_MaximumTypeLabel", _colorScaleMaxTypeBox));
            inner.Children.Add(_colorScaleMaxTypeBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MaximumValueLabel", _colorScaleMaxValueBox));
            inner.Children.Add(_colorScaleMaxValueBox);
            inner.Children.Add(Label("ConditionalFormatDialog_MaximumColorLabel", _colorScaleMaxColorBox));
            inner.Children.Add(DockedPickerRow(_colorScaleMaxColorBox, _colorScaleMaxColorButton));
            ResetValueInputs();
        }

        private void BuildIconSetEditor(StackPanel inner)
        {
            _iconSetStyleBox.SelectionChanged += (_, _) => BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string);
            _iconSetThresholdPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            BuildIconSetThresholdPanel(_iconSetStyleBox.SelectedItem as string ?? IconSetStyles[0]);
            inner.Children.Add(Label("ConditionalFormatDialog_IconSetLabel", _iconSetStyleBox));
            inner.Children.Add(_iconSetStyleBox);
            inner.Children.Add(_iconSetShowValueBox);
            inner.Children.Add(_iconSetReverseBox);
            inner.Children.Add(new TextBlock
            {
                Text = UiText.Get("ConditionalFormatDialog_ThresholdsHeader"),
                Margin = new Thickness(0, 4, 0, 2),
            });
            inner.Children.Add(_iconSetThresholdPanel);
            ResetValueInputs();
        }

        private void AddContainsShellEditor(StackPanel inner, string ruleType)
        {
            _conditionKindBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatRuleTypeBox");
            _conditionKindBox.ItemsSource = Localized(ConditionalFormatDialogCatalog.ConditionKindOptions);
            _conditionKindBox.SelectedItem = UiText.Get(ConditionalFormatDialogCatalog.ConditionKindKeyForRuleType(ruleType));
            AutomationProperties.SetName(_conditionKindBox, UiText.CreateAutomationName(UiText.Get("ConditionalFormat_RuleTypeLabel")));
            _conditionKindBox.SelectionChanged += ConditionKindBox_SelectionChanged;
            inner.Children.Add(Label("ConditionalFormatDialog_FormatOnlyCellsWithLabel", _conditionKindBox));
            inner.Children.Add(_conditionKindBox);

            ResetValueInputs();
            _value2Label = Label("ConditionalFormatDialog_MaximumLabel", _value2Box);
            _cellValueOperatorBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatOperatorBox");
            _cellValueOperatorBox.ItemsSource = LocalizedWithRuleType(ConditionalFormatDialogCatalog.CellValueOperatorOptions)
                .Select(item => item.Label)
                .ToArray();
            _specificTextOperatorBox = NewCombo(new Thickness(0, 4, 0, 8), "ConditionalFormatTextOperatorBox");
            _specificTextOperatorBox.ItemsSource = LocalizedWithRuleType(ConditionalFormatDialogCatalog.SpecificTextOperatorOptions)
                .Select(item => item.Label)
                .ToArray();

            var kind = _conditionKindBox.SelectedItem as string;
            if (kind == UiText.Get("ConditionalFormatDialog_ConditionKind_CellValue"))
            {
                _cellValueOperatorBox.SelectedItem = LabelForRuleType(
                    LocalizedWithRuleType(ConditionalFormatDialogCatalog.CellValueOperatorOptions),
                    ruleType,
                    "ConditionalFormatDialog_CellValueOperator_GreaterThan");
                _cellValueOperatorBox.SelectionChanged += CellValueOperatorBox_SelectionChanged;
                inner.Children.Add(Label("ConditionalFormatDialog_OperatorLabel", _cellValueOperatorBox));
                inner.Children.Add(_cellValueOperatorBox);
                var isBetween = ruleType is ConditionalFormatDialogCatalog.BetweenRule or ConditionalFormatDialogCatalog.NotBetweenRule;
                inner.Children.Add(Label(
                    isBetween ? "ConditionalFormatDialog_MinimumLabel" : "ConditionalFormatDialog_ValueLabel",
                    _value1Box));
                inner.Children.Add(_value1Box);
                if (isBetween)
                {
                    inner.Children.Add(_value2Label);
                    inner.Children.Add(_value2Box);
                }
            }
            else if (kind == UiText.Get("ConditionalFormatDialog_ConditionKind_SpecificText"))
            {
                _value1Box = NewTextBox(new Thickness(0, 4, 0, 8), "ConditionalFormatTextBox");
                _specificTextOperatorBox.SelectedItem = LabelForRuleType(
                    LocalizedWithRuleType(ConditionalFormatDialogCatalog.SpecificTextOperatorOptions),
                    ruleType,
                    "ConditionalFormatDialog_TextOperator_Containing");
                _specificTextOperatorBox.SelectionChanged += SpecificTextOperatorBox_SelectionChanged;
                inner.Children.Add(Label("ConditionalFormatDialog_TextOperatorLabel", _specificTextOperatorBox));
                inner.Children.Add(_specificTextOperatorBox);
                inner.Children.Add(Label("ConditionalFormatDialog_TextLabel", _value1Box));
                inner.Children.Add(_value1Box);
            }
            else if (kind == UiText.Get("ConditionalFormatDialog_ConditionKind_DatesOccurring"))
            {
                _dateOccurringPeriodBox = CreateDateOccurringPeriodBox();
                inner.Children.Add(Label("ConditionalFormatDialog_DatePeriodLabel", _dateOccurringPeriodBox));
                inner.Children.Add(_dateOccurringPeriodBox);
            }
        }

        private static string LabelForRuleType(
            (string Label, string RuleType)[] items,
            string ruleType,
            string fallbackKey)
        {
            foreach (var item in items)
            {
                if (item.RuleType == ruleType)
                    return item.Label;
            }

            return UiText.Get(fallbackKey);
        }

        private void ConditionKindBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox { SelectedItem: string label })
                return;

            var key = ConditionalFormatDialogCatalog.ConditionKindOptions
                .FirstOrDefault(option => UiText.Get(option.LabelKey) == label)?.LabelKey;
            var newType = ConditionalFormatDialogCatalog.DefaultRuleTypeForConditionKindKey(key);
            if (newType != _ruleType)
                RefreshRuleDescription(newType);
        }

        private void CellValueOperatorBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: string label } &&
                RuleTypeForOperatorLabel(LocalizedWithRuleType(ConditionalFormatDialogCatalog.CellValueOperatorOptions), label) is { } ruleType &&
                ruleType != _ruleType)
                RefreshRuleDescription(ruleType);
        }

        private void SpecificTextOperatorBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: string label } &&
                RuleTypeForOperatorLabel(LocalizedWithRuleType(ConditionalFormatDialogCatalog.SpecificTextOperatorOptions), label) is { } ruleType &&
                ruleType != _ruleType)
                RefreshRuleDescription(ruleType);
        }

        private static string? RuleTypeForOperatorLabel((string Label, string RuleType)[] labels, string label)
        {
            foreach (var item in labels)
            {
                if (item.Label == label)
                    return item.RuleType;
            }

            return null;
        }

        // ── Visual preview (WPF ConditionalFormatDialog.Preview) ───────────────────

        private static void AddVisualPreview(Panel panel, string ruleType)
        {
            panel.Children.Add(new TextBlock
            {
                Text = UiText.Get("ConditionalFormatDialog_PreviewHeader"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 4, 0, 6),
            });

            panel.Children.Add(ruleType switch
            {
                "Color Scale" => BuildColorScalePreview(),
                "Icon Set" => BuildIconSetPreview(),
                _ => BuildDataBarPreview(),
            });
        }

        private static Border BuildDataBarPreview()
        {
            var preview = new Border
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 12),
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(1),
                Child = new AvaloniaGrid
                {
                    Children =
                    {
                        new Border
                        {
                            Width = 150,
                            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                            Background = new LinearGradientBrush
                            {
                                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                                GradientStops =
                                {
                                    new GradientStop(Color.FromRgb(91, 155, 213), 0),
                                    new GradientStop(Color.FromRgb(189, 215, 238), 1),
                                },
                            },
                        },
                        new TextBlock
                        {
                            Text = "123",
                            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
                            VerticalAlignment = AvaloniaVerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 8, 0),
                        },
                    },
                },
            };
            AutomationProperties.SetAutomationId(preview, "DataBarPreview");
            return preview;
        }

        private static Border BuildColorScalePreview()
        {
            var preview = new Border
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 12),
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(99, 190, 123), 0),
                        new GradientStop(Color.FromRgb(255, 235, 132), 0.5),
                        new GradientStop(Color.FromRgb(248, 105, 107), 1),
                    },
                },
            };
            AutomationProperties.SetAutomationId(preview, "ColorScalePreview");
            return preview;
        }

        private static StackPanel BuildIconSetPreview()
        {
            var preview = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12),
                Children =
                {
                    new TextBlock { Text = "▲", Foreground = Brushes.Green, FontSize = 18, Margin = new Thickness(0, 0, 10, 0) },
                    new TextBlock { Text = "▶", Foreground = Brushes.Goldenrod, FontSize = 18, Margin = new Thickness(0, 0, 10, 0) },
                    new TextBlock { Text = "▼", Foreground = Brushes.Red, FontSize = 18 },
                },
            };
            AutomationProperties.SetAutomationId(preview, "IconSetPreview");
            return preview;
        }

        // ── Colour pickers ─────────────────────────────────────────────────────────

        private Button CreateColorScaleColorButton(TextBox colorBox, string tooltipKey, string automationId)
        {
            var button = NewPickerButton(tooltipKey, new Thickness(6, 4, 0, 8), automationId);
            button.Click += (_, _) => _owner.RunGuarded(async () =>
            {
                var choice = await _owner.ShowColorPickerDialogAsync(
                    UiText.Get("ColorPicker_SelectColor"),
                    TryParseColorBox(colorBox),
                    allowNoColor: false,
                    owner: _dialog);
                if (choice is { Color: { } selected })
                    colorBox.Text = FormatRgb(new RgbColor(selected.R, selected.G, selected.B));
            });
            return button;
        }

        private Button CreateOptionalColorButton(TextBox colorBox, string tooltipKey, string automationId)
        {
            var button = NewPickerButton(tooltipKey, new Thickness(6, 4, 0, 8), automationId);
            button.Click += (_, _) => _owner.RunGuarded(async () =>
            {
                var choice = await _owner.ShowColorPickerDialogAsync(
                    UiText.Get("ColorPicker_SelectColor"),
                    TryParseColorBox(colorBox),
                    allowNoColor: true,
                    owner: _dialog);
                if (choice is not { } picked)
                    return;

                colorBox.Text = picked.Color is { } selected
                    ? FormatRgb(new RgbColor(selected.R, selected.G, selected.B))
                    : string.Empty;
            });
            return button;
        }

        private static CellColor? TryParseColorBox(TextBox colorBox) =>
            ColorInputParser.TryParseRgbColorText(colorBox.Text ?? string.Empty, out var parsed) ? parsed : null;

        private async Task FormatButton_ClickAsync()
        {
            var preset = SelectedColorPreset();
            var initial = _customFormatStyle?.FillColor
                ?? new CellColor(preset.FillColor.R, preset.FillColor.G, preset.FillColor.B);
            var choice = await _owner.ShowColorPickerDialogAsync(
                UiText.Get("ColorPicker_SelectColor"),
                initial,
                allowNoColor: false,
                owner: _dialog);
            if (choice is not { Color: { } color })
                return;

            _customFormatStyle = new CellStyle { FillColor = color };
            _colorBox.SelectedItem = CustomFormatLabel;
        }

        private void SelectColor(CellColor color)
        {
            for (var i = 0; i < ColorOptions.Count; i++)
            {
                if (ColorOptions[i].FillColor == color)
                {
                    _colorBox.SelectedIndex = i;
                    _customFormatStyle = null;
                    break;
                }
            }

            if (_colorBox.SelectedIndex < 0 || ColorOptions[_colorBox.SelectedIndex].FillColor != color)
            {
                _customFormatStyle = new CellStyle { FillColor = color };
                _colorBox.SelectedItem = CustomFormatLabel;
            }
        }

        private ConditionalFormatDialogColorPreset SelectedColorPreset()
        {
            var index = _colorBox.SelectedIndex < 0 ? 0 : _colorBox.SelectedIndex;
            return ColorOptions[index];
        }

        private CellColor SelectedDataBarColor(CellColor fallback) =>
            _colorBox.SelectedItem as string == CustomFormatLabel && _customFormatStyle?.FillColor is { } custom
                ? custom
                : fallback;

        private CellStyle BuildSelectedCellStyle()
        {
            if (_colorBox.SelectedItem as string == CustomFormatLabel && _customFormatStyle is not null)
                return _customFormatStyle.Clone();

            return SelectedColorPreset().ToCellStyle();
        }

        private void UpdateColorScaleMidpointState()
        {
            var enabled = _colorScaleUseThreeColorBox.IsChecked == true;
            _colorScaleMidTypeBox.IsEnabled = enabled;
            _colorScaleMidValueBox.IsEnabled = enabled;
            _colorScaleMidColorBox.IsEnabled = enabled;
            _colorScaleMidColorButton.IsEnabled = enabled;
        }

        // ── Icon-set thresholds (WPF ConditionalFormatDialog.IconSets) ──────────────

        private static string[] BuildIconOverrideChoices()
        {
            var choices = new List<string>
            {
                UiText.Get("ConditionalFormatDialog_IconOverride_Default"),
                UiText.Get("ConditionalFormatDialog_IconOverride_NoIcon"),
            };
            foreach (var option in ConditionalFormatIconSetCatalog.GalleryOptions)
            {
                var label = UiText.Get(option.LabelKey);
                for (var i = 0; i < option.IconCount; i++)
                    choices.Add($"{label} {i + 1}|{option.Style}|{i}");
            }

            return choices.ToArray();
        }

        private static string IconOverrideToChoice(CfIconOverride? ovr)
        {
            if (ovr is null)
                return UiText.Get("ConditionalFormatDialog_IconOverride_Default");
            if (string.Equals(ovr.IconSet, "NoIcons", StringComparison.OrdinalIgnoreCase))
                return UiText.Get("ConditionalFormatDialog_IconOverride_NoIcon");

            var option = ConditionalFormatIconSetCatalog.GalleryOptions
                .FirstOrDefault(candidate => string.Equals(candidate.Style, ovr.IconSet, StringComparison.Ordinal));
            if (option is null)
                return UiText.Get("ConditionalFormatDialog_IconOverride_Default");

            return $"{UiText.Get(option.LabelKey)} {ovr.IconId + 1}|{option.Style}|{ovr.IconId}";
        }

        private static CfIconOverride? ChoiceToIconOverride(string? choice)
        {
            if (choice is null || choice == UiText.Get("ConditionalFormatDialog_IconOverride_Default"))
                return null;
            if (choice == UiText.Get("ConditionalFormatDialog_IconOverride_NoIcon"))
                return new CfIconOverride("NoIcons", 0);

            var parts = choice.Split('|');
            if (parts.Length == 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iconId))
                return new CfIconOverride(parts[1], iconId);
            return null;
        }

        private void BuildIconSetThresholdPanel(
            string? style,
            IReadOnlyList<CfThresholdModel>? existing = null,
            IReadOnlyList<CfIconOverride>? existingOverrides = null)
        {
            _iconSetThresholdPanel.Children.Clear();
            _iconSetThresholdRows.Clear();

            var count = ConditionalFormatIconSetCatalog.GetIconCount(style);
            var defaults = ConditionalFormatIconSetCatalog.CreateThresholds(style);

            for (var i = 0; i < count; i++)
            {
                var threshold = existing is not null && i < existing.Count ? existing[i] : defaults[i];

                var typeBox = NewCombo(new Thickness(6, 0, 6, 0), $"ConditionalFormatIconThresholdTypeBox{i + 1}");
                typeBox.Width = 100;
                typeBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
                typeBox.ItemsSource = ClassicThresholdTypes;
                typeBox.SelectedItem = threshold.Type;
                if (typeBox.SelectedIndex < 0)
                    typeBox.SelectedIndex = 0;

                var valueBox = NewTextBox(new Thickness(0), $"ConditionalFormatIconThresholdValueBox{i + 1}", threshold.Value ?? string.Empty);
                valueBox.Width = 80;
                valueBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
                valueBox.VerticalAlignment = AvaloniaVerticalAlignment.Center;

                var existingOverride = existingOverrides is not null && i < existingOverrides.Count
                    ? existingOverrides[i]
                    : null;
                var overrideBox = NewCombo(new Thickness(10, 0, 0, 0), $"ConditionalFormatIconThresholdOverrideBox{i + 1}");
                overrideBox.Width = 160;
                overrideBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
                overrideBox.ItemsSource = IconOverrideChoices;
                overrideBox.SelectedItem = IconOverrideToChoice(existingOverride);
                if (overrideBox.SelectedIndex < 0)
                    overrideBox.SelectedIndex = 0;

                _iconSetThresholdPanel.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 2),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = UiText.Format("ConditionalFormatDialog_IconThresholdTextFormat", i + 1),
                            Width = 110,
                            VerticalAlignment = AvaloniaVerticalAlignment.Center,
                        },
                        typeBox,
                        valueBox,
                        overrideBox,
                    },
                });
                _iconSetThresholdRows.Add((typeBox, valueBox, overrideBox));
            }
        }

        // ── Edit (WPF ConditionalFormatDialog(ConditionalFormat existingRule)) ──────

        private void PopulateFromExistingRule(ConditionalFormat existingRule)
        {
            if (existingRule.RuleType == CfRuleType.Formula)
            {
                if (_formulaBox is not null)
                {
                    _formulaBox.Text = string.IsNullOrEmpty(existingRule.FormulaText)
                        ? "="
                        : $"={existingRule.FormulaText}";
                }
            }
            else
            {
                _value1Box.Text = existingRule.Value1 ?? string.Empty;
                _value2Box.Text = existingRule.Value2 ?? string.Empty;
                if (existingRule.RuleType == CfRuleType.IconSet)
                {
                    var style = string.IsNullOrWhiteSpace(existingRule.IconSetStyle)
                        ? IconSetStyles[0]
                        : existingRule.IconSetStyle;
                    if (!IconSetStyles.Contains(style))
                    {
                        var items = IconSetStyles.ToList();
                        items.Add(style);
                        _iconSetStyleBox.ItemsSource = items;
                    }

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
                    _dataBarMinValueBox.Text = existingRule.DataBarMinThresholdValue ?? string.Empty;
                    _dataBarMaxTypeBox.SelectedItem = existingRule.DataBarMaxThresholdType;
                    _dataBarMaxValueBox.Text = existingRule.DataBarMaxThresholdValue ?? string.Empty;
                    _dataBarShowValueBox.IsChecked = !existingRule.DataBarShowValue;
                    _dataBarGradientBox.IsChecked = existingRule.DataBarGradient;
                    _dataBarMinLengthBox.Text = existingRule.DataBarMinLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                    _dataBarMaxLengthBox.Text = existingRule.DataBarMaxLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                    _dataBarBorderBox.IsChecked = existingRule.DataBarBorder;
                    _dataBarAxisPositionBox.SelectedItem = AxisPositionToLabel(existingRule.DataBarAxisPosition);
                    _dataBarAxisColorBox.Text = existingRule.DataBarAxisColor is { } axis ? FormatRgb(axis) : string.Empty;
                    _dataBarNegativeFillColorBox.Text = existingRule.DataBarNegativeFillColor is { } negativeFill ? FormatRgb(negativeFill) : string.Empty;
                    _dataBarNegativeBorderColorBox.Text = existingRule.DataBarNegativeBorderColor is { } negativeBorder ? FormatRgb(negativeBorder) : string.Empty;
                }
                else if (existingRule.RuleType == CfRuleType.ColorScale)
                {
                    _colorScaleMinTypeBox.SelectedItem = existingRule.MinThresholdType;
                    _colorScaleMinValueBox.Text = existingRule.MinThresholdValue ?? string.Empty;
                    _colorScaleMinColorBox.Text = FormatRgb(existingRule.MinColor);
                    _colorScaleUseThreeColorBox.IsChecked = existingRule.UseThreeColorScale;
                    _colorScaleMidTypeBox.SelectedItem = existingRule.MidThresholdType;
                    _colorScaleMidValueBox.Text = existingRule.MidThresholdValue ?? string.Empty;
                    _colorScaleMidColorBox.Text = FormatRgb(existingRule.MidColor);
                    _colorScaleMaxTypeBox.SelectedItem = existingRule.MaxThresholdType;
                    _colorScaleMaxValueBox.Text = existingRule.MaxThresholdValue ?? string.Empty;
                    _colorScaleMaxColorBox.Text = FormatRgb(existingRule.MaxColor);
                    UpdateColorScaleMidpointState();
                }
                else if (existingRule.RuleType is CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith)
                {
                    _value1Box.Text = existingRule.TextRuleText ?? string.Empty;
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
                    _topBottomRankBox.Text = existingRule.TopBottomRank.ToString(CultureInfo.InvariantCulture);
                }
            }

            // Pre-select the closest color option from FormatIfTrue.FillColor.
            if (existingRule.RuleType == CfRuleType.DataBar)
            {
                SelectColor(new CellColor(
                    existingRule.DataBarColor.R,
                    existingRule.DataBarColor.G,
                    existingRule.DataBarColor.B));
            }
            else if (existingRule.FormatIfTrue?.FillColor is { } fillColor)
            {
                SelectColor(fillColor);
            }
        }

        // ── OK (WPF ConditionalFormatDialog.Result) ────────────────────────────────

        private async Task OkClickAsync()
        {
            var input = CreateRuleInputFromControls();
            var validation = ConditionalFormatRuleSchema.ForRuleType(input.RuleType).Validate(input);
            if (!validation.IsValid)
            {
                await ShowValidationWarningAsync(validation);
                return;
            }

            _result = ConditionalFormatRuleBuilder.Build(
                input,
                _range,
                id: _existingId,
                customFormat: ConditionalFormatDialogCatalog.IsVisualRuleType(_ruleType) ? null : BuildSelectedCellStyle(),
                existingRule: _existingRule);
            _dialog.Close();
        }

        private CfRuleInput CreateRuleInputFromControls()
        {
            var ruleType = ConditionalFormatDialogPlanner.ModelRuleTypeForDialogRuleType(
                _ruleType,
                DuplicateValuesRuleType(_duplicateValuesKindBox.SelectedItem as string) == CfRuleType.UniqueValues);

            var input = new CfRuleInput
            {
                RuleType = ruleType,
                Operator = ConditionalFormatDialogPlanner.OperatorForDialogRuleType(_ruleType),
                IsTop = ConditionalFormatDialogCatalog.IsTopRuleType(_ruleType),
                IsPercent = ConditionalFormatDialogCatalog.IsTopBottomPercentRuleType(_ruleType),
            };

            return ruleType switch
            {
                CfRuleType.Formula => input with { Formula = _formulaBox?.Text },
                CfRuleType.CellValue => input with { Value1 = _value1Box.Text, Value2 = _value2Box.Text },
                CfRuleType.IconSet => input with
                {
                    IconSetStyle = _iconSetStyleBox.SelectedItem as string ?? ConditionalFormatIconSetCatalog.DefaultStyle,
                    IconSetShowValue = _iconSetShowValueBox.IsChecked == true,
                    IconSetReverse = _iconSetReverseBox.IsChecked == true,
                    IconSetThresholds = BuildIconSetThresholdInputs(),
                    IconOverrides = BuildIconOverrideInputs(),
                },
                CfRuleType.DataBar => input with
                {
                    DataBarColor = RgbColor.FromCellColor(SelectedDataBarColor(SelectedColorPreset().FillColor)),
                    DataBarMinType = SelectedThresholdType(_dataBarMinTypeBox, CfThresholdType.Min),
                    DataBarMinValue = _dataBarMinValueBox.Text,
                    DataBarMaxType = SelectedThresholdType(_dataBarMaxTypeBox, CfThresholdType.Max),
                    DataBarMaxValue = _dataBarMaxValueBox.Text,
                    DataBarShowValue = _dataBarShowValueBox.IsChecked != true,
                    DataBarGradient = _dataBarGradientBox.IsChecked == true,
                    DataBarMinLength = _dataBarMinLengthBox.Text,
                    DataBarMaxLength = _dataBarMaxLengthBox.Text,
                    DataBarBorder = _dataBarBorderBox.IsChecked == true,
                    DataBarAxisPosition = AxisPositionToXmlValue(_dataBarAxisPositionBox.SelectedItem as string),
                    DataBarAxisColor = ConditionalFormatInputParser.ParseOptionalRgbColor(_dataBarAxisColorBox.Text),
                    DataBarNegativeFillColor = ConditionalFormatInputParser.ParseOptionalRgbColor(_dataBarNegativeFillColorBox.Text),
                    DataBarNegativeBorderColor = ConditionalFormatInputParser.ParseOptionalRgbColor(_dataBarNegativeBorderColorBox.Text),
                },
                CfRuleType.ColorScale => input with
                {
                    ColorScaleMinType = SelectedThresholdType(_colorScaleMinTypeBox, CfThresholdType.Min),
                    ColorScaleMinValue = _colorScaleMinValueBox.Text,
                    MinColor = _colorScaleMinColorBox.Text,
                    UseThreeColorScale = _colorScaleUseThreeColorBox.IsChecked == true,
                    ColorScaleMidType = SelectedThresholdType(_colorScaleMidTypeBox, CfThresholdType.Percentile),
                    ColorScaleMidValue = _colorScaleMidValueBox.Text,
                    MidColor = _colorScaleMidColorBox.Text,
                    ColorScaleMaxType = SelectedThresholdType(_colorScaleMaxTypeBox, CfThresholdType.Max),
                    ColorScaleMaxValue = _colorScaleMaxValueBox.Text,
                    MaxColor = _colorScaleMaxColorBox.Text,
                },
                CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith =>
                    input with { Text = _value1Box.Text },
                CfRuleType.DateOccurring => input with { DatePeriod = DatePeriodValue(_dateOccurringPeriodBox.SelectedItem as string) },
                CfRuleType.Top10 => input with { Rank = _topBottomRankBox.Text },
                _ => input,
            };
        }

        private IReadOnlyList<CfThresholdModel>? BuildIconSetThresholdInputs()
        {
            if (_iconSetThresholdRows.Count == 0)
                return null;

            var thresholds = new List<CfThresholdModel>(_iconSetThresholdRows.Count);
            foreach (var (typeBox, valueBox, _) in _iconSetThresholdRows)
            {
                var type = typeBox.SelectedItem is CfThresholdType selected ? selected : CfThresholdType.Percent;
                thresholds.Add(new CfThresholdModel(type, ConditionalFormatInputParser.BlankToNull(valueBox.Text)));
            }

            return thresholds;
        }

        private IReadOnlyList<CfIconOverride?>? BuildIconOverrideInputs()
        {
            if (_iconSetThresholdRows.Count == 0)
                return null;

            var overrides = new List<CfIconOverride?>(_iconSetThresholdRows.Count);
            foreach (var (_, _, overrideBox) in _iconSetThresholdRows)
                overrides.Add(ChoiceToIconOverride(overrideBox.SelectedItem as string));

            return overrides;
        }

        private static CfThresholdType SelectedThresholdType(ComboBox comboBox, CfThresholdType fallback) =>
            comboBox.SelectedItem is CfThresholdType selected ? selected : fallback;

        private async Task ShowValidationWarningAsync(CfValidationResult validation)
        {
            var error = validation.Errors[0];
            var (messageKey, target) = error.Field switch
            {
                CfInputField.Formula => ("ConditionalFormatDialog_InvalidFormulaMessage", (TextBox?)_formulaBox),
                CfInputField.Value1 => ("ConditionalFormatDialog_InvalidValueMessage", _value1Box),
                CfInputField.Value2 => ("ConditionalFormatDialog_InvalidMaximumValueMessage", _value2Box),
                CfInputField.Text => ("ConditionalFormatDialog_InvalidTextMessage", _value1Box),
                CfInputField.Rank => ("ConditionalFormatDialog_InvalidRankOrPercentMessage", _topBottomRankBox),
                CfInputField.DataBarMinLength => ("ConditionalFormatDialog_InvalidMinimumBarLengthMessage", _dataBarMinLengthBox),
                CfInputField.DataBarMaxLength => ("ConditionalFormatDialog_InvalidMaximumBarLengthMessage", _dataBarMaxLengthBox),
                CfInputField.ColorScaleMinColor => ("ConditionalFormatDialog_InvalidMinimumColorMessage", _colorScaleMinColorBox),
                CfInputField.ColorScaleMidColor => ("ConditionalFormatDialog_InvalidMidpointColorMessage", _colorScaleMidColorBox),
                CfInputField.ColorScaleMaxColor => ("ConditionalFormatDialog_InvalidMaximumColorMessage", _colorScaleMaxColorBox),
                _ => ("ConditionalFormatDialog_InvalidValueMessage", null),
            };

            await AvaloniaUserMessageDialog.ShowWarningAsync(_dialog, UiText.Get(messageKey), _dialog.Title ?? string.Empty);
            if (target is not null)
                AvaloniaCompactDialogChrome.FocusAndSelect(target);
        }

        // ── Label <-> value maps (WPF ConditionalFormatDialog.Parsing) ──────────────

        private static string FormatRgb(RgbColor color) => ConditionalFormatInputParser.FormatRgb(color);

        private static string[] DataBarAxisPositionLabels() =>
            ConditionalFormatDialogCatalog.AxisPositionOptions
                .Select(option => UiText.Get(option.LabelKey))
                .ToArray();

        private static string? AxisPositionToXmlValue(string? label)
        {
            var key = ConditionalFormatDialogCatalog.AxisPositionOptions
                .FirstOrDefault(option => label == UiText.Get(option.LabelKey))?.LabelKey;
            return ConditionalFormatDialogCatalog.AxisPositionValueForKey(key);
        }

        private static string AxisPositionToLabel(string? xmlValue) =>
            UiText.Get(ConditionalFormatDialogCatalog.AxisPositionKeyForValue(xmlValue));

        private static CfRuleType DuplicateValuesRuleType(string? label) =>
            string.Equals(label, UiText.Get("ConditionalFormatDialog_DuplicateKind_Unique"), StringComparison.OrdinalIgnoreCase)
                ? CfRuleType.UniqueValues
                : CfRuleType.DuplicateValues;

        private static string DatePeriodValue(string? label)
        {
            var key = ConditionalFormatDialogCatalog.DatePeriodOptions
                .FirstOrDefault(option => label == UiText.Get(option.LabelKey))?.LabelKey;
            return ConditionalFormatDialogCatalog.DatePeriodValueForKey(key);
        }

        private static string DatePeriodLabel(string? value) =>
            UiText.Get(ConditionalFormatDialogCatalog.DatePeriodKeyForValue(value));

        // ── Validation / automation access ─────────────────────────────────────────

        private Control? FindControl(string automationId) =>
            _dialog.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);

        /// <summary>
        /// Selects the left-list entry that owns <paramref name="ruleType"/> (a WPF rule-type token such as
        /// "Data Bar") and rebuilds the description, like a user pick in the list or the dedicated dialogs.
        /// </summary>
        private void SelectRuleType(string ruleType)
        {
            _ignoreShellChange = true;
            _shellList.SelectedIndex = ShellIndexForRuleType(ruleType);
            _ignoreShellChange = false;
            if (ruleType != _ruleType)
                RefreshRuleDescription(ruleType);
        }
    }
}
