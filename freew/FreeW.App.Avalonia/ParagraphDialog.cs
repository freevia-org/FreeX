using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using TextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia;

/// <summary>Avalonia chrome for the shared two-tab WPF Paragraph dialog contract.</summary>
public sealed class ParagraphDialog : FreeWDialogWindow
{
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            FontFamily = new FontFamily("Segoe UI"),
            ControlHeight = 20,
            TextBoxHeight = 18,
            ComboBoxHeight = 22,
            TabHeight = 20,
            ButtonHeight = 20,
            ButtonPadding = new Thickness(10, 1),
            ForegroundBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)),
            ButtonBorderBrush = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            DialogTabPaneBorderBrush = new SolidColorBrush(Color.FromRgb(0xAC, 0xAC, 0xAC)),
            InputBorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3)),
            ComboBoxBackgroundBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            TextBoxBackgroundBrush = Brushes.White,
            DisabledTextBoxBackgroundBrush = Brushes.White,
            TextSelectionBrush = new SolidColorBrush(Color.FromRgb(0x56, 0x9D, 0xE5)),
            DialogInactiveTabBorderBrush = new SolidColorBrush(Color.FromRgb(0xAC, 0xAC, 0xAC)),
            DialogInactiveTabBackgroundBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            RemoveFocusAdorner = true,
        };
    private readonly TabControl _tabs;
    private readonly TextBox _left;
    private readonly TextBox _right;
    private readonly ComboBox _special;
    private readonly TextBox _specialAmount;
    private readonly TextBox _before;
    private readonly TextBox _after;
    private readonly TextBox _lineSpacing;
    private readonly CheckBox _keepWithNext;
    private readonly CheckBox _keepLinesTogether;
    private readonly CheckBox _widowControl;
    private readonly CheckBox _pageBreakBefore;
    private readonly CheckBox _suppressHyphens;
    private readonly CheckBox _suppressLineNumbers;
    private readonly CheckBox _contextualSpacing;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public ParagraphDialog(ParagraphFormatting current)
    {
        ArgumentNullException.ThrowIfNull(current);
        // Keep the outer authority size in lockstep with ParagraphBreaksDialog.Prompt. The harness
        // reserves the native WPF frame and supplies the remaining client height, so these tab panes
        // must consume the same two client-area heights as WPF rather than growing from Avalonia's
        // default control templates.
        PageLayoutDialogChrome.Configure(this, "Paragraph", 380);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        var state = ParagraphBreaksDialogPlanner.BuildInitialState(current, DialogCulture);
        _left = NumberBox(state.LeftText);
        _right = NumberBox(state.RightText);
        _special = PageLayoutDialogChrome.Combo(
            ParagraphIndentDialogPlanner.SpecialItems.Select(item => item.Label),
            state.SpecialIndex,
            120,
            DialogChromeStyle);
        _special.HorizontalAlignment = HorizontalAlignment.Stretch;
        _specialAmount = NumberBox(state.SpecialAmountText);
        _specialAmount.IsEnabled = state.SpecialAmountEnabled;
        FontParagraphDialogChrome.ApplyTextBox(_specialAmount, DialogChromeStyle);
        _special.SelectionChanged += (_, _) =>
            _specialAmount.IsEnabled = ParagraphBreaksDialogPlanner.IsSpecialAmountEnabled(_special.SelectedIndex);
        _before = NumberBox(state.SpaceBeforeText);
        _after = NumberBox(state.SpaceAfterText);
        _lineSpacing = NumberBox(state.LineSpacingText);
        _keepWithNext = Check("Keep with next", state.KeepWithNext);
        _keepLinesTogether = Check("Keep lines together", state.KeepLinesTogether);
        _widowControl = Check("Widow/orphan control", state.WidowControl);
        _pageBreakBefore = Check("Page break before", state.PageBreakBefore);
        _suppressHyphens = Check("Suppress auto-hyphenation", state.SuppressAutoHyphens);
        _suppressLineNumbers = Check("Suppress line numbers", state.SuppressLineNumbers);
        _contextualSpacing = Check("Don't add space between paragraphs of the same style", state.ContextualSpacing);

        AutomationProperties.SetAutomationId(_left, ParagraphBreaksDialogPlanner.LeftIndentAutomationId);

        _tabs = new TabControl
        {
            Margin = new Thickness(12, 12, 11, 0),
            Padding = new Thickness(0),
            Height = 253,
        };
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            _tabs,
            DialogChromeStyle,
            contentPaneMargin: new Thickness(0, -1, 0, 0));
        _tabs.Items.Add(new TabItem { Header = "Indents and Spacing", Width = 123, Content = BuildIndentsTab() });
        _tabs.Items.Add(new TabItem { Header = "Line and Page Breaks", Width = 122, Content = BuildBreaksTab() });
        _tabs.SelectionChanged += (_, _) => _tabs.Height = _tabs.SelectedIndex == 1 ? 235 : 253;
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(12, 8, 11, 0));

        var root = new StackPanel();
        root.Children.Add(_tabs);
        root.Children.Add(_status);
        var actions = PageLayoutDialogChrome.Actions(Accept, () => Close(null), DialogChromeStyle, 72);
        actions.Margin = new Thickness(12, 10, 11, 11);
        root.Children.Add(actions);
        Content = root;

        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            ApplyParagraphChrome();
            PageLayoutDialogChrome.FocusAndSelect(_left);
        }, DispatcherPriority.Loaded);
        PageLayoutDialogChrome.WireEscape<ParagraphBreaksDialogResult?>(this);
    }

    private Control BuildIndentsTab()
    {
        var grid = new Grid { Margin = new Thickness(9, 12, 12, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(104)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        for (var row = 0; row < 8; row++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddGridRow(grid, 0, "Left indent (pt):", _left);
        AddGridRow(grid, 1, "Right indent (pt):", _right);
        AddGridRow(grid, 2, "Special:", _special);
        AddGridRow(grid, 3, "By (pt):", _specialAmount);
        AddGridRow(grid, 4, "Space before (pt):", _before);
        AddGridRow(grid, 5, "Space after (pt):", _after);
        AddGridRow(grid, 6, "Line spacing (\u00d7):", _lineSpacing);
        Grid.SetRow(_contextualSpacing, 7);
        Grid.SetColumnSpan(_contextualSpacing, 2);
        _contextualSpacing.Margin = new Thickness(3, 4, 0, 0);
        grid.Children.Add(_contextualSpacing);
        return grid;
    }

    private Control BuildBreaksTab()
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = "Pagination", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(_keepWithNext);
        panel.Children.Add(_keepLinesTogether);
        panel.Children.Add(_widowControl);
        panel.Children.Add(_pageBreakBefore);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Formatting exceptions", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(_suppressHyphens);
        panel.Children.Add(_suppressLineNumbers);
        return panel;
    }

    private void Accept()
    {
        var input = new ParagraphBreaksDialogInput(
            _left.Text,
            _right.Text,
            _special.SelectedIndex,
            _specialAmount.Text,
            _before.Text,
            _after.Text,
            _lineSpacing.Text,
            _keepWithNext.IsChecked == true,
            _keepLinesTogether.IsChecked == true,
            _widowControl.IsChecked == true,
            _pageBreakBefore.IsChecked == true,
            _suppressHyphens.IsChecked == true,
            _suppressLineNumbers.IsChecked == true,
            _contextualSpacing.IsChecked == true);
        if (!ParagraphBreaksDialogPlanner.TryBuildResult(input, DialogCulture, out var result, out var validation))
        {
            PageLayoutDialogChrome.ShowError(_status, validation?.Message ?? ParagraphBreaksDialogPlanner.ValidationMessage);
            _tabs.SelectedIndex = 0;
            var target = validation?.Field switch
            {
                ParagraphBreaksDialogField.Right => _right,
                ParagraphBreaksDialogField.SpecialAmount => _specialAmount,
                ParagraphBreaksDialogField.SpaceBefore => _before,
                ParagraphBreaksDialogField.SpaceAfter => _after,
                ParagraphBreaksDialogField.LineSpacing => _lineSpacing,
                _ => _left
            };
            PageLayoutDialogChrome.FocusAndSelect(target);
            return;
        }
        Close(result);
    }

    public static void ApplyResult(DocumentView editor, ParagraphBreaksDialogResult result) =>
        editor.ApplyParagraphDialogFormatting(
            result.LeftPt,
            result.RightPt,
            result.FirstLinePt,
            result.SpaceBeforePt,
            result.SpaceAfterPt,
            result.LineSpacing,
            result.KeepWithNext,
            result.KeepLinesTogether,
            result.WidowControl,
            result.PageBreakBefore,
            result.SuppressAutoHyphens,
            result.SuppressLineNumbers,
            result.ContextualSpacing);

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        var (_, current) = editor.GetCaretFormatting();
        var result = await new ParagraphDialog(current).ShowDialog<ParagraphBreaksDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result);
        editor.Focus();
    }

    // Compatibility for existing callers that construct the pre-parity Avalonia result directly.
    public sealed record ParagraphDialogResult(
        TextAlignment Alignment,
        double IndentLeftPt,
        double IndentRightPt,
        double FirstLineIndentPt,
        double SpaceBeforePt,
        double SpaceAfterPt,
        LineSpacingRule LineRule,
        double LineSpacingValue);

    public static void ApplyResult(DocumentView editor, ParagraphDialogResult result, ParagraphFormatting original) =>
        editor.ApplyParagraphDialogFormatting(
            result.Alignment,
            result.IndentLeftPt,
            result.IndentRightPt,
            result.FirstLineIndentPt,
            result.SpaceBeforePt,
            result.SpaceAfterPt,
            result.LineRule,
            result.LineSpacingValue);

    private void ApplyParagraphChrome()
    {
        foreach (var box in new[] { _left, _right, _specialAmount, _before, _after, _lineSpacing })
            FontParagraphDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        FontParagraphDialogChrome.ApplyComboBox(_special, DialogChromeStyle, editable: false);
        foreach (var checkBox in new[] { _keepWithNext, _keepLinesTogether, _widowControl, _pageBreakBefore, _suppressHyphens, _suppressLineNumbers, _contextualSpacing })
            FontParagraphDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle);
        foreach (var button in this.GetVisualDescendants().OfType<Button>())
            AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, 72, button.IsDefault);
    }

    private static TextBox NumberBox(string text) =>
        PageLayoutDialogChrome.NumberBox(text, 120, DialogChromeStyle, stretch: true);

    private static void AddGridRow(Grid grid, int row, string label, Control field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        field.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static CheckBox Check(string label, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 0, 6) };
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(box, DialogChromeStyle);
        return box;
    }
}
