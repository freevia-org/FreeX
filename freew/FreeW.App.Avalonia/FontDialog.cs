using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Avalonia counterpart of the WPF Font dialog. The WPF surface is authoritative for layout and
/// fields; shared planners own all catalogs, initial state, parsing, and validation.
/// </summary>
public sealed class FontDialog : FreeWDialogWindow
{
    private static readonly FontDialogSurfaceSpec Surface = FontDialogPlanner.Surface;
    private static readonly FontDialogVisualMetrics Layout = FontDialogPlanner.VisualMetrics;
    private static AvaloniaCompactDialogChromeStyle DialogChromeStyle =>
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            ControlHeight = 20,
            TextBoxHeight = 18,
            ComboBoxHeight = 22,
            TabHeight = 20,
            ButtonHeight = 20,
            ForegroundBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)),
            FocusedInputBorderBrush = new SolidColorBrush(Color.FromRgb(0x56, 0x9D, 0xE5)),
            ButtonBorderBrush = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            DialogTabPaneBorderBrush = new SolidColorBrush(Color.FromRgb(0xAC, 0xAC, 0xAC)),
            DialogInactiveTabBorderBrush = new SolidColorBrush(Color.FromRgb(0xAC, 0xAC, 0xAC)),
            DialogInactiveTabBackgroundBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            RemoveFocusAdorner = true,
        };

    private readonly FontDialogSession _session;
    private readonly IUserMessageService _messageService;

    private readonly TextBox _familyBox;
    private readonly ComboBox _sizeBox;
    private readonly ComboBox _colorBox;
    private readonly CheckBox _boldChk = Check(Surface.Effect(FontDialogEffectKind.Bold));
    private readonly CheckBox _italicChk = Check(Surface.Effect(FontDialogEffectKind.Italic));
    private readonly CheckBox _underlineChk = Check(Surface.Effect(FontDialogEffectKind.Underline));
    private readonly CheckBox _strikeChk = Check(Surface.Effect(FontDialogEffectKind.Strikethrough));
    private readonly CheckBox _doubleStrikeChk = Check(Surface.Effect(FontDialogEffectKind.DoubleStrikethrough));
    private readonly CheckBox _hiddenChk = Check(Surface.Effect(FontDialogEffectKind.Hidden));
    private readonly CheckBox _smallCapsChk = Check(Surface.Effect(FontDialogEffectKind.SmallCaps));
    private readonly CheckBox _allCapsChk = Check(Surface.Effect(FontDialogEffectKind.AllCaps));
    private readonly CheckBox _superChk = Check(Surface.Effect(FontDialogEffectKind.Superscript));
    private readonly CheckBox _subChk = Check(Surface.Effect(FontDialogEffectKind.Subscript), trailingMargin: 0);
    private readonly TextBox _spacingBox;
    private readonly TextBox _kerningBox;
    private readonly TextBox _positionBox;
    private readonly ComboBox _ligatureBox;
    private readonly TextBox _stylisticBox;
    private readonly ComboBox _numberFormBox;
    private readonly ComboBox _numberSpacingBox;
    private readonly TextBlock _status = new();

    public FontDialog(RunFormatting current, IUserMessageService? messageService = null)
        : this(new FontDialogSelectionState(current), messageService)
    {
    }

    public FontDialog(
        FontDialogSelectionState selection,
        IUserMessageService? messageService = null)
        : base(DialogChromeStyle)
    {
        _session = FontDialogPlanner.CreateSession(selection, CultureInfo.CurrentCulture);
        _messageService = messageService ?? new AvaloniaUserMessageService(this);

        Title = Surface.Title;
        Width = Surface.WindowWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var state = _session.InitialState;

        _familyBox = TextBox(state.FontFamilyText ?? string.Empty, Surface.Field(FontDialogFieldKind.FontFamily).MinWidth);
        _sizeBox = Combo(
            FontDialogPlanner.SizeChoices.Select(choice => choice.Label),
            selectedIndex: -1,
            minWidth: Surface.Field(FontDialogFieldKind.FontSize).MinWidth,
            editable: true);
        _sizeBox.Text = state.FontSizeText;
        _colorBox = Combo(
            FontDialogPlanner.ColorChoices.Select(choice => choice.Label),
            state.ColorIndex,
            minWidth: Surface.Field(FontDialogFieldKind.Color).MinWidth);

        foreach (var spec in Surface.Effects)
        {
            var checkBox = EffectControlFor(spec.Kind);
            checkBox.IsChecked = state.EffectValue(spec.Kind);
            AutomationProperties.SetAutomationId(checkBox, spec.AutomationId);
        }
        ApplyCheckBoxChrome();

        _superChk.IsCheckedChanged += (_, _) =>
        {
            var alignment = _session.PlanVerticalAlignmentToggle(
                _superChk.IsChecked == true,
                _subChk.IsChecked == true,
                FontDialogVerticalAlignmentToggle.Superscript,
                _superChk.IsChecked);
            _superChk.IsChecked = alignment.Superscript;
            _subChk.IsChecked = alignment.Subscript;
        };
        _subChk.IsCheckedChanged += (_, _) =>
        {
            var alignment = _session.PlanVerticalAlignmentToggle(
                _superChk.IsChecked == true,
                _subChk.IsChecked == true,
                FontDialogVerticalAlignmentToggle.Subscript,
                _subChk.IsChecked);
            _superChk.IsChecked = alignment.Superscript;
            _subChk.IsChecked = alignment.Subscript;
        };

        _spacingBox = TextBox(state.CharacterSpacingText ?? string.Empty, Surface.Field(FontDialogFieldKind.CharacterSpacing).MinWidth);
        _kerningBox = TextBox(state.KerningMinSizeText ?? string.Empty, Surface.Field(FontDialogFieldKind.Kerning).MinWidth);
        _positionBox = TextBox(state.PositionText ?? string.Empty, Surface.Field(FontDialogFieldKind.Position).MinWidth);
        _ligatureBox = Combo(
            FontDialogPlanner.LigatureChoices.Select(choice => choice.Label),
            state.LigatureIndex,
            minWidth: Surface.Field(FontDialogFieldKind.Ligatures).MinWidth);
        _stylisticBox = TextBox(state.StylisticSetText ?? string.Empty, Surface.Field(FontDialogFieldKind.StylisticSet).MinWidth);
        ToolTip.SetTip(_stylisticBox, Surface.Field(FontDialogFieldKind.StylisticSet).ToolTip);
        _numberFormBox = Combo(
            FontDialogPlanner.NumberFormChoices.Select(choice => choice.Label),
            state.NumberFormIndex,
            minWidth: Surface.Field(FontDialogFieldKind.NumberForm).MinWidth);
        _numberSpacingBox = Combo(
            FontDialogPlanner.NumberSpacingChoices.Select(choice => choice.Label),
            state.NumberSpacingIndex,
            minWidth: Surface.Field(FontDialogFieldKind.NumberSpacing).MinWidth);

        var fieldControls = new Dictionary<FontDialogFieldKind, Control>
        {
            [FontDialogFieldKind.FontFamily] = _familyBox,
            [FontDialogFieldKind.FontSize] = _sizeBox,
            [FontDialogFieldKind.Color] = _colorBox,
            [FontDialogFieldKind.CharacterSpacing] = _spacingBox,
            [FontDialogFieldKind.Kerning] = _kerningBox,
            [FontDialogFieldKind.Position] = _positionBox,
            [FontDialogFieldKind.Ligatures] = _ligatureBox,
            [FontDialogFieldKind.StylisticSet] = _stylisticBox,
            [FontDialogFieldKind.NumberForm] = _numberFormBox,
            [FontDialogFieldKind.NumberSpacing] = _numberSpacingBox,
        };
        foreach (var spec in Surface.Fields)
            AutomationProperties.SetAutomationId(fieldControls[spec.Kind], spec.AutomationId);

        var fontPanel = new StackPanel { Margin = ToThickness(Layout.AvaloniaFontTabContentMargin) };
        foreach (var kind in Surface.Tabs.First(tab => tab.Kind == FontDialogTabKind.Font).Fields)
            AddField(fontPanel, Surface.Field(kind).Label, fieldControls[kind]);
        fontPanel.Children.Add(new TextBlock { Text = Surface.EffectsSectionLabel, Margin = ToThickness(Layout.AvaloniaEffectsLabelMargin) });
        var effects = new WrapPanel();
        foreach (var spec in Surface.Effects)
            effects.Children.Add(EffectControlFor(spec.Kind));
        fontPanel.Children.Add(effects);

        var advancedPanel = new StackPanel { Margin = ToThickness(Layout.AvaloniaAdvancedTabContentMargin) };
        foreach (var kind in Surface.Tabs.First(tab => tab.Kind == FontDialogTabKind.Advanced).Fields)
            AddField(advancedPanel, Surface.Field(kind).Label, fieldControls[kind]);

        var tabs = new TabControl { Margin = new Thickness(0) };
        var tabPanels = new Dictionary<FontDialogTabKind, Control>
        {
            [FontDialogTabKind.Font] = fontPanel,
            [FontDialogTabKind.Advanced] = advancedPanel,
        };
        foreach (var spec in Surface.Tabs)
        {
            var tab = new TabItem
            {
                Header = spec.Header,
                Content = new ScrollViewer
                {
                    Content = tabPanels[spec.Kind],
                    VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                },
            };
            AutomationProperties.SetAutomationId(tab, spec.AutomationId);
            tabs.Items.Add(tab);
        }
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            tabs,
            DialogChromeStyle,
            contentPaneMargin: ToThickness(Layout.AvaloniaTabPaneMargin));

        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _status,
            DialogChromeStyle,
            ToThickness(Layout.AvaloniaValidationMargin));
        var buttons = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            OnOk,
            () => Close(null),
            buttonWidth: Surface.ActionButtonWidth,
            margin: ToThickness(Layout.ActionRowMargin),
            style: DialogChromeStyle);

        var root = new StackPanel { Margin = ToThickness(Layout.AvaloniaRootMargin) };
        root.Children.Add(tabs);
        root.Children.Add(_status);
        root.Children.Add(buttons);
        Content = root;

        Opened += (_, _) =>
        {
            foreach (var combo in this.GetVisualDescendants().OfType<ComboBox>())
                AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
            foreach (var box in fieldControls.Values.OfType<TextBox>())
                AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
            ApplyCheckBoxChrome();
            _familyBox.Focus();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            Close(null);
            args.Handled = true;
        };
    }

    private async void OnOk()
    {
        _status.IsVisible = false;
        var acceptance = _session.PlanAcceptance(FontDialogPlanner.CaptureControlState(
            _familyBox.Text,
            _sizeBox.Text,
            _colorBox.SelectedIndex,
            _spacingBox.Text,
            _kerningBox.Text,
            _positionBox.Text,
            _ligatureBox.SelectedIndex,
            _stylisticBox.Text,
            _numberFormBox.SelectedIndex,
            _numberSpacingBox.SelectedIndex,
            kind => EffectControlFor(kind).IsChecked));

        if (!acceptance.IsAccepted)
        {
            await _messageService.ShowWarningAsync(acceptance.ErrorMessage ?? string.Empty);
            return;
        }

        Close(acceptance.Result);
    }

    public static void ApplyResult(DocumentView editor, FontDialogWorkflowResult result, RunFormatting original)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        var session = FontDialogPlanner.CreateSession(original, CultureInfo.CurrentCulture);
        ExecuteApplyPlan(editor, session.BuildApplyPlan(result));
    }

    private static void ExecuteApplyPlan(DocumentView editor, FontDialogApplyPlan plan)
    {
        editor.BeginFontUndoGroup();
        try
        {
            foreach (var command in plan.Commands)
            {
                switch (command)
                {
                    case FontDialogApplyCommand.SetFamily setFamily:
                        editor.SetSelectionFontFamily(setFamily.Family ?? string.Empty);
                        break;
                    case FontDialogApplyCommand.SetSize setSize:
                        editor.SetSelectionFontSize(setSize.SizePt);
                        break;
                    case FontDialogApplyCommand.Toggle toggle:
                        ExecuteToggle(editor, toggle.Target);
                        break;
                    case FontDialogApplyCommand.SetColor setColor:
                        editor.SetFontColor(setColor.ColorHex);
                        break;
                    case FontDialogApplyCommand.SetHighlight setHighlight:
                        editor.SetHighlightColor(setHighlight.ColorHex);
                        break;
                    case FontDialogApplyCommand.ApplyAdvanced applyAdvanced:
                        editor.ApplyAdvancedFontFormatting(applyAdvanced.Formatting);
                        break;
                }
            }
        }
        finally
        {
            editor.CommitFontUndoGroup(plan.UndoLabel);
        }
    }

    private static void ExecuteToggle(DocumentView editor, FontDialogToggleCommand target)
    {
        switch (target)
        {
            case FontDialogToggleCommand.Bold:
                editor.ToggleBold();
                break;
            case FontDialogToggleCommand.Italic:
                editor.ToggleItalic();
                break;
            case FontDialogToggleCommand.Underline:
                editor.ToggleUnderline();
                break;
            case FontDialogToggleCommand.Strikethrough:
                editor.ToggleStrikethrough();
                break;
            case FontDialogToggleCommand.DoubleStrikethrough:
                editor.ToggleDoubleStrikethrough();
                break;
            case FontDialogToggleCommand.Hidden:
                editor.ToggleHidden();
                break;
            case FontDialogToggleCommand.Superscript:
                editor.ToggleSuperscript();
                break;
            case FontDialogToggleCommand.Subscript:
                editor.ToggleSubscript();
                break;
            case FontDialogToggleCommand.SmallCaps:
                editor.ToggleSmallCaps();
                break;
            case FontDialogToggleCommand.AllCaps:
                editor.ToggleAllCaps();
                break;
        }
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var selection = editor.GetSelectionFormatting();
        var dialog = new FontDialog(selection);
        var result = await dialog.ShowDialog<FontDialogWorkflowResult?>(owner);
        if (result is not null)
            ExecuteApplyPlan(editor, dialog._session.BuildApplyPlan(result));
    }

    private static void AddField(Panel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = ToThickness(Layout.FieldLabelMargin) });
        control.Margin = ToThickness(Layout.FieldControlMargin);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(control);
    }

    private static TextBox TextBox(string text, double minWidth)
    {
        var box = new TextBox { Text = text, MinWidth = minWidth };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static ComboBox Combo(
        IEnumerable<string> items,
        int selectedIndex,
        double minWidth,
        bool editable = false)
    {
        var combo = new ComboBox
        {
            ItemsSource = items.ToArray(),
            SelectedIndex = selectedIndex,
            MinWidth = minWidth,
            IsEditable = editable,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        return combo;
    }

    private static CheckBox Check(FontDialogEffectSpec spec, double? trailingMargin = null) => new()
    {
        Content = spec.Label,
        IsThreeState = spec.IsThreeState,
        Margin = new Thickness(
            0,
            0,
            trailingMargin ?? Layout.EffectTrailingMargin,
            Layout.EffectBottomMargin),
    };

    private static Thickness ToThickness(FontDialogThickness value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);

    private CheckBox EffectControlFor(FontDialogEffectKind kind) => kind switch
    {
        FontDialogEffectKind.Bold => _boldChk,
        FontDialogEffectKind.Italic => _italicChk,
        FontDialogEffectKind.Underline => _underlineChk,
        FontDialogEffectKind.Strikethrough => _strikeChk,
        FontDialogEffectKind.DoubleStrikethrough => _doubleStrikeChk,
        FontDialogEffectKind.Hidden => _hiddenChk,
        FontDialogEffectKind.SmallCaps => _smallCapsChk,
        FontDialogEffectKind.AllCaps => _allCapsChk,
        FontDialogEffectKind.Superscript => _superChk,
        FontDialogEffectKind.Subscript => _subChk,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private void ApplyCheckBoxChrome()
    {
        foreach (var spec in Surface.Effects)
            FontParagraphDialogChrome.ApplyCheckBox(EffectControlFor(spec.Kind), DialogChromeStyle);
    }

}
