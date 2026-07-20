using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
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
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public ParagraphDialog(ParagraphFormatting current)
    {
        ArgumentNullException.ThrowIfNull(current);
        PageLayoutDialogChrome.Configure(this, "Paragraph", 400);
        var state = ParagraphBreaksDialogPlanner.BuildInitialState(current, DialogCulture);
        _left = NumberBox(state.LeftText);
        _right = NumberBox(state.RightText);
        _special = PageLayoutDialogChrome.Combo(
            ParagraphIndentDialogPlanner.SpecialItems.Select(item => item.Label),
            state.SpecialIndex,
            150);
        _specialAmount = NumberBox(state.SpecialAmountText);
        _specialAmount.IsEnabled = state.SpecialAmountEnabled;
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

        _tabs = new TabControl { Margin = new Thickness(16, 14, 16, 0) };
        _tabs.Items.Add(new TabItem { Header = "Indents and Spacing", Content = BuildIndentsTab() });
        _tabs.Items.Add(new TabItem { Header = "Line and Page Breaks", Content = BuildBreaksTab() });
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, PageLayoutDialogChrome.Style, new Thickness(16, 8, 16, 0));

        var root = new StackPanel();
        root.Children.Add(_tabs);
        root.Children.Add(_status);
        var actions = PageLayoutDialogChrome.Actions(Accept, () => Close(null));
        actions.Margin = new Thickness(16, 12, 16, 16);
        root.Children.Add(actions);
        Content = root;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_left);
        PageLayoutDialogChrome.WireEscape<ParagraphBreaksDialogResult?>(this);
    }

    private Control BuildIndentsTab()
    {
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        panel.Children.Add(PageLayoutDialogChrome.Row("Left indent (pt):", _left));
        panel.Children.Add(PageLayoutDialogChrome.Row("Right indent (pt):", _right));
        panel.Children.Add(PageLayoutDialogChrome.Row("Special:", _special));
        panel.Children.Add(PageLayoutDialogChrome.Row("By (pt):", _specialAmount));
        panel.Children.Add(PageLayoutDialogChrome.Row("Space before (pt):", _before));
        panel.Children.Add(PageLayoutDialogChrome.Row("Space after (pt):", _after));
        panel.Children.Add(PageLayoutDialogChrome.Row("Line spacing (x):", _lineSpacing));
        return panel;
    }

    private Control BuildBreaksTab()
    {
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 5 };
        panel.Children.Add(new TextBlock { Text = "Pagination", FontWeight = global::Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(_keepWithNext);
        panel.Children.Add(_keepLinesTogether);
        panel.Children.Add(_widowControl);
        panel.Children.Add(_pageBreakBefore);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 5, 0, 5) });
        panel.Children.Add(new TextBlock { Text = "Formatting exceptions", FontWeight = global::Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(_suppressHyphens);
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
            _suppressHyphens.IsChecked == true);
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
            result.SuppressAutoHyphens);

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

    private static TextBox NumberBox(string text) => PageLayoutDialogChrome.NumberBox(text, 120);

    private static CheckBox Check(string label, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        AvaloniaCompactDialogChrome.ApplyCheckBox(box, PageLayoutDialogChrome.Style);
        return box;
    }
}
