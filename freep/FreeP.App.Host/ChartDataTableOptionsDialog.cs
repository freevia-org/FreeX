using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart data-table options dialog.</summary>
public sealed class ChartDataTableOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartDataTableOptionsPlanner _planner;
    private readonly CheckBox _showTableCheck;
    private readonly CheckBox _horizontalBorderCheck;
    private readonly CheckBox _verticalBorderCheck;
    private readonly CheckBox _outlineBorderCheck;
    private readonly CheckBox _legendKeysCheck;
    private readonly TextBox _backgroundColorBox;
    private readonly TextBox _borderColorBox;
    private readonly TextBox _borderWidthBox;
    private readonly TextBox _textColorBox;
    private readonly TextBox _fontSizeBox;
    private readonly TextBox _fontFamilyBox;
    private readonly CheckBox _boldCheck;
    private readonly CheckBox _italicCheck;

    public ChartDataTableOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartDataTableOptionsPlanner.FromChart(chart);
        var surface = ChartDataTableOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDataTableOptionsPlanner.DefaultDialogWidth;
        Height = ChartDataTableOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _showTableCheck = new CheckBox { Content = surface.ShowDataTableLabel, IsChecked = _planner.ShowDataTable };
        _horizontalBorderCheck = new CheckBox { Content = surface.HorizontalBorderLabel, IsChecked = _planner.ShowHorizontalBorder };
        _verticalBorderCheck = new CheckBox { Content = surface.VerticalBorderLabel, IsChecked = _planner.ShowVerticalBorder };
        _outlineBorderCheck = new CheckBox { Content = surface.OutlineBorderLabel, IsChecked = _planner.ShowOutlineBorder };
        _legendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, IsChecked = _planner.ShowLegendKeys };
        _backgroundColorBox = CreateTextBox(_planner.BackgroundColor);
        _borderColorBox = CreateTextBox(_planner.BorderColor);
        _borderWidthBox = CreateTextBox(FormatOptional(_planner.BorderWidthPt));
        _textColorBox = CreateTextBox(_planner.TextColor);
        _fontSizeBox = CreateTextBox(FormatOptional(_planner.FontSizePt));
        _fontFamilyBox = CreateTextBox(_planner.FontFamily);
        _boldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, IsChecked = _planner.Bold };
        _italicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, IsChecked = _planner.Italic };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 14, 8, 8),
        };
        var ok = new Button { Content = surface.OkLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = surface.CancelLabel, IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(_showTableCheck);
        content.Children.Add(_horizontalBorderCheck);
        content.Children.Add(_verticalBorderCheck);
        content.Children.Add(_outlineBorderCheck);
        content.Children.Add(_legendKeysCheck);
        AddTextRow(content, surface.BackgroundColorLabel, _backgroundColorBox);
        AddTextRow(content, surface.BorderColorLabel, _borderColorBox);
        AddTextRow(content, surface.BorderWidthLabel, _borderWidthBox);
        AddTextRow(content, surface.TextColorLabel, _textColorBox);
        AddTextRow(content, surface.FontSizeLabel, _fontSizeBox);
        AddTextRow(content, surface.FontFamilyLabel, _fontFamilyBox);
        content.Children.Add(_boldCheck);
        content.Children.Add(_italicCheck);
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartDataTableOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartDataTableOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetShowDataTable(_showTableCheck.IsChecked == true);
        _planner.SetShowHorizontalBorder(_horizontalBorderCheck.IsChecked == true);
        _planner.SetShowVerticalBorder(_verticalBorderCheck.IsChecked == true);
        _planner.SetShowOutlineBorder(_outlineBorderCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_legendKeysCheck.IsChecked == true);
        _planner.SetBackgroundColor(_backgroundColorBox.Text);
        _planner.SetBorderColor(_borderColorBox.Text);
        _planner.SetBorderWidth(ParseOptional(_borderWidthBox.Text, "Border width"));
        _planner.SetTextColor(_textColorBox.Text);
        _planner.SetFontSize(ParseOptional(_fontSizeBox.Text, "Font size"));
        _planner.SetFontFamily(_fontFamilyBox.Text);
        _planner.SetBold(_boldCheck.IsChecked);
        _planner.SetItalic(_italicCheck.IsChecked);
    }

    private static TextBox CreateTextBox(string value) => new() { Text = value, MinWidth = 150 };

    private static void AddTextRow(Panel panel, string label, TextBox box)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        var labelControl = new Label { Content = label, Padding = new Thickness(0, 2, 8, 2) };
        Grid.SetColumn(box, 1);
        row.Children.Add(labelControl);
        row.Children.Add(box);
        panel.Children.Add(row);
    }

    private static double? ParseOptional(string? text, string surface)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value) && value > 0)
            return value;
        throw new FormatException($"{surface} must be a positive finite number or blank.");
    }

    private static string FormatOptional(double? value) => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? string.Empty;
}
