using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    // Named paper sizes (portrait dimensions in points; 72 pt = 1 inch). "Custom" keeps whatever is typed.
    private static readonly (string Name, double WidthPt, double HeightPt)[] PaperSizes =
    [
        ("Letter (8.5\" x 11\")", 612, 792),
        ("Legal (8.5\" x 14\")", 612, 1008),
        ("Tabloid (11\" x 17\")", 792, 1224),
        ("A3 (29.7cm x 42cm)", 841.9, 1190.55),
        ("A4 (21cm x 29.7cm)", 595.3, 841.9),
        ("A5 (14.8cm x 21cm)", 419.55, 595.3),
        ("B4 (25cm x 35.3cm)", 708.7, 1000.65),
        ("B5 (17.6cm x 25cm)", 498.9, 708.7),
        ("Custom", 0, 0),
    ];

    private static readonly string[] OrientationNames = ["Portrait", "Landscape"];
    private static readonly string[] MultiplePagesNames = ["Normal", "Mirror margins"];
    private static readonly string[] ApplyToNames = ["Whole document", "This section"];

    private static readonly string[] SectionStartNames = ["Continuous", "New page", "Even page", "Odd page"];
    private static readonly SectionBreakKind[] SectionStartValues =
        [SectionBreakKind.Continuous, SectionBreakKind.NextPage, SectionBreakKind.EvenPage, SectionBreakKind.OddPage];

    private static readonly string[] VAlignNames = ["Top", "Center", "Justified", "Bottom"];
    private static readonly PageVerticalAlignment[] VAlignValues =
        [PageVerticalAlignment.Top, PageVerticalAlignment.Center, PageVerticalAlignment.Justified, PageVerticalAlignment.Bottom];

    // Margins tab.
    private readonly TextBox _top;
    private readonly TextBox _bottom;
    private readonly TextBox _left;
    private readonly TextBox _right;
    private readonly TextBox _gutter;
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

        // A landscape page stores already-swapped width/height; show the user the portrait (un-swapped)
        // dimensions in the Paper tab and recombine with orientation on accept, exactly as Word does.
        var (portraitW, portraitH) = page.Landscape
            ? (page.HeightPt, page.WidthPt)
            : (page.WidthPt, page.HeightPt);

        _top = NumberBox(page.MarginTopPt);
        _bottom = NumberBox(page.MarginBottomPt);
        _left = NumberBox(page.MarginLeftPt);
        _right = NumberBox(page.MarginRightPt);
        _gutter = NumberBox(page.GutterPt);
        _orientation = Combo(OrientationNames, page.Landscape ? 1 : 0);
        _multiplePages = Combo(MultiplePagesNames, page.MirrorMargins ? 1 : 0);
        _applyTo = Combo(ApplyToNames, 0);

        _width = NumberBox(portraitW);
        _height = NumberBox(portraitH);
        _paperSize = Combo(Array.ConvertAll(PaperSizes, p => p.Name), PaperIndexFor(portraitW, portraitH));
        _paperSize.SelectionChanged += (_, _) => ApplyPaperPreset();
        _width.TextChanged += (_, _) => SyncPaperToCustom();
        _height.TextChanged += (_, _) => SyncPaperToCustom();

        _sectionStart = Combo(SectionStartNames, Math.Max(0, Array.IndexOf(SectionStartValues, sectionStart)));
        _differentFirstPage = new CheckBox { Content = "Different first page", IsChecked = page.DifferentFirstPage };
        _differentOddEven = new CheckBox { Content = "Different odd and even", IsChecked = page.DifferentOddEvenPages, Margin = new Thickness(0, 4, 0, 0) };
        // Word defaults the header/footer distance to 0.5" (36 pt); show that when the model carries no explicit
        // value (the "unspecified" 0), so the dialog reflects the effective distance.
        _headerDistance = NumberBox(page.HeaderDistancePt > 0 ? page.HeaderDistancePt : 36);
        _footerDistance = NumberBox(page.FooterDistancePt > 0 ? page.FooterDistancePt : 36);
        _vAlign = Combo(VAlignNames, Math.Max(0, Array.IndexOf(VAlignValues, page.VerticalAlignment)));

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
        var grid = TwoColumnGrid(7);
        AddRow(grid, 0, "Top (pt):", _top);
        AddRow(grid, 1, "Bottom (pt):", _bottom);
        AddRow(grid, 2, "Left (pt):", _left);
        AddRow(grid, 3, "Right (pt):", _right);
        AddRow(grid, 4, "Gutter (pt):", _gutter);
        AddRow(grid, 5, "Orientation:", _orientation);
        AddRow(grid, 6, "Multiple pages:", _multiplePages);
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

    private static TextBox NumberBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
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

    // Maps a (width, height) back to a named paper-size index so reopening shows the current size; an unmatched
    // size resolves to "Custom" (the last entry).
    private static int PaperIndexFor(double widthPt, double heightPt)
    {
        for (var i = 0; i < PaperSizes.Length - 1; i++)
            if (Math.Abs(PaperSizes[i].WidthPt - widthPt) < 1 && Math.Abs(PaperSizes[i].HeightPt - heightPt) < 1)
                return i;
        return PaperSizes.Length - 1; // Custom
    }

    // Fills width/height from the chosen named preset (Custom leaves them as typed).
    private void ApplyPaperPreset()
    {
        var index = _paperSize.SelectedIndex;
        if (index < 0 || index >= PaperSizes.Length - 1)
            return; // Custom or none selected
        _suppressPaperSync = true;
        _width.Text = PaperSizes[index].WidthPt.ToString("0.##", CultureInfo.CurrentCulture);
        _height.Text = PaperSizes[index].HeightPt.ToString("0.##", CultureInfo.CurrentCulture);
        _suppressPaperSync = false;
    }

    // When the user edits width/height by hand, switch the dropdown to "Custom" (unless a preset was just applied).
    private void SyncPaperToCustom()
    {
        if (_suppressPaperSync)
            return;
        if (TryParse(_width.Text, out var w) && TryParse(_height.Text, out var h))
            _paperSize.SelectedIndex = PaperIndexFor(w, h);
    }

    private void Accept()
    {
        if (!TryParse(_top.Text, out var top) || top < 0
            || !TryParse(_bottom.Text, out var bottom) || bottom < 0
            || !TryParse(_left.Text, out var left) || left < 0
            || !TryParse(_right.Text, out var right) || right < 0
            || !TryParse(_gutter.Text, out var gutter) || gutter < 0
            || !TryParse(_width.Text, out var width) || width <= 0
            || !TryParse(_height.Text, out var height) || height <= 0
            || !TryParse(_headerDistance.Text, out var headerDistance) || headerDistance < 0
            || !TryParse(_footerDistance.Text, out var footerDistance) || footerDistance < 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter non-negative margins/distances and a positive page width and height (in points).");
            return;
        }

        var landscape = _orientation.SelectedIndex == 1;
        // Width/height are entered portrait-first; swap them for landscape so the stored geometry is oriented.
        var (storedW, storedH) = landscape ? (height, width) : (width, height);

        _result = new Result(
            MarginTopPt: top,
            MarginBottomPt: bottom,
            MarginLeftPt: left,
            MarginRightPt: right,
            GutterPt: gutter,
            Landscape: landscape,
            MirrorMargins: _multiplePages.SelectedIndex == 1,
            WidthPt: storedW,
            HeightPt: storedH,
            SectionStart: SectionStartValues[Math.Max(0, _sectionStart.SelectedIndex)],
            DifferentFirstPage: _differentFirstPage.IsChecked == true,
            DifferentOddEvenPages: _differentOddEven.IsChecked == true,
            HeaderDistancePt: headerDistance,
            FooterDistancePt: footerDistance,
            VerticalAlignment: VAlignValues[Math.Max(0, _vAlign.SelectedIndex)]);
        Close();
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

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
