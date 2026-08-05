using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartDataTableOptionsDialog : Window
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

    internal ChartDataTableOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartDataTableOptionsPlanner.FromChart(chart);
        var surface = ChartDataTableOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDataTableOptionsPlanner.DefaultDialogWidth;
        Height = ChartDataTableOptionsPlanner.DefaultDialogHeight;
        MinWidth = 340;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _showTableCheck = new CheckBox { Content = surface.ShowDataTableLabel, IsChecked = _planner.ShowDataTable };
        _horizontalBorderCheck = new CheckBox { Content = surface.HorizontalBorderLabel, IsChecked = _planner.ShowHorizontalBorder };
        _verticalBorderCheck = new CheckBox { Content = surface.VerticalBorderLabel, IsChecked = _planner.ShowVerticalBorder };
        _outlineBorderCheck = new CheckBox { Content = surface.OutlineBorderLabel, IsChecked = _planner.ShowOutlineBorder };
        _legendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, IsChecked = _planner.ShowLegendKeys };
        _backgroundColorBox = CreateTextBox(_planner.BackgroundColor);
        _borderColorBox = CreateTextBox(_planner.BorderColor);
        _borderWidthBox = CreateTextBox(Format(_planner.BorderWidthPt));
        _textColorBox = CreateTextBox(_planner.TextColor);
        _fontSizeBox = CreateTextBox(Format(_planner.FontSizePt));
        _fontFamilyBox = CreateTextBox(_planner.FontFamily);
        _boldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, IsChecked = _planner.Bold };
        _italicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, IsChecked = _planner.Italic };

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                _showTableCheck,
                _horizontalBorderCheck,
                _verticalBorderCheck,
                _outlineBorderCheck,
                _legendKeysCheck,
                ChartOptionsDialogChrome.CreateRow(surface.BackgroundColorLabel, _backgroundColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.BorderColorLabel, _borderColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.BorderWidthLabel, _borderWidthBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.TextColorLabel, _textColorBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FontSizeLabel, _fontSizeBox, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FontFamilyLabel, _fontFamilyBox, 180),
                _boldCheck,
                _italicCheck,
                buttons,
            },
        };
    }

    internal ChartDataTableOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        bool showDataTable,
        bool showHorizontalBorder,
        bool showVerticalBorder,
        bool showOutlineBorder,
        bool showLegendKeys,
        string? backgroundColor = null,
        string? borderColor = null,
        double? borderWidthPt = null,
        string? textColor = null,
        double? fontSizePt = null,
        string? fontFamily = null,
        bool? bold = null,
        bool? italic = null)
    {
        _showTableCheck.IsChecked = showDataTable;
        _horizontalBorderCheck.IsChecked = showHorizontalBorder;
        _verticalBorderCheck.IsChecked = showVerticalBorder;
        _outlineBorderCheck.IsChecked = showOutlineBorder;
        _legendKeysCheck.IsChecked = showLegendKeys;
        _backgroundColorBox.Text = backgroundColor ?? string.Empty;
        _borderColorBox.Text = borderColor ?? string.Empty;
        _borderWidthBox.Text = Format(borderWidthPt);
        _textColorBox.Text = textColor ?? string.Empty;
        _fontSizeBox.Text = Format(fontSizePt);
        _fontFamilyBox.Text = fontFamily ?? string.Empty;
        _boldCheck.IsChecked = bold;
        _italicCheck.IsChecked = italic;
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartDataTableOptions(BuildCommitPlanForTests());
            Close(true);
        }
        catch (FormatException)
        {
            // Keep the dialog open so the user can correct the invalid numeric field.
        }
        catch (ArgumentException)
        {
            // Keep the dialog open so the user can correct the invalid color field.
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

    private static TextBox CreateTextBox(string value) => new() { Text = value };

    private static double? ParseOptional(string? text, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            CultureInfo.CurrentCulture,
            value => double.IsFinite(value) && value > 0,
            $"{label} must be a positive finite number or blank.");
    }

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
