using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
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
/// The dialog only produces a <see cref="PageSetupDialogResult"/>; the ribbon command applies it through
/// <see cref="FreeW.App.Host.Editing.DocumentView.ApplyPageSettings"/> — the same single commit + re-render path
/// every other FreeW page-setup command uses — so all the edited values round-trip through the existing w:sectPr /
/// settings.xml writers (pgSz, pgMar gutter/header/footer, vAlign, titlePg, mirrorMargins, evenAndOddHeaders).
/// Measurements are shown in points, matching FreeW's other page-setup dialogs (Columns, Hyphenation Options).
/// </para>
/// </summary>
internal sealed class PageSetupDialog : Free.Shared.Ribbon.Wpf.DialogWindow, IPageSetupDialogControlSource
{
    /// <summary>Which paper-size tab the dialog should open on (Margins by default).</summary>
    internal enum Tab { Margins, Paper, Layout }

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
    private readonly PageSetupDialogSession _session;
    private PageSetupDialogResult? _result;
    private PageSetupDialogFollowUp _acceptedFollowUp;

    /// <summary>True when the user clicked the Line Numbers… launcher and accepted the dialog.</summary>
    public bool LineNumbersRequested => _acceptedFollowUp == PageSetupDialogFollowUp.LineNumbers;

    /// <summary>True when the user clicked the Borders… launcher and accepted the dialog.</summary>
    public bool BordersRequested => _acceptedFollowUp == PageSetupDialogFollowUp.Borders;

    private PageSetupDialog(Window? owner, PageSettings page, SectionBreakKind sectionStart, Tab initialTab)
    {
        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var surface = PageSetupDialogPlanner.Surface;
        _owner = owner;
        Owner = owner;
        Title = surface.Title;
        Width = metrics.WindowWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _session = PageSetupDialogPlanner.CreateSession(
            page,
            sectionStart,
            CultureInfo.CurrentCulture);
        var state = _session.InitialState;

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
        _paperSize = Combo(_session.PaperOptions.Select(p => p.HostLabel).ToArray(), state.PaperSizeIndex);
        ApplyEnabledState(_session.EnabledState);
        _paperSize.SelectionChanged += (_, _) => ApplyPaperPreset();
        _width.TextChanged += (_, _) => SyncPaperToCustom();
        _height.TextChanged += (_, _) => SyncPaperToCustom();

        _sectionStart = Combo(PageSetupDialogPlanner.SectionStartNames.ToArray(), state.SectionStartIndex);
        _differentFirstPage = new CheckBox { Content = surface.LayoutToggles[0].Label, IsChecked = state.DifferentFirstPage };
        _differentOddEven = new CheckBox { Content = surface.LayoutToggles[1].Label, IsChecked = state.DifferentOddEvenPages, Margin = ToThickness(metrics.SecondCheckMargin) };
        _headerDistance = NumberBox(state.HeaderDistanceText);
        _footerDistance = NumberBox(state.FooterDistanceText);
        _vAlign = Combo(PageSetupDialogPlanner.VerticalAlignmentNames.ToArray(), state.VerticalAlignmentIndex);

        var tabs = new TabControl { Margin = ToThickness(metrics.TabMargin) };
        foreach (var tabSpec in surface.Tabs)
        {
            var tab = new TabItem { Header = tabSpec.Header, Content = BuildTab(tabSpec) };
            AutomationProperties.SetAutomationId(tab, tabSpec.AutomationId);
            tabs.Items.Add(tab);
        }
        tabs.SelectedIndex = (int)initialTab;

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: metrics.ActionButtonWidth, rowMargin: ToThickness(metrics.ActionRowMargin));

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;

