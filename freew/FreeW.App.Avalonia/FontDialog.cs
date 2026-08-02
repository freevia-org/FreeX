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

    private readonly RunFormatting _original;
    private readonly bool _boldIndeterminate;
    private readonly bool _italicIndeterminate;
    private readonly bool _underlineIndeterminate;
    private readonly bool _strikeIndeterminate;
    private readonly bool _doubleStrikeIndeterminate;
    private readonly bool _hiddenIndeterminate;
    private readonly bool _familyIndeterminate;
    private readonly bool _sizeIndeterminate;

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
        _original = selection.Run;
        _boldIndeterminate = selection.BoldIndeterminate;
        _italicIndeterminate = selection.ItalicIndeterminate;
        _underlineIndeterminate = selection.UnderlineIndeterminate;
        _strikeIndeterminate = selection.StrikethroughIndeterminate;
        _doubleStrikeIndeterminate = selection.DoubleStrikethroughIndeterminate;
        _hiddenIndeterminate = selection.HiddenIndeterminate;
        _familyIndeterminate = selection.FamilyIndeterminate;
        _sizeIndeterminate = selection.SizeIndeterminate;

        Title = "Font";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);

        var state = FontDialogPlanner.BuildInitialState(_original, CultureInfo.CurrentCulture);

        _familyBox = TextBox(_familyIndeterminate ? string.Empty : state.FontFamilyText, minWidth: 200);
        _sizeBox = Combo(
            FontDialogPlanner.SizeChoices.Select(choice => choice.Label),
            selectedIndex: -1,
            minWidth: 80,
            editable: true);
        _sizeBox.Text = _sizeIndeterminate ? string.Empty : state.FontSizeText;
        _colorBox = Combo(
            FontDialogPlanner.ColorChoices.Select(choice => choice.Label),
            state.ColorIndex,
            minWidth: 180);

        _boldChk.IsChecked = _boldIndeterminate ? null : state.Bold;
        _italicChk.IsChecked = _italicIndeterminate ? null : state.Italic;
        _underlineChk.IsChecked = _underlineIndeterminate ? null : state.Underline;
        _strikeChk.IsChecked = _strikeIndeterminate ? null : state.Strikethrough;
        _doubleStrikeChk.IsChecked = _doubleStrikeIndeterminate ? null : state.DoubleStrikethrough;
        _hiddenChk.IsChecked = _hiddenIndeterminate ? null : state.Hidden;
        _smallCapsChk.IsChecked = state.SmallCaps;
        _allCapsChk.IsChecked = state.AllCaps;
        _superChk.IsChecked = state.Superscript;
        _subChk.IsChecked = state.Subscript;
        ApplyCheckBoxChrome();

        _superChk.IsCheckedChanged += (_, _) =>
        {
            if (_superChk.IsChecked == true)
                _subChk.IsChecked = false;
        };
        _subChk.IsCheckedChanged += (_, _) =>
        {
            if (_subChk.IsChecked == true)
                _superChk.IsChecked = false;
        };

        _spacingBox = TextBox(state.CharacterSpacingText, minWidth: 100);
        _kerningBox = TextBox(state.KerningMinSizeText, minWidth: 100);
        _positionBox = TextBox(state.PositionText, minWidth: 100);
        _ligatureBox = Combo(
            FontDialogPlanner.LigatureChoices.Select(choice => choice.Label),
            state.LigatureIndex,
            minWidth: 180);
        _stylisticBox = TextBox(state.StylisticSetText, minWidth: 100);
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
        bool? Hidden = null);

    private void OnOk()
    {
        _status.IsVisible = false;
        var input = new FontDialogInput(
            _familyIndeterminate && string.IsNullOrWhiteSpace(_familyBox.Text)
                ? _original.FontFamily
                : _familyBox.Text,
            _sizeIndeterminate && string.IsNullOrWhiteSpace(_sizeBox.Text)
                ? FormatOptional(_original.FontSizePt)
                : _sizeBox.Text,
            _colorBox.SelectedIndex,
            _boldIndeterminate ? _original.Bold : _boldChk.IsChecked == true,
            _italicIndeterminate ? _original.Italic : _italicChk.IsChecked == true,
            _underlineIndeterminate ? _original.Underline : _underlineChk.IsChecked == true,
            _strikeIndeterminate ? _original.Strikethrough : _strikeChk.IsChecked == true,
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
            _doubleStrikeIndeterminate ? _original.DoubleStrikethrough : _doubleStrikeChk.IsChecked == true,
            _hiddenIndeterminate ? _original.Hidden : _hiddenChk.IsChecked == true);

        if (!FontDialogPlanner.TryBuildResult(
                input,
                _original,
                CultureInfo.CurrentCulture,
                out var planned,
                out var errorMessage))
        {
            _status.Text = errorMessage ?? FontDialogPlanner.FontSizeValidationMessage;
            _status.IsVisible = true;
            return;
        }

        Close(ToDialogResult(planned!));
    }

    private FontDialogResult ToDialogResult(RunFormatting result) => new(
        Family: result.FontFamily,
        SizePt: result.FontSizePt,
        Bold: _boldIndeterminate ? null : result.Bold,
        Italic: _italicIndeterminate ? null : result.Italic,
        Underline: _underlineIndeterminate ? null : result.Underline,
        Strikethrough: _strikeIndeterminate ? null : result.Strikethrough,
        VerticalAlign: result.VerticalAlign,
        SmallCaps: result.SmallCaps,
        AllCaps: result.AllCaps,
        ColorHex: result.ColorHex,
        HighlightHex: _original.HighlightColorHex,
        FamilyChanged: !_familyIndeterminate || !string.IsNullOrWhiteSpace(_familyBox.Text),
        SizeChanged: !_sizeIndeterminate || !string.IsNullOrWhiteSpace(_sizeBox.Text),
        CharacterSpacingPt: result.CharacterSpacingPt,
        KerningMinSizePt: result.KerningMinSizePt,
        PositionPt: result.PositionPt,
        Ligatures: result.Ligatures,
        StylisticSet: result.StylisticSet,
        NumberForm: result.NumberForm,
        NumberSpacing: result.NumberSpacing,
        AdvancedChanged: true,
        DoubleStrikethrough: _doubleStrikeIndeterminate ? null : result.DoubleStrikethrough,
        Hidden: _hiddenIndeterminate ? null : result.Hidden);

    public static void ApplyResult(DocumentView editor, FontDialogResult result, RunFormatting original)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        editor.BeginFontUndoGroup();
        try
        {
            if (result.FamilyChanged && result.Family != original.FontFamily)
                editor.SetSelectionFontFamily(result.Family ?? string.Empty);
            if (result.SizeChanged && result.SizePt != original.FontSizePt && result.SizePt.HasValue)
                editor.SetSelectionFontSize(result.SizePt.Value);
            if (result.Bold.HasValue && result.Bold.Value != original.Bold)
                editor.ToggleBold();
            if (result.Italic.HasValue && result.Italic.Value != original.Italic)
                editor.ToggleItalic();
            if (result.Underline.HasValue && result.Underline.Value != original.Underline)
                editor.ToggleUnderline();
            if (result.Strikethrough.HasValue && result.Strikethrough.Value != original.Strikethrough)
                editor.ToggleStrikethrough();
            if (result.DoubleStrikethrough.HasValue
                && result.DoubleStrikethrough.Value != original.DoubleStrikethrough)
                editor.ToggleDoubleStrikethrough();
            if (result.Hidden.HasValue && result.Hidden.Value != original.Hidden)
                editor.ToggleHidden();

            if (result.VerticalAlign != original.VerticalAlign)
            {
                if (result.VerticalAlign == VerticalAlign.Superscript)
                    editor.ToggleSuperscript();
                else if (result.VerticalAlign == VerticalAlign.Subscript)
                    editor.ToggleSubscript();
                else if (original.VerticalAlign == VerticalAlign.Superscript)
                    editor.ToggleSuperscript();
                else if (original.VerticalAlign == VerticalAlign.Subscript)
                    editor.ToggleSubscript();
            }

            if (result.ColorHex != original.ColorHex)
                editor.SetFontColor(result.ColorHex);
            if (result.HighlightHex != original.HighlightColorHex)
                editor.SetHighlightColor(result.HighlightHex);
            if (result.SmallCaps != original.SmallCaps)
                editor.ToggleSmallCaps();
            if (result.AllCaps != original.AllCaps)
                editor.ToggleAllCaps();

            if (result.AdvancedChanged)
            {
                editor.ApplyAdvancedFontFormatting(original with
                {
                    CharacterSpacingPt = result.CharacterSpacingPt,
                    KerningMinSizePt = result.KerningMinSizePt,
                    PositionPt = result.PositionPt,
                    Ligatures = result.Ligatures,
                    StylisticSet = result.StylisticSet,
                    NumberForm = result.NumberForm,
                    NumberSpacing = result.NumberSpacing,
                });
            }
        }
        finally
        {
            editor.CommitFontUndoGroup("Font");
        }
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var selection = editor.GetSelectionFormatting();
        var result = await new FontDialog(selection).ShowDialog<FontDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result, selection.Run);
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

    private static string FormatOptional(double? value) =>
        value?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
}
