using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Localization;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

public enum PageSetupDialogTab
{
    Margins,
    Paper,
    Layout
}

public sealed record PageSetupDialogOutcome(
    PageSetupDialogResult Settings,
    bool LineNumbersRequested,
    bool BordersRequested);

/// <summary>Avalonia chrome for the shared WPF-authoritative three-tab Page Setup contract.</summary>
public sealed class PageSetupDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = PageLayoutDialogChrome.Style;
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;
    // The authority capture is rendered at 96 DPI with the WPF dialog's compact tab metrics.
    // Avalonia's Fluent header measures these three English labels differently on Linux, so keep
    // the route's tab geometry explicit while the shared chrome still owns colors and templates.
    private static readonly double[] AuthorityTabWidths = [59, 40, 48];
    private const double AuthorityActionSpacing = 14;
    private const double AuthorityActionRightInset = 15;
    private const double AuthorityLauncherLeftInset = -1;
    private const double AuthorityLauncherSpacing = 14;

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
    private bool _suppressPaperSync;
    private bool _lineNumbersRequested;
    private bool _bordersRequested;

    public PageSetupDialog(
        PageSettings current,
        PageSetupDialogTab initialTab = PageSetupDialogTab.Margins,
        SectionBreakKind sectionStart = SectionBreakKind.NextPage)
    {
        ArgumentNullException.ThrowIfNull(current);
        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var validation = metrics.Validation;
        PageLayoutDialogChrome.Configure(this, PageSetupDialogPlanner.Title, metrics.WindowWidth);

        var state = PageSetupDialogPlanner.BuildInitialState(
            current,
            sectionStart,
            PageSetupDialogPlanner.HostPaperOptions,
            validation.GeometryMode,
            DialogCulture);
        _top = NumberBox(state.MarginTopText);
        _bottom = NumberBox(state.MarginBottomText);
        _left = NumberBox(state.MarginLeftText);
        _right = NumberBox(state.MarginRightText);
        _gutter = NumberBox(state.GutterText);
        _gutterPosition = Combo(PageSetupDialogPlanner.GutterPositionNames, state.GutterPositionIndex);
        _orientation = Combo(PageSetupDialogPlanner.OrientationNames, state.OrientationIndex);
        _multiplePages = Combo(PageSetupDialogPlanner.MultiplePagesNames, state.MultiplePagesIndex);
        _applyTo = Combo(PageSetupDialogPlanner.ApplyToNames, 0);
        _paperSize = Combo(PageSetupDialogPlanner.HostPaperOptions.Select(option => option.HostLabel), state.PaperSizeIndex);
        _width = NumberBox(state.WidthText);
        _height = NumberBox(state.HeightText);
        _sectionStart = Combo(PageSetupDialogPlanner.SectionStartNames, state.SectionStartIndex);
        _differentFirstPage = Check(PageSetupDialogPlanner.DifferentFirstPageLabel, state.DifferentFirstPage, new Thickness(0));
        _differentOddEven = Check(PageSetupDialogPlanner.DifferentOddEvenLabel, state.DifferentOddEvenPages, ToThickness(metrics.SecondCheckMargin));
        _headerDistance = NumberBox(state.HeaderDistanceText);
        _footerDistance = NumberBox(state.FooterDistanceText);
        _verticalAlignment = Combo(PageSetupDialogPlanner.VerticalAlignmentNames, state.VerticalAlignmentIndex);

        _paperSize.SelectionChanged += (_, _) => ApplyPaperPreset();
        _width.TextChanged += (_, _) => SyncPaperToCustom();
        _height.TextChanged += (_, _) => SyncPaperToCustom();

        var tabs = new TabControl
        {
            Margin = ToThickness(metrics.TabMargin),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        tabs.Items.Add(new TabItem { Header = metrics.TabNames[0], Content = BuildMarginsTab() });
        tabs.Items.Add(new TabItem { Header = metrics.TabNames[1], Content = BuildPaperTab() });
        tabs.Items.Add(new TabItem { Header = metrics.TabNames[2], Content = BuildLayoutTab() });
        tabs.SelectedIndex = (int)initialTab;
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            tabs,
            DialogChromeStyle,
            contentPaneMargin: ToThickness(metrics.TabPaneMargin));
        for (var index = 0; index < tabs.Items.Count && index < AuthorityTabWidths.Length; index++)
            ((TabItem)tabs.Items[index]!).Width = AuthorityTabWidths[index];

        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(16, 8, 16, 0));
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto")
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRow(_status, 1);
        var actionStyle = DialogChromeStyle with
        {
            ActionSpacing = AuthorityActionSpacing
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
            AuthorityActionRightInset,
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
            PageLayoutDialogChrome.FocusAndSelect(_top);
        };
        PageLayoutDialogChrome.WireEscape<PageSetupDialogOutcome?>(this);
    }

    private Control BuildMarginsTab()
    {
        var panel = TabPanel();
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.TopMarginLabel, _top));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.BottomMarginLabel, _bottom));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.LeftMarginLabel, _left));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.RightMarginLabel, _right));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.GutterLabel, _gutter));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.GutterPositionLabel, _gutterPosition));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.OrientationLabel, _orientation));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.MultiplePagesLabel, _multiplePages));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.ApplyToLabel, _applyTo));
        return panel;
    }

    private Control BuildPaperTab()
    {
        var panel = TabPanel();
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.PaperSizeLabel, _paperSize));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.CustomWidthLabel, _width));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.CustomHeightLabel, _height));
        return panel;
    }

    private Control BuildLayoutTab()
    {
        var panel = TabPanel();
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.SectionStartLabel, _sectionStart));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.VerticalAlignmentLabel, _verticalAlignment));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.HeaderDistanceLabel, _headerDistance));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.FooterDistanceLabel, _footerDistance));

        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var checks = new StackPanel { Margin = new Thickness(0, metrics.CheckGroupTopSpacing, 0, 0) };
        checks.Children.Add(_differentFirstPage);
        checks.Children.Add(_differentOddEven);
        panel.Children.Add(checks);

        var launchers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = AuthorityLauncherSpacing,
            Margin = new Thickness(AuthorityLauncherLeftInset, metrics.LauncherTopSpacing, 0, 0)
        };
        var lineNumbers = new Button { Content = PageSetupDialogPlanner.LineNumbersLabel };
        var borders = new Button { Content = PageSetupDialogPlanner.BordersLabel };
        AvaloniaCompactDialogChrome.ApplyButton(lineNumbers, DialogChromeStyle, minWidth: metrics.LauncherButtonWidth);
        AvaloniaCompactDialogChrome.ApplyButton(borders, DialogChromeStyle, minWidth: metrics.LauncherButtonWidth);
        lineNumbers.Click += (_, _) =>
        {
            _lineNumbersRequested = true;
            Accept();
        };
        borders.Click += (_, _) =>
        {
            _bordersRequested = true;
            Accept();
        };
        launchers.Children.Add(lineNumbers);
        launchers.Children.Add(borders);
        panel.Children.Add(launchers);
        return panel;
    }

    private void ApplyPaperPreset()
    {
        var preset = PageSetupDialogPlanner.ApplyPaperPreset(
            PageSetupDialogPlanner.HostPaperOptions,
            _paperSize.SelectedIndex,
            DialogCulture);
        if (preset is null)
            return;

        _suppressPaperSync = true;
        _width.Text = preset.Value.WidthText;
        _height.Text = preset.Value.HeightText;
        _suppressPaperSync = false;
    }

    private void SyncPaperToCustom()
    {
        if (_suppressPaperSync ||
            !double.TryParse(_width.Text, NumberStyles.Float, DialogCulture, out var width) ||
            !double.TryParse(_height.Text, NumberStyles.Float, DialogCulture, out var height))
            return;

        _paperSize.SelectedIndex = PageSetupDialogPlanner.PaperIndexFor(
            PageSetupDialogPlanner.HostPaperOptions,
            width,
            height);
    }

    private void Accept()
    {
        var validation = PageSetupDialogPlanner.PresentationMetrics.Validation;
        var input = new PageSetupDialogInput(
            MarginTopText: _top.Text,
            MarginBottomText: _bottom.Text,
            MarginLeftText: _left.Text,
            MarginRightText: _right.Text,
            GutterText: _gutter.Text,
            OrientationIndex: _orientation.SelectedIndex,
            MultiplePagesIndex: _multiplePages.SelectedIndex,
            WidthText: _width.Text,
            HeightText: _height.Text,
            PaperSizeIndex: _paperSize.SelectedIndex,
            SectionStartIndex: _sectionStart.SelectedIndex,
            DifferentFirstPage: _differentFirstPage.IsChecked == true,
            DifferentOddEvenPages: _differentOddEven.IsChecked == true,
            HeaderDistanceText: _headerDistance.Text,
            FooterDistanceText: _footerDistance.Text,
            VerticalAlignmentIndex: _verticalAlignment.SelectedIndex,
            UseSelectedPaperPreset: validation.UseSelectedPaperPreset,
            GeometryMode: validation.GeometryMode,
            ValidationProfile: validation.ValidationProfile,
            GutterPositionIndex: _gutterPosition.SelectedIndex);
        if (!PageSetupDialogPlanner.TryBuildResult(
                input,
                PageSetupDialogPlanner.HostPaperOptions,
                DialogCulture,
                out var result,
                out var error))
        {
            PageLayoutDialogChrome.ShowError(_status, error ?? validation.Message);
            return;
        }

        Close(new PageSetupDialogOutcome(result!, _lineNumbersRequested, _bordersRequested));
    }

    public static void ApplyResult(DocumentView editor, PageSetupDialogResult result) =>
        editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyPageSetupResult(page, result));

    public static async Task ShowAndApplyAsync(
        Window owner,
        DocumentView editor,
        PageSetupDialogTab initialTab = PageSetupDialogTab.Margins,
        Func<Task>? openLineNumbers = null,
        Func<Task>? openBorders = null)
    {
        var outcome = await new PageSetupDialog(editor.Document.Page, initialTab)
            .ShowDialog<PageSetupDialogOutcome?>(owner);
        if (outcome is null)
            return;

        ApplyResult(editor, outcome.Settings);
        if (outcome.LineNumbersRequested && openLineNumbers is not null)
            await openLineNumbers();
        else if (outcome.BordersRequested && openBorders is not null)
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

    private static Thickness ToThickness(PageSetupDialogThickness value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);
}
