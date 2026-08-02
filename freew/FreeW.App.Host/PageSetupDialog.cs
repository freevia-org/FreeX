using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's unified "Page Setup" dialog (Layout &gt; Page Setup launcher). Edits the active section's
/// <see cref="PageSettings"/> across three tabs, mirroring Word's layout:
/// <list type="bullet">
/// <item>Margins — top/bottom/left/right margins, the binding gutter, orientation (portrait/landscape) and the
/// mirror-margins option, plus the "apply to" scope (whole document / this section).</item>
/// <item>Paper — a named paper-size dropdown (Letter / Legal / Tabloid / A3 / A4 / A5 / B4 / B5 / Custom) with
/// editable custom width/height.</item>
/// <item>Layout — the section start (continuous / new page / even page / odd page), different-first-page and
/// odd/even header-footer toggles, the header/footer distance from the page edge, vertical alignment, and the
/// Line Numbers… / Borders… launchers (which defer to FreeW's existing Line Numbers cycle and Borders and
/// Shading dialog).</item>
/// </list>
///
/// <para>
/// The dialog only produces a <see cref="Result"/>; the ribbon command applies it through
/// <see cref="FreeW.App.Host.Editing.DocumentView.ApplyPageSettings"/> — the same single commit + re-render path
/// every other FreeW page-setup command uses — so all the edited values round-trip through the existing w:sectPr /
/// settings.xml writers (pgSz, pgMar gutter/header/footer, vAlign, titlePg, mirrorMargins, evenAndOddHeaders).
/// Measurements are shown in points, matching FreeW's other page-setup dialogs (Columns, Hyphenation Options).
/// </para>
/// </summary>
internal sealed class PageSetupDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>Which paper-size tab the dialog should open on (Margins by default).</summary>
    internal enum Tab { Margins, Paper, Layout }

    /// <summary>The settings the dialog produces, applied onto the active section's <see cref="PageSettings"/>.</summary>
    internal sealed record Result(
        double MarginTopPt,
        double MarginBottomPt,
        double MarginLeftPt,
        double MarginRightPt,
        double GutterPt,
        bool GutterAtTop,
        bool Landscape,
        bool MirrorMargins,
        double WidthPt,
        double HeightPt,
        SectionBreakKind SectionStart,
        bool DifferentFirstPage,
        bool DifferentOddEvenPages,
        double HeaderDistancePt,
        double FooterDistancePt,
        PageVerticalAlignment VerticalAlignment);

    // Margins tab.
    private readonly TextBox _top;
    private readonly TextBox _bottom;
    private readonly TextBox _left;
    private readonly TextBox _right;
    private readonly TextBox _gutter;
    private readonly ComboBox _gutterPosition;
    private readonly ComboBox _orientation;
    private readonly ComboBox _multiplePages;
    private readonly ComboBox _applyTo;

    // Paper tab.
    private readonly ComboBox _paperSize;
    private readonly TextBox _width;
    private readonly TextBox _height;
    private bool _suppressPaperSync;

    // Layout tab.
    private readonly ComboBox _sectionStart;
    private readonly CheckBox _differentFirstPage;
    private readonly CheckBox _differentOddEven;
    private readonly TextBox _headerDistance;
    private readonly TextBox _footerDistance;
    private readonly ComboBox _vAlign;

    private readonly Window? _owner;
    private Result? _result;
    private bool _lineNumbersRequested;
    private bool _bordersRequested;

    /// <summary>True when the user clicked the Line Numbers… launcher and accepted the dialog.</summary>
    public bool LineNumbersRequested => _lineNumbersRequested;

    /// <summary>True when the user clicked the Borders… launcher and accepted the dialog.</summary>
    public bool BordersRequested => _bordersRequested;

    private PageSetupDialog(Window? owner, PageSettings page, SectionBreakKind sectionStart, Tab initialTab)
    {
        _owner = owner;
        Owner = owner;
        Title = "Page Setup";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var state = PageSetupDialogPlanner.BuildInitialState(
            page,
            sectionStart,
            PageSetupDialogPlanner.HostPaperOptions,
            PageSetupGeometryMode.PortraitInputSwappedWhenLandscape,
            CultureInfo.CurrentCulture);

        _top = NumberBox(state.MarginTopText);
        _bottom = NumberBox(state.MarginBottomText);
        _left = NumberBox(state.MarginLeftText);
        _right = NumberBox(state.MarginRightText);
        _gutter = NumberBox(state.GutterText);
        _gutterPosition = Combo(PageSetupDialogPlanner.GutterPositionNames.ToArray(), state.GutterPositionIndex);
        _orientation = Combo(PageSetupDialogPlanner.OrientationNames.ToArray(), state.OrientationIndex);
        _multiplePages = Combo(PageSetupDialogPlanner.MultiplePagesNames.ToArray(), state.MultiplePagesIndex);
        _applyTo = Combo(PageSetupDialogPlanner.ApplyToNames.ToArray(), 0);

        _width = NumberBox(state.WidthText);
        _height = NumberBox(state.HeightText);
        _paperSize = Combo(PageSetupDialogPlanner.HostPaperOptions.Select(p => p.HostLabel).ToArray(), state.PaperSizeIndex);
        _paperSize.SelectionChanged += (_, _) => ApplyPaperPreset();
        _width.TextChanged += (_, _) => SyncPaperToCustom();
        _height.TextChanged += (_, _) => SyncPaperToCustom();

        _sectionStart = Combo(PageSetupDialogPlanner.SectionStartNames.ToArray(), state.SectionStartIndex);
        _differentFirstPage = new CheckBox { Content = "Different first page", IsChecked = state.DifferentFirstPage };
        _differentOddEven = new CheckBox { Content = "Different odd and even", IsChecked = state.DifferentOddEvenPages, Margin = new Thickness(0, 4, 0, 0) };
        _headerDistance = NumberBox(state.HeaderDistanceText);
        _footerDistance = NumberBox(state.FooterDistanceText);
        _vAlign = Combo(PageSetupDialogPlanner.VerticalAlignmentNames.ToArray(), state.VerticalAlignmentIndex);

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        tabs.Items.Add(new TabItem { Header = "Margins", Content = BuildMarginsTab() });
        tabs.Items.Add(new TabItem { Header = "Paper", Content = BuildPaperTab() });
        tabs.Items.Add(new TabItem { Header = "Layout", Content = BuildLayoutTab() });
        tabs.SelectedIndex = (int)initialTab;

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(14, 12, 14, 12));

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;

        DialogFocus.FocusAndSelect(_top);
    }

    private UIElement BuildMarginsTab()
    {
        var grid = TwoColumnGrid(8);
        AddRow(grid, 0, "Top (pt):", _top);
        AddRow(grid, 1, "Bottom (pt):", _bottom);
        AddRow(grid, 2, "Left (pt):", _left);
        AddRow(grid, 3, "Right (pt):", _right);
        AddRow(grid, 4, "Gutter (pt):", _gutter);
        AddRow(grid, 5, PageSetupDialogPlanner.GutterPositionLabel, _gutterPosition);
        AddRow(grid, 6, "Orientation:", _orientation);
        AddRow(grid, 7, "Multiple pages:", _multiplePages);
        // "Apply to" is shown on every tab in Word; here it lives at the foot of the Margins tab.
        var applyGrid = TwoColumnGrid(1);
        AddRow(applyGrid, 0, "Apply to:", _applyTo);
        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(grid);
        stack.Children.Add(applyGrid);
        return stack;
    }

    private UIElement BuildPaperTab()
    {
        var grid = TwoColumnGrid(3);
        AddRow(grid, 0, "Paper size:", _paperSize);
        AddRow(grid, 1, "Width (pt):", _width);
        AddRow(grid, 2, "Height (pt):", _height);
        return new StackPanel { Margin = new Thickness(14), Children = { grid } };
    }

    private UIElement BuildLayoutTab()
    {
        var grid = TwoColumnGrid(4);
        AddRow(grid, 0, "Section start:", _sectionStart);
        AddRow(grid, 1, "Vertical alignment:", _vAlign);
        AddRow(grid, 2, "Header from edge (pt):", _headerDistance);
        AddRow(grid, 3, "Footer from edge (pt):", _footerDistance);

        var checks = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        checks.Children.Add(_differentFirstPage);
        checks.Children.Add(_differentOddEven);

        // Line Numbers… / Borders… launchers, matching Word's Layout tab. Each sets a flag and accepts the
        // dialog so the ribbon command can open the corresponding existing FreeW feature afterwards.
        var lineNumbers = new Button { Content = "_Line Numbers…", MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        lineNumbers.Click += (_, _) => { _lineNumbersRequested = true; Accept(); };
        var borders = new Button { Content = "_Borders…", MinWidth = 110 };
        borders.Click += (_, _) => { _bordersRequested = true; Accept(); };
        var launchers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { lineNumbers, borders }
        };

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(grid);
        stack.Children.Add(checks);
        stack.Children.Add(launchers);
        return stack;
    }

    private static Grid TwoColumnGrid(int rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static ComboBox Combo(string[] items, int selectedIndex)
    {
        var combo = new ComboBox { MinWidth = 180 };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        return combo;
    }

    private static TextBox NumberBox(string value) => new()
    {
        Text = value,
        MinWidth = 120
    };

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    // Fills width/height from the chosen named preset (Custom leaves them as typed).
    private void ApplyPaperPreset()
    {
        var preset = PageSetupDialogPlanner.ApplyPaperPreset(
            PageSetupDialogPlanner.HostPaperOptions,
            _paperSize.SelectedIndex,
            CultureInfo.CurrentCulture);
        if (preset is null)
            return;

        _suppressPaperSync = true;
        _width.Text = preset.Value.WidthText;
        _height.Text = preset.Value.HeightText;
        _suppressPaperSync = false;
    }

    // When the user edits width/height by hand, switch the dropdown to "Custom" (unless a preset was just applied).
    private void SyncPaperToCustom()
    {
        if (_suppressPaperSync)
            return;
        if (TryParse(_width.Text, out var w) && TryParse(_height.Text, out var h))
            _paperSize.SelectedIndex = PageSetupDialogPlanner.PaperIndexFor(PageSetupDialogPlanner.HostPaperOptions, w, h);
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
            VerticalAlignmentIndex: _vAlign.SelectedIndex,
            UseSelectedPaperPreset: false,
            GeometryMode: PageSetupGeometryMode.PortraitInputSwappedWhenLandscape,
            ValidationProfile: PageSetupValidationProfile.UnifiedDialog,
            GutterPositionIndex: _gutterPosition.SelectedIndex);

        if (!PageSetupDialogPlanner.TryBuildResult(
                input,
                PageSetupDialogPlanner.HostPaperOptions,
                CultureInfo.CurrentCulture,
                out var planned,
                out var error))
        {
            DialogMessageHelper.ShowWarning(this, error ?? PageSetupDialogPlanner.UnifiedValidationMessage);
            return;
        }

        _result = ToHostResult(planned!);
        Close();
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static Result ToHostResult(PageSetupDialogResult result) => new(
        MarginTopPt: result.MarginTopPt,
        MarginBottomPt: result.MarginBottomPt,
        MarginLeftPt: result.MarginLeftPt,
        MarginRightPt: result.MarginRightPt,
        GutterPt: result.GutterPt,
        GutterAtTop: result.GutterAtTop,
        Landscape: result.Landscape,
        MirrorMargins: result.MirrorMargins,
        WidthPt: result.WidthPt,
        HeightPt: result.HeightPt,
        SectionStart: result.SectionStart,
        DifferentFirstPage: result.DifferentFirstPage,
        DifferentOddEvenPages: result.DifferentOddEvenPages,
        HeaderDistancePt: result.HeaderDistancePt,
        FooterDistancePt: result.FooterDistancePt,
        VerticalAlignment: result.VerticalAlignment);

    internal static PageSetupDialogResult ToPresentationResult(Result result) => new(
        MarginTopPt: result.MarginTopPt,
        MarginBottomPt: result.MarginBottomPt,
        MarginLeftPt: result.MarginLeftPt,
        MarginRightPt: result.MarginRightPt,
        GutterPt: result.GutterPt,
        GutterAtTop: result.GutterAtTop,
        Landscape: result.Landscape,
        MirrorMargins: result.MirrorMargins,
        WidthPt: result.WidthPt,
        HeightPt: result.HeightPt,
        SectionStart: result.SectionStart,
        DifferentFirstPage: result.DifferentFirstPage,
        DifferentOddEvenPages: result.DifferentOddEvenPages,
        HeaderDistancePt: result.HeaderDistancePt,
        FooterDistancePt: result.FooterDistancePt,
        VerticalAlignment: result.VerticalAlignment);

    /// <summary>
    /// Test seam: builds a non-modal dialog instance seeded from <paramref name="page"/> so unit tests can
    /// exercise the control wiring (seeding, the paper-preset / orientation mapping) without a modal loop.
    /// </summary>
    internal static PageSetupDialog CreateForTest(PageSettings page, Tab initialTab = Tab.Margins) =>
        new(owner: null, page, SectionBreakKind.NextPage, initialTab);

    /// <summary>
    /// Test seam: validates the current control values and returns the <see cref="Result"/> they describe
    /// (or null when validation fails), without closing the window — the same mapping <see cref="Accept"/>
    /// performs.
    /// </summary>
    internal Result? AcceptForTest()
    {
        Accept();
        return _result;
    }

    /// <summary>
    /// Show the Page Setup dialog seeded with the active section's <paramref name="page"/> settings (and its
    /// <paramref name="sectionStart"/> break kind), opened on <paramref name="initialTab"/>. Returns the chosen
    /// settings plus the dialog instance (so the caller can inspect the Line Numbers… / Borders… launcher
    /// flags), or null if cancelled.
    /// </summary>
    public static (Result Settings, bool LineNumbers, bool Borders)? Prompt(
        Window? owner, PageSettings page, SectionBreakKind sectionStart = SectionBreakKind.NextPage, Tab initialTab = Tab.Margins)
    {
        var dialog = new PageSetupDialog(owner, page, sectionStart, initialTab);
        dialog.ShowDialog();
        return dialog._result is { } result
            ? (result, dialog.LineNumbersRequested, dialog.BordersRequested)
            : null;
    }
}
