using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Borders and Shading" dialog (Home / Design &gt; Borders &gt; Borders and Shading...).
/// </summary>
internal sealed class BordersAndShadingDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly BordersAndShadingDialogSession _session;
    private readonly ComboBox _setting;
    private readonly ComboBox _lineStyle;
    private readonly ComboBox _color;
    private readonly TextBox _width;
    private readonly CheckBox _top;
    private readonly CheckBox _left;
    private readonly CheckBox _bottom;
    private readonly CheckBox _right;

    private readonly ComboBox _pageSetting;
    private readonly ComboBox _pageLineStyle;
    private readonly ComboBox _pageColor;
    private readonly TextBox _pageWidth;
    private readonly ComboBox _pageArtStyle;

    private readonly ComboBox _shadingColor;
    private readonly ComboBox _shadingPattern;

    private BordersAndShadingDialogResult? _result;

    private BordersAndShadingDialog(Window? owner, ParagraphFormatting paragraph, PageBorder? pageBorder)
    {
        _session = new BordersAndShadingDialogSession(paragraph, pageBorder, CultureInfo.CurrentCulture);
        var state = _session.InitialState;
        Owner = owner;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, BordersAndShadingDialogPlanner.AutomationId);
        Title = BordersAndShadingDialogPlanner.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _setting = Combo(BordersAndShadingDialogPlanner.SettingNames, state.ParagraphSettingIndex);
        _lineStyle = Combo(BordersAndShadingDialogPlanner.LineStyleNames, state.ParagraphLineStyleIndex);
        _color = ColorCombo(state.ParagraphColorIndex);
        _width = NumberBox(state.ParagraphWidthText);
        _top = EdgeBox(BordersAndShadingDialogPlanner.TopLabel, state.Top);
        _left = EdgeBox(BordersAndShadingDialogPlanner.LeftLabel, state.Left);
        _bottom = EdgeBox(BordersAndShadingDialogPlanner.BottomLabel, state.Bottom);
        _right = EdgeBox(BordersAndShadingDialogPlanner.RightLabel, state.Right);
        _setting.SelectionChanged += (_, _) => ApplyParagraphSetting();

        _pageSetting = Combo(BordersAndShadingDialogPlanner.SettingNames, state.PageSettingIndex);
        _pageLineStyle = Combo(BordersAndShadingDialogPlanner.LineStyleNames, state.PageLineStyleIndex);
        _pageColor = ColorCombo(state.PageColorIndex);
        _pageWidth = NumberBox(state.PageWidthText);
        _pageArtStyle = Combo(
            BordersAndShadingDialogPlanner.ArtBorders.Select(a => a.Label).ToArray(),
            state.PageArtIndex);

        _shadingColor = ColorCombo(state.ShadingColorIndex, includeNone: true);
        _shadingPattern = Combo(BordersAndShadingDialogPlanner.PatternNames, state.ShadingPatternIndex);

        SetAutomationId(_setting, BordersAndShadingDialogPlanner.ParagraphSettingAutomationId);
        SetAutomationId(_lineStyle, BordersAndShadingDialogPlanner.ParagraphStyleAutomationId);
        SetAutomationId(_color, BordersAndShadingDialogPlanner.ParagraphColorAutomationId);
        SetAutomationId(_width, BordersAndShadingDialogPlanner.ParagraphWidthAutomationId);
        SetAutomationId(_top, BordersAndShadingDialogPlanner.TopEdgeAutomationId);
        SetAutomationId(_left, BordersAndShadingDialogPlanner.LeftEdgeAutomationId);
        SetAutomationId(_bottom, BordersAndShadingDialogPlanner.BottomEdgeAutomationId);
        SetAutomationId(_right, BordersAndShadingDialogPlanner.RightEdgeAutomationId);
        SetAutomationId(_pageSetting, BordersAndShadingDialogPlanner.PageSettingAutomationId);
        SetAutomationId(_pageLineStyle, BordersAndShadingDialogPlanner.PageStyleAutomationId);
        SetAutomationId(_pageColor, BordersAndShadingDialogPlanner.PageColorAutomationId);
        SetAutomationId(_pageWidth, BordersAndShadingDialogPlanner.PageWidthAutomationId);
        SetAutomationId(_pageArtStyle, BordersAndShadingDialogPlanner.PageArtAutomationId);
        SetAutomationId(_shadingColor, BordersAndShadingDialogPlanner.ShadingColorAutomationId);
        SetAutomationId(_shadingPattern, BordersAndShadingDialogPlanner.ShadingPatternAutomationId);

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        SetAutomationId(tabs, BordersAndShadingDialogPlanner.TabsAutomationId);
        var bordersTab = new TabItem { Header = BordersAndShadingDialogPlanner.BordersTabLabel, Content = BuildBordersTab() };
        var pageBorderTab = new TabItem { Header = BordersAndShadingDialogPlanner.PageBorderTabLabel, Content = BuildPageBorderTab() };
        var shadingTab = new TabItem { Header = BordersAndShadingDialogPlanner.ShadingTabLabel, Content = BuildShadingTab() };
        SetAutomationId(bordersTab, BordersAndShadingDialogPlanner.BordersTabAutomationId);
        SetAutomationId(pageBorderTab, BordersAndShadingDialogPlanner.PageBorderTabAutomationId);
        SetAutomationId(shadingTab, BordersAndShadingDialogPlanner.ShadingTabAutomationId);
        tabs.Items.Add(bordersTab);
        tabs.Items.Add(pageBorderTab);
        tabs.Items.Add(shadingTab);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(14, 12, 14, 12));
        var ok = (Button)buttons.Children[0];
        var cancel = (Button)buttons.Children[1];
        SetAutomationId(ok, BordersAndShadingDialogPlanner.AcceptButtonAutomationId);
        SetAutomationId(cancel, BordersAndShadingDialogPlanner.CancelButtonAutomationId);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;

        DialogFocus.FocusAndSelect(_width);
    }

    private Grid BuildBordersTab()
    {
        var grid = TwoColumnGrid(7);
        AddRow(grid, 0, BordersAndShadingDialogPlanner.SettingLabel, _setting);
        AddRow(grid, 1, BordersAndShadingDialogPlanner.StyleLabel, _lineStyle);
        AddRow(grid, 2, BordersAndShadingDialogPlanner.ColorLabel, _color);
        AddRow(grid, 3, BordersAndShadingDialogPlanner.WidthLabel, _width);
        AddRow(grid, 4, BordersAndShadingDialogPlanner.EdgesLabel, EdgeRow(_top, _bottom));
        AddRow(grid, 5, string.Empty, EdgeRow(_left, _right));
        return grid;
    }

    private Grid BuildPageBorderTab()
    {
        var grid = TwoColumnGrid(5);
        AddRow(grid, 0, BordersAndShadingDialogPlanner.SettingLabel, _pageSetting);
        AddRow(grid, 1, BordersAndShadingDialogPlanner.StyleLabel, _pageLineStyle);
        AddRow(grid, 2, BordersAndShadingDialogPlanner.ArtBorderLabel, _pageArtStyle);
        AddRow(grid, 3, BordersAndShadingDialogPlanner.ColorLabel, _pageColor);
        AddRow(grid, 4, BordersAndShadingDialogPlanner.WidthLabel, _pageWidth);
        return grid;
    }

    private Grid BuildShadingTab()
    {
        var grid = TwoColumnGrid(2);
        AddRow(grid, 0, BordersAndShadingDialogPlanner.FillLabel, _shadingColor);
        AddRow(grid, 1, BordersAndShadingDialogPlanner.PatternLabel, _shadingPattern);
        return grid;
    }

    private static Grid TwoColumnGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static StackPanel EdgeRow(CheckBox a, CheckBox b)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        a.Margin = new Thickness(0, 0, 16, 0);
        panel.Children.Add(a);
        panel.Children.Add(b);
        return panel;
    }

    private static ComboBox Combo(IReadOnlyList<string> items, int selected)
    {
        var combo = new ComboBox { MinWidth = 160 };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = Math.Clamp(selected, 0, items.Count - 1);
        return combo;
    }

    private static ComboBox ColorCombo(int selectedIndex, bool includeNone = false)
    {
        var combo = new ComboBox { MinWidth = 160 };
        if (includeNone)
            combo.Items.Add(new ComboBoxItem { Content = BordersAndShadingDialogPlanner.NoColorLabel, Tag = (string?)null });
        foreach (var hex in BordersAndShadingDialogPlanner.Palette)
        {
            combo.Items.Add(SwatchItem(hex));
        }
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, combo.Items.Count - 1);
        return combo;
    }

    private static ComboBoxItem SwatchItem(string hex)
    {
        var swatch = new System.Windows.Shapes.Rectangle
        {
            Width = 28,
            Height = 12,
            Stroke = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            StrokeThickness = 1,
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            Margin = new Thickness(0, 0, 6, 0)
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(swatch);
        panel.Children.Add(new TextBlock { Text = hex, VerticalAlignment = VerticalAlignment.Center });
        return new ComboBoxItem { Content = panel, Tag = hex };
    }

    private static void SetAutomationId(DependencyObject control, string automationId) =>
        System.Windows.Automation.AutomationProperties.SetAutomationId(control, automationId);

    private static string? SelectedColor(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: string hex } ? hex : null;

    private static TextBox NumberBox(string text) => new()
    {
        Text = text,
        MinWidth = 160
    };

    private static CheckBox EdgeBox(string label, bool isChecked) => new()
    {
        Content = label,
        IsChecked = isChecked,
        VerticalAlignment = VerticalAlignment.Center
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

        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private void ApplyParagraphSetting()
    {
        var plan = _session.PlanParagraphSetting(_setting.SelectedIndex);
        if (plan.EdgeValue is { } edgeValue)
            SetEdges(edgeValue);
        SetEdgesEnabled(plan.EdgesEnabled);
    }

    private void SetEdges(bool value)
    {
        _top.IsChecked = value;
        _left.IsChecked = value;
        _bottom.IsChecked = value;
        _right.IsChecked = value;
    }

    private void SetEdgesEnabled(bool enabled)
    {
        _top.IsEnabled = enabled;
        _left.IsEnabled = enabled;
        _bottom.IsEnabled = enabled;
        _right.IsEnabled = enabled;
    }

    private void Accept()
    {
        var input = new BordersAndShadingDialogInput(
            ParagraphSettingIndex: _setting.SelectedIndex,
            ParagraphLineStyleIndex: _lineStyle.SelectedIndex,
            ParagraphColorHex: SelectedColor(_color),
            ParagraphWidthText: _width.Text,
            Top: _top.IsChecked == true,
            Left: _left.IsChecked == true,
            Bottom: _bottom.IsChecked == true,
            Right: _right.IsChecked == true,
            PageSettingIndex: _pageSetting.SelectedIndex,
            PageLineStyleIndex: _pageLineStyle.SelectedIndex,
            PageColorHex: SelectedColor(_pageColor),
            PageWidthText: _pageWidth.Text,
            PageArtIndex: _pageArtStyle.SelectedIndex,
            ShadingColorHex: SelectedColor(_shadingColor),
            ShadingPatternIndex: _shadingPattern.SelectedIndex);

        var acceptance = _session.PlanAcceptance(input);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ValidationMessage ?? BordersAndShadingDialogPlanner.WidthValidationMessage);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    public static BordersAndShadingDialogResult? Prompt(Window? owner, ParagraphFormatting paragraph, PageBorder? pageBorder)
    {
        var dialog = new BordersAndShadingDialog(owner, paragraph, pageBorder);
        dialog.ApplyParagraphSetting();
        dialog.ShowDialog();
        return dialog._result;
    }
}
