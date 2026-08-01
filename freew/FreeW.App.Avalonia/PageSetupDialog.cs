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

    private readonly TextBox _top;
    private readonly TextBox _bottom;
    private readonly TextBox _left;
    private readonly TextBox _right;
    private readonly TextBox _gutter;
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

    public PageSetupDialog(PageSettings current, PageSetupDialogTab initialTab = PageSetupDialogTab.Margins)
    {
        ArgumentNullException.ThrowIfNull(current);
        PageLayoutDialogChrome.Configure(this, PageSetupDialogPlanner.Title, 420);

        var state = PageSetupDialogPlanner.BuildInitialState(
            current,
            SectionBreakKind.NextPage,
            PageSetupDialogPlanner.HostPaperOptions,
            PageSetupGeometryMode.PortraitInputSwappedWhenLandscape,
            DialogCulture);
        _top = NumberBox(state.MarginTopText);
        _bottom = NumberBox(state.MarginBottomText);
        _left = NumberBox(state.MarginLeftText);
        _right = NumberBox(state.MarginRightText);
        _gutter = NumberBox(state.GutterText);
        _orientation = Combo(PageSetupDialogPlanner.OrientationNames, state.OrientationIndex);
        _multiplePages = Combo(PageSetupDialogPlanner.MultiplePagesNames, state.MultiplePagesIndex);
        _applyTo = Combo(PageSetupDialogPlanner.ApplyToNames, 0);
        _paperSize = Combo(PageSetupDialogPlanner.HostPaperOptions.Select(option => option.HostLabel), state.PaperSizeIndex);
        _width = NumberBox(state.WidthText);
        _height = NumberBox(state.HeightText);
        _sectionStart = Combo(PageSetupDialogPlanner.SectionStartNames, state.SectionStartIndex);
        _differentFirstPage = Check("Different first page", state.DifferentFirstPage, new Thickness(0));
        _differentOddEven = Check("Different odd and even", state.DifferentOddEvenPages, new Thickness(0, 4, 0, 0));
        _headerDistance = NumberBox(state.HeaderDistanceText);
        _footerDistance = NumberBox(state.FooterDistanceText);
        _verticalAlignment = Combo(PageSetupDialogPlanner.VerticalAlignmentNames, state.VerticalAlignmentIndex);

        _paperSize.SelectionChanged += (_, _) => ApplyPaperPreset();
        _width.TextChanged += (_, _) => SyncPaperToCustom();
        _height.TextChanged += (_, _) => SyncPaperToCustom();

        var tabs = new TabControl
        {
            Margin = new Thickness(14, 14, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        tabs.Items.Add(new TabItem { Header = "Margins", Content = BuildMarginsTab() });
        tabs.Items.Add(new TabItem { Header = "Paper", Content = BuildPaperTab() });
        tabs.Items.Add(new TabItem { Header = "Layout", Content = BuildLayoutTab() });
        tabs.SelectedIndex = (int)initialTab;
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            tabs,
            DialogChromeStyle,
            contentPaneMargin: new Thickness(-12, -2, -12, 0));

        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(16, 8, 16, 0));
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto")
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRow(_status, 1);
        var actions = PageLayoutDialogChrome.Actions(Accept, () => Close(null), buttonWidth: 72);
        actions.Margin = new Thickness(14, 12, 14, 12);
        Grid.SetRow(actions, 2);
        // Keep the logical/visual child order aligned with the WPF authority: the action row
        // precedes the tab content even though Grid rows place it at the bottom on screen.
        root.Children.Add(actions);
        root.Children.Add(tabs);
        root.Children.Add(_status);
        Content = root;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_top);
        PageLayoutDialogChrome.WireEscape<PageSetupDialogOutcome?>(this);
    }

    private Control BuildMarginsTab()
    {
        var panel = TabPanel();
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.TopMarginLabel, _top));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.BottomMarginLabel, _bottom));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.LeftMarginLabel, _left));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.RightMarginLabel, _right));
        panel.Children.Add(PageSetupRow("Gutter (pt):", _gutter));
        panel.Children.Add(PageSetupRow("Orientation:", _orientation));
        panel.Children.Add(PageSetupRow("Multiple pages:", _multiplePages));
        panel.Children.Add(PageSetupRow("Apply to:", _applyTo));
        return panel;
    }

    private Control BuildPaperTab()
    {
        var panel = TabPanel();
        panel.Children.Add(PageSetupRow("Paper size:", _paperSize));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.CustomWidthLabel, _width));
        panel.Children.Add(PageSetupRow(PageSetupDialogPlanner.CustomHeightLabel, _height));
        return panel;
    }

    private Control BuildLayoutTab()
    {
        var panel = TabPanel();
        panel.Children.Add(PageSetupRow("Section start:", _sectionStart));
        panel.Children.Add(PageSetupRow("Vertical alignment:", _verticalAlignment));
        panel.Children.Add(PageSetupRow("Header from edge (pt):", _headerDistance));
        panel.Children.Add(PageSetupRow("Footer from edge (pt):", _footerDistance));

        var checks = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        checks.Children.Add(_differentFirstPage);
        checks.Children.Add(_differentOddEven);
        panel.Children.Add(checks);

        var launchers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        var lineNumbers = new Button { Content = "Line Numbers\u2026" };
        var borders = new Button { Content = "Borders\u2026" };
        AvaloniaCompactDialogChrome.ApplyButton(lineNumbers, DialogChromeStyle, minWidth: 110);
        AvaloniaCompactDialogChrome.ApplyButton(borders, DialogChromeStyle, minWidth: 110);
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
            UseSelectedPaperPreset: false,
            GeometryMode: PageSetupGeometryMode.PortraitInputSwappedWhenLandscape,
            ValidationProfile: PageSetupValidationProfile.UnifiedDialog);
        if (!PageSetupDialogPlanner.TryBuildResult(
                input,
                PageSetupDialogPlanner.HostPaperOptions,
                DialogCulture,
                out var result,
                out var error))
        {
            PageLayoutDialogChrome.ShowError(_status, error ?? PageSetupDialogPlanner.UnifiedValidationMessage);
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

    private static StackPanel TabPanel() => new() { Margin = new Thickness(14) };

    private static TextBox NumberBox(string value) => PageLayoutDialogChrome.NumberBox(value, 120, stretch: true);

    private static ComboBox Combo(IEnumerable<string> values, int selectedIndex)
    {
        var combo = PageLayoutDialogChrome.Combo(values, selectedIndex, 180);
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        return combo;
    }

    private static Control PageSetupRow(string label, Control field)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        field.Margin = new Thickness(0, 4, 0, 4);
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
}