        ApplyFocus(_session.InitialFocusPlan);
    }

    private UIElement BuildTab(PageSetupDialogTabSpec tab)
    {
        var grid = TwoColumnGrid(tab.Rows.Count);
        for (var index = 0; index < tab.Rows.Count; index++)
            AddRow(grid, index, tab.Rows[index].Label, ControlFor(tab.Rows[index].Kind));
        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var stack = new StackPanel { Margin = ToThickness(metrics.TabContentMargin) };
        stack.Children.Add(grid);
        if (tab.Kind != PageSetupDialogTabKind.Layout)
            return stack;

        var checks = new StackPanel { Margin = new Thickness(0, metrics.CheckGroupTopSpacing, 0, 0) };
        checks.Children.Add(_differentFirstPage);
        checks.Children.Add(_differentOddEven);
        stack.Children.Add(checks);

        var launchers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, metrics.LauncherTopSpacing, 0, 0),
        };
        foreach (var launcher in PageSetupDialogPlanner.Surface.LayoutLaunchers)
        {
            var button = new Button { Content = "_" + launcher.Label, MinWidth = metrics.LauncherButtonWidth };
            if (launchers.Children.Count == 0)
                button.Margin = new Thickness(0, 0, metrics.LauncherSpacing, 0);
            button.Click += (_, _) => Accept(launcher.FollowUp);
            launchers.Children.Add(button);
        }
        stack.Children.Add(launchers);
        return stack;
    }

    private UIElement ControlFor(PageSetupDialogControlKind kind) => kind switch
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
        PageSetupDialogControlKind.VerticalAlignment => _vAlign,
        PageSetupDialogControlKind.HeaderDistance => _headerDistance,
        PageSetupDialogControlKind.FooterDistance => _footerDistance,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static Grid TwoColumnGrid(int rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PageSetupDialogPlanner.PresentationMetrics.LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static ComboBox Combo(string[] items, int selectedIndex)
    {
        var metrics = PageSetupDialogPlanner.PresentationMetrics;
        var combo = new ComboBox
        {
            MinWidth = metrics.ComboBoxMinWidth,
            Height = metrics.FieldHeight,
            MinHeight = metrics.FieldHeight,
            MaxHeight = metrics.FieldHeight
        };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        return combo;
    }

    private static TextBox NumberBox(string value) => new()
    {
        Text = value,
        MinWidth = PageSetupDialogPlanner.PresentationMetrics.NumberBoxMinWidth,
        Height = PageSetupDialogPlanner.PresentationMetrics.FieldHeight,
        MinHeight = PageSetupDialogPlanner.PresentationMetrics.FieldHeight,
        MaxHeight = PageSetupDialogPlanner.PresentationMetrics.FieldHeight
    };

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, PageSetupDialogPlanner.PresentationMetrics.RowInset, PageSetupDialogPlanner.PresentationMetrics.LabelFieldSpacing, PageSetupDialogPlanner.PresentationMetrics.RowInset)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, PageSetupDialogPlanner.PresentationMetrics.RowInset, 0, PageSetupDialogPlanner.PresentationMetrics.RowInset);
        grid.Children.Add(field);
    }

    // Fills width/height from the chosen named preset (Custom leaves them as typed).
    private void ApplyPaperPreset()
    {
        ApplyPaperProjection(_session.PlanPaperSelection(_paperSize.SelectedIndex));
    }

    // When the user edits width/height by hand, switch the dropdown to "Custom" (unless a preset was just applied).
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

    private void Accept(PageSetupDialogFollowUp followUp)
    {
        var acceptance = _session.PlanAcceptance(this, followUp);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ErrorMessage!);
            ApplyFocus(acceptance.FocusPlan!);
            return;
        }

        _result = acceptance.Result!;
        _acceptedFollowUp = acceptance.FollowUp;
        Close();
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
            DialogFocus.FocusAndSelect(target);
        else
            DialogFocus.Focus(target);
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

    private static Thickness ToThickness(PageSetupDialogThickness value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);

    internal static PageSetupDialogResult ToPresentationResult(PageSetupDialogResult result) => result;

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
    int IPageSetupDialogControlSource.VerticalAlignmentIndex => _vAlign.SelectedIndex;

    /// <summary>
    /// Test seam: builds a non-modal dialog instance seeded from <paramref name="page"/> so unit tests can
    /// exercise the control wiring (seeding, the paper-preset / orientation mapping) without a modal loop.
    /// </summary>
    internal static PageSetupDialog CreateForTest(PageSettings page, Tab initialTab = Tab.Margins) =>
        new(owner: null, page, SectionBreakKind.NextPage, initialTab);

    /// <summary>
    /// Test seam: validates the current control values and returns the <see cref="PageSetupDialogResult"/> they describe
    /// (or null when validation fails), without closing the window — the same mapping <see cref="Accept"/>
    /// performs.
    /// </summary>
    internal PageSetupDialogResult? AcceptForTest()
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
    public static (PageSetupDialogResult Settings, bool LineNumbers, bool Borders)? Prompt(
        Window? owner, PageSettings page, SectionBreakKind sectionStart = SectionBreakKind.NextPage, Tab initialTab = Tab.Margins)
    {
        var dialog = new PageSetupDialog(owner, page, sectionStart, initialTab);
        dialog.ShowDialog();
        return dialog._result is { } result
            ? (result, dialog.LineNumbersRequested, dialog.BordersRequested)
            : null;
    }
}
