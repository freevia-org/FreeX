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
        Owner = owner;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "BordersAndShadingDialog");
        Title = "Borders and Shading";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var border = paragraph.Border;

        _setting = Combo(BordersAndShadingDialogPlanner.SettingNames, BordersAndShadingDialogPlanner.SettingIndexFor(border));
        _lineStyle = Combo(BordersAndShadingDialogPlanner.LineStyleNames, BordersAndShadingDialogPlanner.IndexOfLineStyle(border?.LineStyle ?? BorderLineStyle.Single));
        _color = ColorCombo(border?.ColorHex ?? "#000000");
        _width = NumberBox(BordersAndShadingDialogPlanner.FormatPoints(border?.WidthPt ?? 0.5, CultureInfo.CurrentCulture));
        _top = EdgeBox("Top", border?.Top ?? true);
        _left = EdgeBox("Left", border?.Left ?? true);
        _bottom = EdgeBox("Bottom", border?.Bottom ?? true);
        _right = EdgeBox("Right", border?.Right ?? true);
        _setting.SelectionChanged += (_, _) => ApplyParagraphSetting();

        _pageSetting = Combo(BordersAndShadingDialogPlanner.SettingNames, pageBorder is null ? 0 : 1);
        _pageLineStyle = Combo(BordersAndShadingDialogPlanner.LineStyleNames, BordersAndShadingDialogPlanner.IndexOfLineStyle(pageBorder?.LineStyle ?? BorderLineStyle.Single));
        _pageColor = ColorCombo(pageBorder?.ColorHex ?? "#000000");
        _pageWidth = NumberBox(BordersAndShadingDialogPlanner.FormatPoints(pageBorder?.WidthPt ?? 1.0, CultureInfo.CurrentCulture));
        _pageArtStyle = Combo(
            BordersAndShadingDialogPlanner.ArtBorders.Select(a => a.Label).ToArray(),
            BordersAndShadingDialogPlanner.ArtIndexFor(pageBorder?.ArtId ?? 0));

        _shadingColor = ColorCombo(paragraph.ShadingColorHex ?? "#FFFFFF", includeNone: true,
            selectNone: string.IsNullOrEmpty(paragraph.ShadingColorHex));
        _shadingPattern = Combo(BordersAndShadingDialogPlanner.PatternNames, BordersAndShadingDialogPlanner.IndexOfPattern(paragraph.ShadingPattern));

        SetAutomationId(_setting, "BordersAndShadingParagraphSetting");
        SetAutomationId(_lineStyle, "BordersAndShadingParagraphStyle");
        SetAutomationId(_color, "BordersAndShadingParagraphColor");
        SetAutomationId(_width, "BordersAndShadingParagraphWidth");
        SetAutomationId(_top, "BordersAndShadingTopEdge");
        SetAutomationId(_left, "BordersAndShadingLeftEdge");
        SetAutomationId(_bottom, "BordersAndShadingBottomEdge");
        SetAutomationId(_right, "BordersAndShadingRightEdge");
        SetAutomationId(_pageSetting, "BordersAndShadingPageSetting");
        SetAutomationId(_pageLineStyle, "BordersAndShadingPageStyle");
        SetAutomationId(_pageColor, "BordersAndShadingPageColor");
        SetAutomationId(_pageWidth, "BordersAndShadingPageWidth");
        SetAutomationId(_pageArtStyle, "BordersAndShadingPageArt");
        SetAutomationId(_shadingColor, "BordersAndShadingShadingColor");
        SetAutomationId(_shadingPattern, "BordersAndShadingShadingPattern");

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        var bordersTab = new TabItem { Header = "Borders", Content = BuildBordersTab() };
        var pageBorderTab = new TabItem { Header = "Page Border", Content = BuildPageBorderTab() };
        var shadingTab = new TabItem { Header = "Shading", Content = BuildShadingTab() };
        SetAutomationId(bordersTab, "BordersAndShadingBordersTab");
        SetAutomationId(pageBorderTab, "BordersAndShadingPageBorderTab");
        SetAutomationId(shadingTab, "BordersAndShadingShadingTab");
        tabs.Items.Add(bordersTab);
        tabs.Items.Add(pageBorderTab);
        tabs.Items.Add(shadingTab);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(14, 12, 14, 12));
        var ok = (Button)buttons.Children[0];
        var cancel = (Button)buttons.Children[1];
        SetAutomationId(ok, "BordersAndShadingOkButton");
        SetAutomationId(cancel, "BordersAndShadingCancelButton");

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
        AddRow(grid, 0, "Setting:", _setting);
        AddRow(grid, 1, "Style:", _lineStyle);
        AddRow(grid, 2, "Colour:", _color);
        AddRow(grid, 3, "Width (pt):", _width);
        AddRow(grid, 4, "Edges:", EdgeRow(_top, _bottom));
        AddRow(grid, 5, string.Empty, EdgeRow(_left, _right));
        return grid;
    }

    private Grid BuildPageBorderTab()
    {
        var grid = TwoColumnGrid(5);
        AddRow(grid, 0, "Setting:", _pageSetting);
        AddRow(grid, 1, "Style:", _pageLineStyle);
        AddRow(grid, 2, "Art border:", _pageArtStyle);
        AddRow(grid, 3, "Colour:", _pageColor);
        AddRow(grid, 4, "Width (pt):", _pageWidth);
        return grid;
    }

    private Grid BuildShadingTab()
    {
        var grid = TwoColumnGrid(2);
        AddRow(grid, 0, "Fill:", _shadingColor);
        AddRow(grid, 1, "Pattern:", _shadingPattern);
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

    private static ComboBox ColorCombo(string seedHex, bool includeNone = false, bool selectNone = false)
    {
        var combo = new ComboBox { MinWidth = 160 };
        var selectedIndex = 0;
        if (includeNone)
            combo.Items.Add(new ComboBoxItem { Content = "No Colour", Tag = (string?)null });
        var offset = combo.Items.Count;
        for (var i = 0; i < BordersAndShadingDialogPlanner.Palette.Count; i++)
        {
            var hex = BordersAndShadingDialogPlanner.Palette[i];
            combo.Items.Add(SwatchItem(hex));
            if (string.Equals(hex, seedHex, StringComparison.OrdinalIgnoreCase))
                selectedIndex = offset + i;
        }
        combo.SelectedIndex = includeNone && selectNone ? 0 : selectedIndex;
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
        var plan = BordersAndShadingDialogPlanner.PlanParagraphSetting(_setting.SelectedIndex);
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

        if (!BordersAndShadingDialogPlanner.TryBuildResult(
                input,
                CultureInfo.CurrentCulture,
                out _result,
                out var errorMessage))
        {
            DialogMessageHelper.ShowWarning(this, errorMessage ?? BordersAndShadingDialogPlanner.WidthValidationMessage);
            return;
        }

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
