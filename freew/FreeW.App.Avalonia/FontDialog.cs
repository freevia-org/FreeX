using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            FontFamily = new FontFamily("Segoe UI"),
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

    private readonly TextBox _familyBox;
    private readonly ComboBox _sizeBox;
    private readonly ComboBox _colorBox;
    private readonly CheckBox _boldChk = Check("Bold", threeState: true);
    private readonly CheckBox _italicChk = Check("Italic", threeState: true);
    private readonly CheckBox _underlineChk = Check("Underline", threeState: true);
    private readonly CheckBox _strikeChk = Check("Strikethrough", threeState: true);
    private readonly CheckBox _doubleStrikeChk = Check("Double strikethrough", threeState: true);
    private readonly CheckBox _hiddenChk = Check("Hidden", threeState: true);
    private readonly CheckBox _smallCapsChk = Check("Small Caps");
    private readonly CheckBox _allCapsChk = Check("All Caps");
    private readonly CheckBox _superChk = Check("Superscript");
    private readonly CheckBox _subChk = Check("Subscript", trailingMargin: 0);
    private readonly TextBox _spacingBox;
    private readonly TextBox _kerningBox;
    private readonly TextBox _positionBox;
    private readonly ComboBox _ligatureBox;
    private readonly TextBox _stylisticBox;
    private readonly ComboBox _numberFormBox;
    private readonly ComboBox _numberSpacingBox;
    private readonly TextBlock _status = new();

    public FontDialog(RunFormatting current)
        : this(new DocumentView.SelectionFormatting(current, ParagraphFormatting.Default))
    {
    }

    public FontDialog(DocumentView.SelectionFormatting selection)
    {
        _session = FontDialogPlanner.CreateSession(
            new FontDialogSelectionState(
                selection.Run,
                selection.BoldIndeterminate,
                selection.ItalicIndeterminate,
                selection.UnderlineIndeterminate,
                selection.StrikethroughIndeterminate,
                selection.FamilyIndeterminate,
                selection.SizeIndeterminate,
                selection.DoubleStrikethroughIndeterminate,
                selection.HiddenIndeterminate),
            CultureInfo.CurrentCulture);

        Title = "Font";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);

        var state = _session.InitialState;

        _familyBox = TextBox(state.FontFamilyText ?? string.Empty, minWidth: 200);
        _sizeBox = Combo(
            FontDialogPlanner.SizeChoices.Select(choice => choice.Label),
            selectedIndex: -1,
            minWidth: 80,
            editable: true);
        _sizeBox.Text = state.FontSizeText;
        _colorBox = Combo(
            FontDialogPlanner.ColorChoices.Select(choice => choice.Label),
            state.ColorIndex,
            minWidth: 180);

        _boldChk.IsChecked = state.Bold;
        _italicChk.IsChecked = state.Italic;
        _underlineChk.IsChecked = state.Underline;
        _strikeChk.IsChecked = state.Strikethrough;
        _doubleStrikeChk.IsChecked = state.DoubleStrikethrough;
        _hiddenChk.IsChecked = state.Hidden;
        _smallCapsChk.IsChecked = state.SmallCaps;
        _allCapsChk.IsChecked = state.AllCaps;
        _superChk.IsChecked = state.Superscript;
        _subChk.IsChecked = state.Subscript;
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

        _spacingBox = TextBox(state.CharacterSpacingText ?? string.Empty, minWidth: 100);
        _kerningBox = TextBox(state.KerningMinSizeText ?? string.Empty, minWidth: 100);
        _positionBox = TextBox(state.PositionText ?? string.Empty, minWidth: 100);
        _ligatureBox = Combo(
            FontDialogPlanner.LigatureChoices.Select(choice => choice.Label),
            state.LigatureIndex,
            minWidth: 180);
        _stylisticBox = TextBox(state.StylisticSetText ?? string.Empty, minWidth: 100);
        ToolTip.SetTip(_stylisticBox, FontDialogPlanner.StylisticSetToolTip);
        _numberFormBox = Combo(
            FontDialogPlanner.NumberFormChoices.Select(choice => choice.Label),
            state.NumberFormIndex,
            minWidth: 160);
        _numberSpacingBox = Combo(
            FontDialogPlanner.NumberSpacingChoices.Select(choice => choice.Label),
            state.NumberSpacingIndex,
            minWidth: 160);

        var fontPanel = new StackPanel { Margin = new Thickness(12, 12, 11, 6) };
        AddField(fontPanel, "Font family:", _familyBox);
        AddField(fontPanel, "Size (pt):", _sizeBox);
        AddField(fontPanel, "Color:", _colorBox);
        fontPanel.Children.Add(new TextBlock { Text = "Style:", Margin = new Thickness(0, 3, 0, 2) });
        var effects = new WrapPanel();
        foreach (var check in new[]
                 {
                     _boldChk, _italicChk, _underlineChk, _strikeChk, _doubleStrikeChk, _hiddenChk,
                     _smallCapsChk, _allCapsChk, _superChk, _subChk,
                 })
        {
            effects.Children.Add(check);
        }
        fontPanel.Children.Add(effects);

        var advancedPanel = new StackPanel { Margin = new Thickness(10, 12, 10, 10) };
        AddField(advancedPanel, "Character spacing (pt):", _spacingBox);
        AddField(advancedPanel, "Kerning min size (pt):", _kerningBox);
        AddField(advancedPanel, "Position (pt):", _positionBox);
        AddField(advancedPanel, "Ligatures:", _ligatureBox);
        AddField(advancedPanel, "Stylistic set (1-20):", _stylisticBox);
        AddField(advancedPanel, "Number form:", _numberFormBox);
        AddField(advancedPanel, "Number spacing:", _numberSpacingBox);

        var tabs = new TabControl { Margin = new Thickness(0) };
        tabs.Items.Add(new TabItem
        {
            Header = "Font",
            Content = new ScrollViewer
            {
                Content = fontPanel,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            },
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Advanced",
            Content = new ScrollViewer
            {
                Content = advancedPanel,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            },
        });
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            tabs,
            DialogChromeStyle,
            contentPaneMargin: new Thickness(-12, -1, -12, 0));

        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(0, 6, 0, 0));
        var buttons = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            OnOk,
            () => Close(null),
            buttonWidth: 72,
            margin: new Thickness(0, 10, 0, 0),
            style: DialogChromeStyle);

        var root = new StackPanel { Margin = new Thickness(12, 12, 11, 12) };
        root.Children.Add(tabs);
        root.Children.Add(_status);
        root.Children.Add(buttons);
        Content = root;

        Opened += (_, _) =>
        {
            AvaloniaCompactDialogChrome.ApplyDescendantChrome(this, DialogChromeStyle);
            foreach (var combo in this.GetVisualDescendants().OfType<ComboBox>())
                FontParagraphDialogChrome.ApplyComboBox(combo, DialogChromeStyle, combo.IsEditable);
            foreach (var box in new[]
                     {
                         _familyBox, _spacingBox, _kerningBox, _positionBox, _stylisticBox,
                     })
                FontParagraphDialogChrome.ApplyTextBox(box, DialogChromeStyle);
            ApplyFontCheckBoxChrome();
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

    // Compatibility input retained for existing editor-facing tests and callers. Production dialog
    // acceptance returns FontDialogWorkflowResult directly; conversion policy remains in the session.
    public sealed record FontDialogResult(
        string? Family,
        double? SizePt,
        bool? Bold,
        bool? Italic,
        bool? Underline,
        bool? Strikethrough,
        VerticalAlign VerticalAlign,
        bool SmallCaps,
        bool AllCaps,
        string? ColorHex,
        string? HighlightHex,
        bool FamilyChanged = true,
        bool SizeChanged = true,
        double CharacterSpacingPt = 0,
        double? KerningMinSizePt = null,
        double PositionPt = 0,
        LigatureMode Ligatures = LigatureMode.None,
        int? StylisticSet = null,
        NumberForm NumberForm = NumberForm.Default,
        NumberSpacing NumberSpacing = NumberSpacing.Default,
        bool AdvancedChanged = false,
        bool? DoubleStrikethrough = null,
        bool? Hidden = null) : IFontDialogResultSource;

    private void OnOk()
    {
        _status.IsVisible = false;
        var acceptance = _session.PlanAcceptance(new FontDialogControlState(
            _familyBox.Text,
            _sizeBox.Text,
            _colorBox.SelectedIndex,
            _boldChk.IsChecked,
            _italicChk.IsChecked,
            _underlineChk.IsChecked,
            _strikeChk.IsChecked,
            _smallCapsChk.IsChecked == true,
            _allCapsChk.IsChecked == true,
            _superChk.IsChecked == true,
            _subChk.IsChecked == true,
            _spacingBox.Text,
            _kerningBox.Text,
            _positionBox.Text,
            _ligatureBox.SelectedIndex,
            _stylisticBox.Text,
            _numberFormBox.SelectedIndex,
            _numberSpacingBox.SelectedIndex,
            _doubleStrikeChk.IsChecked,
            _hiddenChk.IsChecked));

        if (!acceptance.IsAccepted)
        {
            _status.Text = acceptance.ErrorMessage ?? string.Empty;
            _status.IsVisible = true;
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

    public static void ApplyResult(DocumentView editor, FontDialogResult result, RunFormatting original)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        var session = FontDialogPlanner.CreateSession(original, CultureInfo.CurrentCulture);
        ExecuteApplyPlan(editor, session.BuildApplyPlan(session.ImportResult(result)));
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
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
        control.Margin = new Thickness(0, 0, 0, 8);
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

    private static CheckBox Check(string label, bool threeState = false, double trailingMargin = 12) => new()
    {
        Content = label,
        IsThreeState = threeState,
        Margin = new Thickness(0, 0, trailingMargin, 4),
    };

    private void ApplyCheckBoxChrome()
    {
        foreach (var checkBox in new[]
                 {
                     _boldChk, _italicChk, _underlineChk, _strikeChk, _doubleStrikeChk, _hiddenChk,
                     _smallCapsChk, _allCapsChk, _superChk, _subChk,
                 })
        {
            FontParagraphDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle);
        }
    }

    private void ApplyFontCheckBoxChrome()
    {
        foreach (var checkBox in new[]
                 {
                     _boldChk, _italicChk, _underlineChk, _strikeChk, _doubleStrikeChk, _hiddenChk,
                     _smallCapsChk, _allCapsChk, _superChk, _subChk,
                 })
            FontParagraphDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle);
    }

}
