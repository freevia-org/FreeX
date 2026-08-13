global using PageSetupDialogTab = FreeW.App.Presentation.Dialogs.PageSetupDialogTabKind;

using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Localization;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Avalonia chrome for the shared WPF-authoritative three-tab Page Setup contract.</summary>
public sealed class PageSetupDialog : FreeWDialogWindow, IPageSetupDialogControlSource
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = PageLayoutDialogChrome.Style;
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;

    private readonly TextBox _top;
    private readonly TextBox _bottom;
    private readonly TextBox _left;
    private readonly TextBox _right;
    private readonly TextBox _gutter;
    private readonly ComboBox _gutterPosition;
    private readonly ComboBox _orientation;
    private readonly ComboBox _multiplePages;
    private readonly ComboBox _applyTo;
    private readonly ComboBox _paperSize;
    private readonly TextBox _width;
    private readonly TextBox _height;
    private readonly ComboBox _sectionStart;
    private readonly CheckBox _differentFirstPage;
    private readonly CheckBox _differentOddEven;
    private readonly TextBox _headerDistance;
    private readonly TextBox _footerDistance;
    private readonly ComboBox _verticalAlignment;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();
    private readonly PageSetupDialogSession _session;
    private readonly IUserMessageService _messageService;
    private bool _suppressPaperSync;

    public PageSetupDialog(
        PageSettings current,
        PageSetupDialogTabKind initialTab = PageSetupDialogTabKind.Margins,
        SectionBreakKind sectionStart = SectionBreakKind.NextPage,
        IUserMessageService? messageService = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var surface = PageSetupDialogPlanner.Surface;
        PageLayoutDialogChrome.Configure(this, surface.Title, metrics.WindowWidth);

        _session = PageSetupDialogPlanner.CreateSession(
            current,
            sectionStart,
            DialogCulture);
        _messageService = messageService ?? new AvaloniaUserMessageService(this);
        var state = _session.InitialState;
        _top = NumberBox(state.MarginTopText);
        _bottom = NumberBox(state.MarginBottomText);
        _left = NumberBox(state.MarginLeftText);
        _right = NumberBox(state.MarginRightText);
        _gutter = NumberBox(state.GutterText);
        _gutterPosition = Combo(PageSetupDialogPlanner.GutterPositionNames, state.GutterPositionIndex);
        _orientation = Combo(PageSetupDialogPlanner.OrientationNames, state.OrientationIndex);
        _multiplePages = Combo(PageSetupDialogPlanner.MultiplePagesNames, state.MultiplePagesIndex);
        _applyTo = Combo(PageSetupDialogPlanner.ApplyToNames, 0);
        _paperSize = Combo(_session.PaperOptions.Select(option => option.HostLabel), state.PaperSizeIndex);
        _width = NumberBox(state.WidthText);
        _height = NumberBox(state.HeightText);
        _sectionStart = Combo(PageSetupDialogPlanner.SectionStartNames, state.SectionStartIndex);
        _differentFirstPage = Check(surface.LayoutToggles[0].Label, state.DifferentFirstPage, new Thickness(0));
        _differentOddEven = Check(surface.LayoutToggles[1].Label, state.DifferentOddEvenPages, ToThickness(metrics.SecondCheckMargin));
        _headerDistance = NumberBox(state.HeaderDistanceText);
        _footerDistance = NumberBox(state.FooterDistanceText);
        _verticalAlignment = Combo(PageSetupDialogPlanner.VerticalAlignmentNames, state.VerticalAlignmentIndex);

        ApplyEnabledState(_session.EnabledState);
        _paperSize.SelectionChanged += (_, _) => ApplyPaperPreset();
        _width.TextChanged += (_, _) => SyncPaperToCustom();
        _height.TextChanged += (_, _) => SyncPaperToCustom();

        var tabs = new TabControl
        {
            Margin = ToThickness(metrics.TabMargin),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        foreach (var tabSpec in surface.Tabs)
        {
            var tab = new TabItem { Header = tabSpec.Header, Content = BuildTab(tabSpec) };
            AutomationProperties.SetAutomationId(tab, tabSpec.AutomationId);
            tabs.Items.Add(tab);
        }
        tabs.SelectedIndex = (int)initialTab;
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            tabs,
            DialogChromeStyle,
            contentPaneMargin: ToThickness(metrics.TabPaneMargin));
        for (var index = 0; index < tabs.Items.Count && index < metrics.AvaloniaTabWidths.Count; index++)
            ((TabItem)tabs.Items[index]!).Width = metrics.AvaloniaTabWidths[index];

        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _status,
            DialogChromeStyle,
            ToThickness(metrics.AvaloniaValidationMargin));
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto")
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRow(_status, 1);
        var actionStyle = DialogChromeStyle with
        {
            ActionSpacing = metrics.AvaloniaActionSpacing
        };
        var actions = PageLayoutDialogChrome.Actions(
            Accept,
            () => Close(null),
            style: actionStyle,
            buttonWidth: metrics.ActionButtonWidth);
        var actionMargin = metrics.ActionRowMargin;
        actions.Margin = new Thickness(
            actionMargin.Left,
            actionMargin.Top,
            metrics.AvaloniaActionRightInset,
            actionMargin.Bottom);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        root.Children.Add(tabs);
        root.Children.Add(_status);
        Content = root;

        Opened += (_, _) =>
        {
            foreach (var button in ((Panel)actions).Children.OfType<Button>())
                AvaloniaCompactDialogChrome.ApplyButton(button, actionStyle, metrics.ActionButtonWidth);
            ApplyFocus(_session.InitialFocusPlan);
        };
        PageLayoutDialogChrome.WireEscape<PageSetupDialogAcceptance?>(this);
    }

    private Control BuildTab(PageSetupDialogTabSpec tab)
    {
        var panel = TabPanel();
        foreach (var row in tab.Rows)
            panel.Children.Add(PageSetupRow(row.Label, ControlFor(row.Kind)));
        if (tab.Kind != PageSetupDialogTabKind.Layout)
            return panel;

        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var checks = new StackPanel { Margin = new Thickness(0, metrics.CheckGroupTopSpacing, 0, 0) };
        checks.Children.Add(_differentFirstPage);
        checks.Children.Add(_differentOddEven);
        panel.Children.Add(checks);

        var launchers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = metrics.AvaloniaLauncherSpacing,
            Margin = new Thickness(metrics.AvaloniaLauncherLeftInset, metrics.LauncherTopSpacing, 0, 0)
        };
        foreach (var launcher in PageSetupDialogPlanner.Surface.LayoutLaunchers)
        {
            var button = new Button { Content = launcher.Label };
            AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: metrics.LauncherButtonWidth);
            button.Click += (_, _) => Accept(launcher.FollowUp);
            launchers.Children.Add(button);
        }
        panel.Children.Add(launchers);
        return panel;
    }

    private Control ControlFor(PageSetupDialogControlKind kind) => kind switch
    {
        PageSetupDialogControlKind.MarginTop => _top,
        PageSetupDialogControlKind.MarginBottom => _bottom,
        PageSetupDialogControlKind.MarginLeft => _left,
        PageSetupDialogControlKind.MarginRight => _right,
        PageSetupDialogControlKind.Gutter => _gutter,
        PageSetupDialogControlKind.GutterPosition => _gutterPosition,
        PageSetupDialogControlKind.Orientation => _orientation,
        PageSetupDialogControlKind.MultiplePages => _multiplePages,
        PageSetupDialogControlKind.ApplyTo => _applyTo,
        PageSetupDialogControlKind.PaperSize => _paperSize,
        PageSetupDialogControlKind.PageWidth => _width,
        PageSetupDialogControlKind.PageHeight => _height,
        PageSetupDialogControlKind.SectionStart => _sectionStart,
        PageSetupDialogControlKind.VerticalAlignment => _verticalAlignment,
        PageSetupDialogControlKind.HeaderDistance => _headerDistance,
        PageSetupDialogControlKind.FooterDistance => _footerDistance,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private void ApplyPaperPreset()
    {
        ApplyPaperProjection(_session.PlanPaperSelection(_paperSize.SelectedIndex));
    }

    private void SyncPaperToCustom()
    {
        if (_suppressPaperSync)
            return;

        var plan = _session.PlanDimensionEdit(_width.Text, _height.Text, _paperSize.SelectedIndex);
        ApplyEnabledState(plan.EnabledState);
        if (plan.UpdatePaperSize)
            _paperSize.SelectedIndex = plan.PaperSizeIndex;
    }

    private void Accept()
    {
        Accept(PageSetupDialogFollowUp.None);
    }

    private async void Accept(PageSetupDialogFollowUp followUp)
    {
        var acceptance = _session.PlanAcceptance(this, followUp);
        if (!acceptance.IsAccepted)
        {
            _status.IsVisible = false;
            await _messageService.ShowWarningAsync(acceptance.ErrorMessage!);
            ApplyFocus(acceptance.FocusPlan!);
            return;
        }

        Close(acceptance);
    }

    private void ApplyFocus(PageSetupDialogFocusPlan plan)
    {
        var target = plan.Field switch
        {
            PageSetupDialogField.MarginTop => _top,
            PageSetupDialogField.MarginBottom => _bottom,
            PageSetupDialogField.MarginLeft => _left,
            PageSetupDialogField.MarginRight => _right,
            PageSetupDialogField.Gutter => _gutter,
            PageSetupDialogField.PageWidth => _width,
            PageSetupDialogField.PageHeight => _height,
            PageSetupDialogField.HeaderDistance => _headerDistance,
            PageSetupDialogField.FooterDistance => _footerDistance,
            _ => _top,
        };

        if (plan.SelectAllOnFocus)
            PageLayoutDialogChrome.FocusAndSelect(target);
        else
            target.Focus();
    }

    private void ApplyPaperProjection(PageSetupPaperSelectionPlan plan)
    {
        ApplyEnabledState(plan.EnabledState);
        if (!plan.UpdateDimensions)
            return;

        _suppressPaperSync = true;
        _width.Text = plan.WidthText!;
        _height.Text = plan.HeightText!;
        _suppressPaperSync = false;
    }

    private void ApplyEnabledState(PageSetupDialogEnabledState state)
    {
        _width.IsEnabled = state.WidthEnabled;
        _height.IsEnabled = state.HeightEnabled;
    }

    public static void ApplyResult(DocumentView editor, PageSetupDialogResult result) =>
        editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyPageSetupResult(page, result));

    public static async Task ShowAndApplyAsync(
        Window owner,
        DocumentView editor,
        PageSetupDialogTabKind initialTab = PageSetupDialogTabKind.Margins,
        Func<Task>? openLineNumbers = null,
        Func<Task>? openBorders = null)
    {
        var outcome = await new PageSetupDialog(editor.Document.Page, initialTab)
            .ShowDialog<PageSetupDialogAcceptance?>(owner);
        if (outcome is null)
            return;

        ApplyResult(editor, outcome.Result!);
        if (outcome.FollowUp == PageSetupDialogFollowUp.LineNumbers && openLineNumbers is not null)
            await openLineNumbers();
        else if (outcome.FollowUp == PageSetupDialogFollowUp.Borders && openBorders is not null)
            await openBorders();
        editor.Focus();
    }

    private static StackPanel TabPanel()
    {
        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var margin = metrics.TabContentMargin;
        return new StackPanel
        {
            Margin = new Thickness(
                margin.Left + metrics.AvaloniaTabContentInset,
                margin.Top,
                margin.Right + metrics.AvaloniaTabContentInset,
                margin.Bottom)
        };
    }

    private static TextBox NumberBox(string value)
    {
        var box = PageLayoutDialogChrome.NumberBox(
            value,
            PageSetupDialogPlanner.PresentationMetrics.NumberBoxMinWidth,
            stretch: true);
        ApplyFieldHeight(box);
        return box;
    }

    private static ComboBox Combo(IEnumerable<string> values, int selectedIndex)
    {
        var combo = PageLayoutDialogChrome.Combo(
            values,
            selectedIndex,
            PageSetupDialogPlanner.PresentationMetrics.ComboBoxMinWidth);
        ApplyFieldHeight(combo);
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        return combo;
    }

    private static void ApplyFieldHeight(Control control)
    {
        var height = PageSetupDialogPlanner.PresentationMetrics.FieldHeight;
        control.Height = height;
        control.MinHeight = height;
        control.MaxHeight = height;
    }

    private static Control PageSetupRow(string label, Control field)
    {
        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(PageSetupDialogPlanner.PresentationMetrics.LabelColumnWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, metrics.RowInset, metrics.LabelFieldSpacing, metrics.RowInset)
        };
        field.Margin = new Thickness(0, metrics.RowInset, 0, metrics.RowInset);
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(field, 1);
        grid.Children.Add(text);
        grid.Children.Add(field);
        return grid;
    }

    private static CheckBox Check(string label, bool value, Thickness margin)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = margin };
        // Page Setup follows the compact WPF checkbox glyph and row metrics used by the authority.
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(box, DialogChromeStyle);
        return box;
    }

    string? IPageSetupDialogControlSource.MarginTopText => _top.Text;
    string? IPageSetupDialogControlSource.MarginBottomText => _bottom.Text;
    string? IPageSetupDialogControlSource.MarginLeftText => _left.Text;
    string? IPageSetupDialogControlSource.MarginRightText => _right.Text;
    string? IPageSetupDialogControlSource.GutterText => _gutter.Text;
    int IPageSetupDialogControlSource.GutterPositionIndex => _gutterPosition.SelectedIndex;
    int IPageSetupDialogControlSource.OrientationIndex => _orientation.SelectedIndex;
    int IPageSetupDialogControlSource.MultiplePagesIndex => _multiplePages.SelectedIndex;
    string? IPageSetupDialogControlSource.WidthText => _width.Text;
    string? IPageSetupDialogControlSource.HeightText => _height.Text;
    int IPageSetupDialogControlSource.PaperSizeIndex => _paperSize.SelectedIndex;
    int IPageSetupDialogControlSource.SectionStartIndex => _sectionStart.SelectedIndex;
    bool IPageSetupDialogControlSource.DifferentFirstPage => _differentFirstPage.IsChecked == true;
    bool IPageSetupDialogControlSource.DifferentOddEvenPages => _differentOddEven.IsChecked == true;
    string? IPageSetupDialogControlSource.HeaderDistanceText => _headerDistance.Text;
    string? IPageSetupDialogControlSource.FooterDistanceText => _footerDistance.Text;
    int IPageSetupDialogControlSource.VerticalAlignmentIndex => _verticalAlignment.SelectedIndex;

    private static Thickness ToThickness(PageSetupDialogThickness value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);
}
