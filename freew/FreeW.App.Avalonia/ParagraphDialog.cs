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
    private static readonly DialogSurfaceSpec<ParagraphBreaksDialogField> Surface =
        ParagraphBreaksDialogPlanner.Surface;
    private static readonly ParagraphDialogVisualMetrics Layout =
        ParagraphBreaksDialogPlanner.VisualMetrics;
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
        PageLayoutDialogChrome.Configure(this, Surface, Layout.WindowWidth);
        var state = ParagraphBreaksDialogPlanner.BuildInitialState(current, DialogCulture);
        _left = NumberBox(state.LeftText);
        _right = NumberBox(state.RightText);
        _special = PageLayoutDialogChrome.Combo(
            ParagraphIndentDialogPlanner.SpecialItems.Select(item => item.Label),
            state.SpecialIndex,
            Layout.NumericFieldMinWidth,
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
        _keepWithNext = Check(Surface.Field(ParagraphBreaksDialogField.KeepWithNext), state.KeepWithNext);
        _keepLinesTogether = Check(Surface.Field(ParagraphBreaksDialogField.KeepLinesTogether), state.KeepLinesTogether);
        _widowControl = Check(Surface.Field(ParagraphBreaksDialogField.WidowControl), state.WidowControl);
        _pageBreakBefore = Check(Surface.Field(ParagraphBreaksDialogField.PageBreakBefore), state.PageBreakBefore);
        _suppressHyphens = Check(Surface.Field(ParagraphBreaksDialogField.SuppressAutoHyphens), state.SuppressAutoHyphens);
        _suppressLineNumbers = Check(Surface.Field(ParagraphBreaksDialogField.SuppressLineNumbers), state.SuppressLineNumbers);
        _contextualSpacing = Check(Surface.Field(ParagraphBreaksDialogField.ContextualSpacing), state.ContextualSpacing);

        PageLayoutDialogChrome.ApplySurface(_left, Surface.Field(ParagraphBreaksDialogField.Left));
        PageLayoutDialogChrome.ApplySurface(_right, Surface.Field(ParagraphBreaksDialogField.Right));
        PageLayoutDialogChrome.ApplySurface(_special, Surface.Field(ParagraphBreaksDialogField.Special));
        PageLayoutDialogChrome.ApplySurface(_specialAmount, Surface.Field(ParagraphBreaksDialogField.SpecialAmount));
        PageLayoutDialogChrome.ApplySurface(_before, Surface.Field(ParagraphBreaksDialogField.SpaceBefore));
        PageLayoutDialogChrome.ApplySurface(_after, Surface.Field(ParagraphBreaksDialogField.SpaceAfter));
        PageLayoutDialogChrome.ApplySurface(_lineSpacing, Surface.Field(ParagraphBreaksDialogField.LineSpacing));
        PageLayoutDialogChrome.ApplyValidation(_status, Surface);

        _tabs = new TabControl
        {
            Margin = ToThickness(Layout.AvaloniaTabsMargin),
            Padding = new Thickness(0),
            Height = Layout.AvaloniaIndentsTabHeight,
        };
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            _tabs,
            DialogChromeStyle,
            contentPaneMargin: ToThickness(Layout.AvaloniaTabPaneMargin));
        var indentsTab = new TabItem
        {
            Header = Surface.Field(ParagraphBreaksDialogField.IndentsAndSpacingTab).Label,
            Width = Layout.AvaloniaIndentsTabHeaderWidth,
            Content = BuildIndentsTab(),
        };
        var breaksTab = new TabItem
        {
            Header = Surface.Field(ParagraphBreaksDialogField.LineAndPageBreaksTab).Label,
            Width = Layout.AvaloniaBreaksTabHeaderWidth,
            Content = BuildBreaksTab(),
        };
        PageLayoutDialogChrome.ApplySurface(indentsTab, Surface.Field(ParagraphBreaksDialogField.IndentsAndSpacingTab));
        PageLayoutDialogChrome.ApplySurface(breaksTab, Surface.Field(ParagraphBreaksDialogField.LineAndPageBreaksTab));
        _tabs.Items.Add(indentsTab);
        _tabs.Items.Add(breaksTab);
        _tabs.SelectionChanged += (_, _) =>
            _tabs.Height = _tabs.SelectedIndex == 1
                ? Layout.AvaloniaBreaksTabHeight
                : Layout.AvaloniaIndentsTabHeight;
        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _status,
            DialogChromeStyle,
            ToThickness(Layout.AvaloniaValidationMargin));

        var root = new StackPanel();
        root.Children.Add(_tabs);
        root.Children.Add(_status);
        var actions = PageLayoutDialogChrome.Actions(
            Accept,
            () => Close(null),
            DialogChromeStyle,
            Layout.ActionButtonWidth);
        actions.Margin = ToThickness(Layout.AvaloniaActionRowMargin);
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
        var grid = new Grid { Margin = ToThickness(Layout.AvaloniaIndentsTabContentMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.AvaloniaLabelColumnWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        for (var row = 0; row < 8; row++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddGridRow(grid, 0, Surface.Field(ParagraphBreaksDialogField.Left).Label, _left);
        AddGridRow(grid, 1, Surface.Field(ParagraphBreaksDialogField.Right).Label, _right);
        AddGridRow(grid, 2, Surface.Field(ParagraphBreaksDialogField.Special).Label, _special);
        AddGridRow(grid, 3, Surface.Field(ParagraphBreaksDialogField.SpecialAmount).Label, _specialAmount);
        AddGridRow(grid, 4, Surface.Field(ParagraphBreaksDialogField.SpaceBefore).Label, _before);
        AddGridRow(grid, 5, Surface.Field(ParagraphBreaksDialogField.SpaceAfter).Label, _after);
        AddGridRow(grid, 6, Surface.Field(ParagraphBreaksDialogField.LineSpacing).Label, _lineSpacing);
        Grid.SetRow(_contextualSpacing, 7);
        Grid.SetColumnSpan(_contextualSpacing, 2);
        _contextualSpacing.Margin = ToThickness(Layout.AvaloniaContextualSpacingMargin);
        grid.Children.Add(_contextualSpacing);
        return grid;
    }

    private Control BuildBreaksTab()
    {
        var panel = new StackPanel { Margin = ToThickness(Layout.WpfTabContentMargin) };
        var paginationHeading = new TextBlock
        {
            Text = Surface.Field(ParagraphBreaksDialogField.PaginationSection).Label,
            FontWeight = FontWeight.SemiBold,
            Margin = ToThickness(Layout.SectionHeadingMargin),
        };
        PageLayoutDialogChrome.ApplySurface(
            paginationHeading,
            Surface.Field(ParagraphBreaksDialogField.PaginationSection));
        panel.Children.Add(paginationHeading);
        panel.Children.Add(_keepWithNext);
        panel.Children.Add(_keepLinesTogether);
        panel.Children.Add(_widowControl);
        panel.Children.Add(_pageBreakBefore);
        panel.Children.Add(new Separator { Margin = ToThickness(Layout.SectionSeparatorMargin) });
        var formattingExceptionsHeading = new TextBlock
        {
            Text = Surface.Field(ParagraphBreaksDialogField.FormattingExceptionsSection).Label,
            FontWeight = FontWeight.SemiBold,
            Margin = ToThickness(Layout.SectionHeadingMargin),
        };
        PageLayoutDialogChrome.ApplySurface(
            formattingExceptionsHeading,
            Surface.Field(ParagraphBreaksDialogField.FormattingExceptionsSection));
        panel.Children.Add(formattingExceptionsHeading);
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

    private void ApplyParagraphChrome()
    {
        foreach (var box in new[] { _left, _right, _specialAmount, _before, _after, _lineSpacing })
            FontParagraphDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        FontParagraphDialogChrome.ApplyComboBox(_special, DialogChromeStyle, editable: false);
        foreach (var checkBox in new[] { _keepWithNext, _keepLinesTogether, _widowControl, _pageBreakBefore, _suppressHyphens, _suppressLineNumbers, _contextualSpacing })
            FontParagraphDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle);
        foreach (var button in this.GetVisualDescendants().OfType<Button>())
            AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, Layout.ActionButtonWidth, button.IsDefault);
    }

    private static TextBox NumberBox(string text) =>
        PageLayoutDialogChrome.NumberBox(
            text,
            Layout.NumericFieldMinWidth,
            DialogChromeStyle,
            stretch: true);

    private static void AddGridRow(Grid grid, int row, string label, Control field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = ToThickness(Layout.FieldLabelMargin),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        field.Margin = ToThickness(Layout.FieldControlMargin);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static CheckBox Check(
        DialogFieldSurfaceSpec<ParagraphBreaksDialogField> field,
        bool value)
    {
        var box = new CheckBox
        {
            Content = field.Label,
            IsChecked = value,
            Margin = ToThickness(Layout.CheckBoxMargin),
        };
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(box, DialogChromeStyle);
        PageLayoutDialogChrome.ApplySurface(box, field);
        return box;
    }

    private static Thickness ToThickness(ParagraphDialogThickness value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);
}
