using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Localization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

public sealed class TabsDialog : FreeWDialogWindow
{
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;

    private TabsDialogState _state;
    private readonly ListBox _stops = new() { Height = 120, MinWidth = 150 };
    private readonly TextBox _position = new() { MinWidth = 120 };
    private readonly ComboBox _alignment = new() { MinWidth = 120 };
    private readonly ComboBox _leader = new() { MinWidth = 120 };
    private readonly TextBox _defaultTab = new() { MinWidth = 120 };

    public TabsDialog(IReadOnlyList<TabStop> tabStops, double defaultTabStopPt)
    {
        Title = "Tabs";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _state = TabsDialogPlanner.BuildInitialState(tabStops, defaultTabStopPt, DialogCulture);
        _alignment.ItemsSource = TabsDialogPlanner.Alignments.Select(choice => choice.Label).ToArray();
        _leader.ItemsSource = TabsDialogPlanner.Leaders.Select(choice => choice.Label).ToArray();
        _alignment.SelectedIndex = 0;
        _leader.SelectedIndex = 0;
        _defaultTab.Text = _state.DefaultTabStopText;

        RefreshRows(selectedIndex: -1);
        _stops.SelectionChanged += (_, _) =>
        {
            var selection = TabsDialogPlanner.ProjectSelectedStop(_state, _stops.SelectedIndex, DialogCulture);
            if (selection is null)
                return;
            _position.Text = selection.PositionText;
            _alignment.SelectedIndex = selection.AlignmentIndex;
            _leader.SelectedIndex = selection.LeaderIndex;
        };

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 7; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Tab stop position (pt):", _position);
        AddRow(grid, 1, "Stops:", _stops);
        AddRow(grid, 2, "Alignment:", _alignment);
        AddRow(grid, 3, "Leader:", _leader);
        AddRow(grid, 4, "Default tab stops (pt):", _defaultTab);

        // Keep the Avalonia grid's fractional row rounding aligned with the WPF authority. The
        // shared four-pixel row margins remain the default; these are only the targeted one-pixel
        // compensations needed to prevent drift through the compact control stack.
        _position.Margin = new Thickness(0, 4, 0, 3);
        _alignment.Margin = new Thickness(0, 4, 0, 3);
        _defaultTab.Margin = new Thickness(0, 4, 0, 5);

        var set = Button("Set", (_, _) => SetStop());
        var clear = Button("Clear", (_, _) =>
        {
            _state = TabsDialogPlanner.ClearStop(_state, _stops.SelectedIndex, _position.Text, DialogCulture);
            RefreshRows(selectedIndex: -1);
        });
        var clearAll = Button("Clear All", (_, _) =>
        {
            _state = TabsDialogPlanner.ClearAll(_state);
            RefreshRows(selectedIndex: -1);
        });
        var actions = AvaloniaDialogButtonRowFactory.CreateRow(
            [set, clear, clearAll],
            new Thickness(0, 8, 0, 0),
            AvaloniaCompactDialogChrome.WindowsStyle with { ActionSpacing = 6 });
        actions.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(actions, 5);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        var buttons = AvaloniaDialogButtonRowFactory.CreateOkCancel(
            Accept,
            () => Close(null),
            buttonWidth: 72,
            rowMargin: new Thickness(0, 10, 0, 0));
        Grid.SetRow(buttons, 6);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        Content = grid;

        Opened += (_, _) =>
        {
            // Avalonia's TextBox template contributes seven extra pixels on this route after the
            // shared chrome pass. WPF's authority TextBox is 26 px high; apply that host-template
            // compensation after the shared pass so the grid rows and action rows line up as one unit.
            var textBoxStyle = AvaloniaCompactDialogChrome.WindowsStyle with { TextBoxHeight = 26 };
            AvaloniaCompactDialogChrome.ApplyTextBox(_position, textBoxStyle);
            AvaloniaCompactDialogChrome.ApplyTextBox(_defaultTab, textBoxStyle);
            AvaloniaCompactDialogChrome.FocusAndSelect(_position);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    public static void ApplyResult(DocumentView editor, TabsDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        editor.SetParagraphTabStops(result.TabStops);
        editor.ApplyPageSettings(page => page.DefaultTabStopPt = result.DefaultTabStopPt);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var (_, paragraph) = editor.GetCaretFormatting();
        var dialog = new TabsDialog(paragraph.TabStops, editor.Document.Page.DefaultTabStopPt);
        var result = await dialog.ShowDialog<TabsDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result);
    }

    private async void SetStop()
    {
        var request = new TabsDialogSetRequest(_position.Text, _alignment.SelectedIndex, _leader.SelectedIndex);
        if (!TabsDialogPlanner.TrySetStop(_state, request, DialogCulture, out var plan, out var error))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                TabsDialogPlanner.ValidationMessageFor(error),
                Title ?? "Tabs");
            return;
        }

        _state = plan!.State;
        RefreshRows(plan.SelectedIndex);
    }

    private async void Accept()
    {
        if (!TabsDialogPlanner.TryBuildResult(_state, _defaultTab.Text, DialogCulture, out var result, out var error))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                TabsDialogPlanner.ValidationMessageFor(error),
                Title ?? "Tabs");
            return;
        }

        Close(result);
    }

    private void RefreshRows(int selectedIndex)
    {
        _stops.ItemsSource = _state.Rows.Select(row => row.DisplayText).ToArray();
        _stops.SelectedIndex = selectedIndex >= 0 && selectedIndex < _state.Rows.Count ? selectedIndex : -1;
    }

    private static void AddRow(Grid grid, int row, string label, Control control)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        control.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    private static Button Button(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Content = text, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(button, AvaloniaCompactDialogChrome.WindowsStyle, 72);
        button.Click += click;
        return button;
    }

    internal TextBox PositionBoxForTest => _position;
    internal TextBox DefaultTabStopBoxForTest => _defaultTab;
    internal ListBox StopsForTest => _stops;
    internal ComboBox AlignmentBoxForTest => _alignment;
    internal ComboBox LeaderBoxForTest => _leader;
}

public sealed class BordersAndShadingDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            ControlHeight = 20,
            TextBoxHeight = 20,
            ButtonHeight = 26,
            ButtonPadding = new Thickness(10, 1),
        };

    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;

    private readonly TabControl _tabs;
    private readonly ComboBox _paragraphSetting = Combo(BordersAndShadingDialogPlanner.SettingNames);
    private readonly ComboBox _paragraphStyle = Combo(BordersAndShadingDialogPlanner.LineStyleNames);
    private readonly ComboBox _paragraphColor = ColorCombo();
    private readonly TextBox _paragraphWidth = NumberBox();
    private readonly CheckBox _top = Check("Top");
    private readonly CheckBox _left = Check("Left");
    private readonly CheckBox _bottom = Check("Bottom");
    private readonly CheckBox _right = Check("Right");

    private readonly ComboBox _pageSetting = Combo(BordersAndShadingDialogPlanner.SettingNames);
    private readonly ComboBox _pageStyle = Combo(BordersAndShadingDialogPlanner.LineStyleNames);
    private readonly ComboBox _pageColor = ColorCombo();
    private readonly TextBox _pageWidth = NumberBox();
    private readonly ComboBox _pageArt = Combo(BordersAndShadingDialogPlanner.ArtBorders.Select(option => option.Label));

    private readonly ComboBox _shadingColor = ColorCombo(includeNone: true);
    private readonly ComboBox _shadingPattern = Combo(BordersAndShadingDialogPlanner.PatternNames);
    private readonly TextBlock _status = new() { IsVisible = false, Foreground = Brushes.Red };

    public BordersAndShadingDialog(ParagraphFormatting paragraph, PageBorder? pageBorder)
    {
        AutomationProperties.SetAutomationId(this, "BordersAndShadingDialog");
        Title = "Borders and Shading";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var border = paragraph.Border;
        _paragraphSetting.SelectedIndex = BordersAndShadingDialogPlanner.SettingIndexFor(border);
        _paragraphStyle.SelectedIndex = BordersAndShadingDialogPlanner.IndexOfLineStyle(border?.LineStyle ?? BorderLineStyle.Single);
        _paragraphColor.SelectedIndex = PaletteIndex(border?.ColorHex ?? "#000000");
        _paragraphWidth.Text = BordersAndShadingDialogPlanner.FormatPoints(border?.WidthPt ?? 0.5, DialogCulture);
        _top.IsChecked = border?.Top ?? true;
        _left.IsChecked = border?.Left ?? true;
        _bottom.IsChecked = border?.Bottom ?? true;
        _right.IsChecked = border?.Right ?? true;
        _paragraphSetting.SelectionChanged += (_, _) => ApplyParagraphSettingPlan();

        _pageSetting.SelectedIndex = pageBorder is null ? 0 : 1;
        _pageStyle.SelectedIndex = BordersAndShadingDialogPlanner.IndexOfLineStyle(pageBorder?.LineStyle ?? BorderLineStyle.Single);
        _pageColor.SelectedIndex = PaletteIndex(pageBorder?.ColorHex ?? "#000000");
        _pageWidth.Text = BordersAndShadingDialogPlanner.FormatPoints(pageBorder?.WidthPt ?? 1.0, DialogCulture);
        _pageArt.SelectedIndex = BordersAndShadingDialogPlanner.ArtIndexFor(pageBorder?.ArtId ?? 0);

        _shadingColor.SelectedIndex = string.IsNullOrWhiteSpace(paragraph.ShadingColorHex)
            ? 0
            : PaletteIndex(paragraph.ShadingColorHex) + 1;
        _shadingPattern.SelectedIndex = BordersAndShadingDialogPlanner.IndexOfPattern(paragraph.ShadingPattern);

        SetAutomationId(_paragraphSetting, "BordersAndShadingParagraphSetting");
        SetAutomationId(_paragraphStyle, "BordersAndShadingParagraphStyle");
        SetAutomationId(_paragraphColor, "BordersAndShadingParagraphColor");
        SetAutomationId(_paragraphWidth, "BordersAndShadingParagraphWidth");
        SetAutomationId(_top, "BordersAndShadingTopEdge");
        SetAutomationId(_left, "BordersAndShadingLeftEdge");
        SetAutomationId(_bottom, "BordersAndShadingBottomEdge");
        SetAutomationId(_right, "BordersAndShadingRightEdge");
        SetAutomationId(_pageSetting, "BordersAndShadingPageSetting");
        SetAutomationId(_pageStyle, "BordersAndShadingPageStyle");
        SetAutomationId(_pageColor, "BordersAndShadingPageColor");
        SetAutomationId(_pageWidth, "BordersAndShadingPageWidth");
        SetAutomationId(_pageArt, "BordersAndShadingPageArt");
        SetAutomationId(_shadingColor, "BordersAndShadingShadingColor");
        SetAutomationId(_shadingPattern, "BordersAndShadingShadingPattern");
        SetAutomationId(_status, "BordersAndShadingValidationMessage");

        _tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        SetAutomationId(_tabs, "BordersAndShadingTabs");
        _tabs.Items.Add(Tab("Borders", "BordersAndShadingBordersTab", BuildBordersTab()));
        _tabs.Items.Add(Tab("Page Border", "BordersAndShadingPageBorderTab", BuildPageBorderTab()));
        _tabs.Items.Add(Tab("Shading", "BordersAndShadingShadingTab", BuildShadingTab()));
        _tabs.SelectionChanged += (_, _) =>
        {
            var target = _tabs.SelectedIndex switch
            {
                1 => (Control)_pageSetting,
                2 => _shadingColor,
                _ => _paragraphWidth,
            };
            Dispatcher.UIThread.Post(() => target.Focus(), DispatcherPriority.Input);
        };
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            _tabs,
            DialogChromeStyle,
            contentPaneMargin: new Thickness(-12, 0, -12, 0));

        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(14, 8, 14, 0));
        var ok = Button(LocalizedUiText.Ok, (_, _) => Accept(), isDefault: true);
        var cancel = Button(LocalizedUiText.Cancel, (_, _) => Close(null), isCancel: true);
        SetAutomationId(ok, "BordersAndShadingOkButton");
        SetAutomationId(cancel, "BordersAndShadingCancelButton");
        var actions = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(14, 12, 14, 12),
            DialogChromeStyle);
        var root = new DockPanel();
        DockPanel.SetDock(actions, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(actions);
        root.Children.Add(_status);
        root.Children.Add(_tabs);
        Content = root;

        Opened += (_, _) =>
        {
            // Apply the route-specific metrics after the base dialog applies its default descendant chrome.
            foreach (var combo in new[]
            {
                _paragraphSetting, _paragraphStyle, _paragraphColor,
                _pageSetting, _pageStyle, _pageColor, _pageArt,
                _shadingColor, _shadingPattern,
            })
            {
                AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
            }

            AvaloniaCompactDialogChrome.ApplyTextBox(_paragraphWidth, DialogChromeStyle);
            AvaloniaCompactDialogChrome.ApplyTextBox(_pageWidth, DialogChromeStyle);
            foreach (var check in new[] { _top, _left, _bottom, _right })
                AvaloniaCompactDialogChrome.ApplyCompactCheckBox(check, DialogChromeStyle);

            foreach (var button in new[] { ok, cancel })
                AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 72, isDefault: button.IsDefault);

            AvaloniaCompactDialogChrome.FocusAndSelect(_paragraphWidth);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            Close(null);
            e.Handled = true;
        };
    }

    internal TabControl TabsForTest => _tabs;
    internal TextBox ParagraphWidthForTest => _paragraphWidth;
    internal ComboBox PageSettingForTest => _pageSetting;
    internal ComboBox ShadingColorForTest => _shadingColor;
    internal TextBlock StatusForTest => _status;

    private Control BuildBordersTab()
    {
        var grid = TwoColumnGrid(6);
        AddRow(grid, 0, "Setting:", _paragraphSetting);
        AddRow(grid, 1, "Style:", _paragraphStyle);
        AddRow(grid, 2, "Colour:", _paragraphColor);
        AddRow(grid, 3, "Width (pt):", _paragraphWidth);
        AddRow(grid, 4, "Edges:", EdgeRow(_top, _bottom));
        AddRow(grid, 5, string.Empty, EdgeRow(_left, _right));
        return grid;
    }

    private Control BuildPageBorderTab()
    {
        var grid = TwoColumnGrid(5);
        AddRow(grid, 0, "Setting:", _pageSetting);
        AddRow(grid, 1, "Style:", _pageStyle);
        AddRow(grid, 2, "Art border:", _pageArt);
        AddRow(grid, 3, "Colour:", _pageColor);
        AddRow(grid, 4, "Width (pt):", _pageWidth);
        return grid;
    }

    private Control BuildShadingTab()
    {
        var grid = TwoColumnGrid(2);
        AddRow(grid, 0, "Fill:", _shadingColor);
        AddRow(grid, 1, "Pattern:", _shadingPattern);
        return grid;
    }

    public static void ApplyResult(DocumentView editor, BordersAndShadingDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        editor.SetParagraphBorder(result.ParagraphBorder);
        editor.SetParagraphShading(result.ShadingHex, result.ShadingPattern);
        editor.ApplyPageSettings(page => page.PageBorder = result.PageBorder);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var (_, paragraph) = editor.GetCaretFormatting();
        var dialog = new BordersAndShadingDialog(paragraph, editor.Document.Page.PageBorder);
        dialog.ApplyParagraphSettingPlan();
        var result = await dialog.ShowDialog<BordersAndShadingDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result);
    }

    private void ApplyParagraphSettingPlan()
    {
        var plan = BordersAndShadingDialogPlanner.PlanParagraphSetting(_paragraphSetting.SelectedIndex);
        foreach (var check in new[] { _top, _left, _bottom, _right })
        {
            check.IsEnabled = plan.EdgesEnabled;
            if (plan.EdgeValue.HasValue)
                check.IsChecked = plan.EdgeValue.Value;
        }
    }

    private void Accept()
    {
        var input = new BordersAndShadingDialogInput(
            ParagraphSettingIndex: _paragraphSetting.SelectedIndex,
            ParagraphLineStyleIndex: _paragraphStyle.SelectedIndex,
            ParagraphColorHex: PaletteHex(_paragraphColor.SelectedIndex),
            ParagraphWidthText: _paragraphWidth.Text,
            Top: _top.IsChecked == true,
            Left: _left.IsChecked == true,
            Bottom: _bottom.IsChecked == true,
            Right: _right.IsChecked == true,
            PageSettingIndex: _pageSetting.SelectedIndex,
            PageLineStyleIndex: _pageStyle.SelectedIndex,
            PageColorHex: PaletteHex(_pageColor.SelectedIndex),
            PageWidthText: _pageWidth.Text,
            PageArtIndex: _pageArt.SelectedIndex,
            ShadingColorHex: _shadingColor.SelectedIndex <= 0 ? null : PaletteHex(_shadingColor.SelectedIndex - 1),
            ShadingPatternIndex: _shadingPattern.SelectedIndex);

        if (!BordersAndShadingDialogPlanner.TryBuildResult(input, DialogCulture, out var result, out var error))
        {
            _status.Text = error ?? BordersAndShadingDialogPlanner.WidthValidationMessage;
            _status.IsVisible = true;
            return;
        }

        Close(result);
    }

    private static string PaletteHex(int index) =>
        BordersAndShadingDialogPlanner.Palette[Math.Clamp(index, 0, BordersAndShadingDialogPlanner.Palette.Count - 1)];

    private static int PaletteIndex(string? hex)
    {
        for (var i = 0; i < BordersAndShadingDialogPlanner.Palette.Count; i++)
            if (string.Equals(BordersAndShadingDialogPlanner.Palette[i], hex, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    private static ComboBox Combo(IEnumerable<string> items)
    {
        var combo = new ComboBox
        {
            ItemsSource = items.ToArray(),
            SelectedIndex = 0,
            MinWidth = 160,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        return combo;
    }

    private static ComboBox ColorCombo(bool includeNone = false)
    {
        var items = new List<Control>();
        if (includeNone)
        {
            var none = new TextBlock { Text = "No Colour", VerticalAlignment = VerticalAlignment.Center };
            SetAutomationId(none, "BordersAndShadingNoShadingColor");
            items.Add(none);
        }
        items.AddRange(BordersAndShadingDialogPlanner.Palette.Select((hex, index) =>
            ColorItem(hex, $"BordersAndShadingColorSwatch{index}")));
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = 0, MinWidth = 160 };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle);
        return combo;
    }

    private static Control ColorItem(string hex, string automationId)
    {
        var swatch = new Border
        {
            Width = 28,
            Height = 12,
            Background = Brush.Parse(hex),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var item = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { swatch, new TextBlock { Text = hex, VerticalAlignment = VerticalAlignment.Center } },
        };
        SetAutomationId(item, automationId);
        AutomationProperties.SetName(item, hex);
        return item;
    }

    private static TextBox NumberBox()
    {
        var box = new TextBox { Width = 160 };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static CheckBox Check(string text)
    {
        var box = new CheckBox
        {
            Content = text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(box, DialogChromeStyle);
        return box;
    }

    private static TabItem Tab(string header, string automationId, Control content)
    {
        var tab = new TabItem { Header = header, Content = content };
        SetAutomationId(tab, automationId);
        return tab;
    }

    private static void SetAutomationId(StyledElement control, string automationId) =>
        AutomationProperties.SetAutomationId(control, automationId);

    private static Grid TwoColumnGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < rows; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static StackPanel EdgeRow(CheckBox first, CheckBox second)
    {
        first.Margin = new Thickness(0, 0, 16, 0);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { first, second },
        };
    }

    private static void AddRow(Grid grid, int row, string label, Control control)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        control.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    private static Button Button(string text, EventHandler<RoutedEventArgs> click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 72, isDefault: isDefault);
        AutomationProperties.SetName(button, Free.Shared.Shell.ShellStrings.Current.CreateAutomationName(text));
        button.Click += click;
        return button;
    }
}

public sealed class SortDialog : FreeWDialogWindow
{
    private readonly ComboBox _type1 = Combo();
    private readonly ComboBox _type2 = Combo();
    private readonly ComboBox _type3 = Combo();
    private readonly RadioButton _asc1 = AscRadio("sort1");
    private readonly RadioButton _asc2 = AscRadio("sort2");
    private readonly RadioButton _asc3 = AscRadio("sort3");
    private readonly RadioButton _desc1 = DescRadio("sort1");
    private readonly RadioButton _desc2 = DescRadio("sort2");
    private readonly RadioButton _desc3 = DescRadio("sort3");
    private readonly CheckBox _useKey2 = new() { Content = "Then by", Margin = new Thickness(0, 8, 0, 4) };
    private readonly CheckBox _useKey3 = new() { Content = "Then by (2nd)", Margin = new Thickness(0, 8, 0, 4) };
    private readonly CheckBox _caseSensitive = new() { Content = "Case sensitive", Margin = new Thickness(0, 10, 0, 4) };
    private readonly CheckBox _hasHeaderRow = new() { Content = "My list has a header row" };

    public SortDialog(bool forTable)
    {
        Title = "Sort";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        SetKeyEnabled(_type2, _asc2, _desc2, enabled: false);
        SetKeyEnabled(_type3, _asc3, _desc3, enabled: false);
        _useKey2.IsCheckedChanged += (_, _) => SetKeyEnabled(_type2, _asc2, _desc2, _useKey2.IsChecked == true);
        _useKey3.IsCheckedChanged += (_, _) => SetKeyEnabled(_type3, _asc3, _desc3, _useKey3.IsChecked == true);

        var outer = new StackPanel { Margin = new Thickness(16) };
        outer.Children.Add(new TextBlock { Text = SortDialogPlanner.PromptLabel(forTable), Margin = new Thickness(0, 0, 0, 8) });
        outer.Children.Add(KeySection("Sort by", _type1, _asc1, _desc1));
        outer.Children.Add(_useKey2);
        outer.Children.Add(KeySection(null, _type2, _asc2, _desc2));
        outer.Children.Add(_useKey3);
        outer.Children.Add(KeySection(null, _type3, _asc3, _desc3));
        outer.Children.Add(_caseSensitive);
        outer.Children.Add(_hasHeaderRow);

        var ok = Button("OK", (_, _) => Accept());
        ok.IsDefault = true;
        var cancel = Button("Cancel", (_, _) => Close(null));
        cancel.IsCancel = true;
        outer.Children.Add(ButtonRow(ok, cancel));
        Content = outer;
    }

    public static void ApplyResult(DocumentView editor, SortDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (editor.IsCaretInTable())
            editor.SortCaretTableRows(result.Kind, result.Ascending, result.CaseSensitive, result.HasHeaderRow);
        else
            editor.SortSelectedParagraphs(result.Kind, result.Ascending, result.CaseSensitive, result.HasHeaderRow);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var dialog = new SortDialog(editor.IsCaretInTable());
        var result = await dialog.ShowDialog<SortDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result.Value);
    }

    private void Accept()
    {
        Close(SortDialogPlanner.BuildResult(
            _type1.SelectedIndex,
            _asc1.IsChecked == true,
            _useKey2.IsChecked == true,
            _type2.SelectedIndex,
            _asc2.IsChecked == true,
            _useKey3.IsChecked == true,
            _type3.SelectedIndex,
            _asc3.IsChecked == true,
            _caseSensitive.IsChecked == true,
            _hasHeaderRow.IsChecked == true));
    }

    private static Control KeySection(string? heading, ComboBox type, RadioButton asc, RadioButton desc)
    {
        var panel = new StackPanel();
        if (!string.IsNullOrWhiteSpace(heading))
            panel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = "Type:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(type);
        panel.Children.Add(row);
        panel.Children.Add(asc);
        panel.Children.Add(desc);
        return panel;
    }

    private static ComboBox Combo() => new()
    {
        ItemsSource = SortDialogPlanner.TypeChoices.Select(choice => choice.Label).ToArray(),
        SelectedIndex = 0,
        MinWidth = 120
    };

    private static RadioButton AscRadio(string groupName) => new()
    {
        Content = "Ascending",
        GroupName = groupName,
        IsChecked = true,
        Margin = new Thickness(4, 0, 0, 2)
    };

    private static RadioButton DescRadio(string groupName) => new()
    {
        Content = "Descending",
        GroupName = groupName,
        Margin = new Thickness(4, 0, 0, 2)
    };

    private static void SetKeyEnabled(ComboBox type, RadioButton asc, RadioButton desc, bool enabled)
    {
        type.IsEnabled = enabled;
        asc.IsEnabled = enabled;
        desc.IsEnabled = enabled;
    }

    private static StackPanel ButtonRow(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        foreach (var button in buttons)
            row.Children.Add(button);
        return row;
    }

    private static Button Button(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Content = text, MinWidth = 76, Margin = new Thickness(6, 0, 0, 0) };
        button.Click += click;
        return button;
    }
}
