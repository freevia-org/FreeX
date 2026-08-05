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

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(_showTableCheck);
        content.Children.Add(_horizontalBorderCheck);
        content.Children.Add(_verticalBorderCheck);
        content.Children.Add(_outlineBorderCheck);
        content.Children.Add(_legendKeysCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateTrailingFieldRow(surface.BackgroundColorLabel, _backgroundColorBox, 150));
        content.Children.Add(ChartOptionsDialogChrome.CreateTrailingFieldRow(surface.BorderColorLabel, _borderColorBox, 150));
        content.Children.Add(ChartOptionsDialogChrome.CreateTrailingFieldRow(surface.BorderWidthLabel, _borderWidthBox, 150));
        content.Children.Add(ChartOptionsDialogChrome.CreateTrailingFieldRow(surface.TextColorLabel, _textColorBox, 150));
        content.Children.Add(ChartOptionsDialogChrome.CreateTrailingFieldRow(surface.FontSizeLabel, _fontSizeBox, 150));
        content.Children.Add(ChartOptionsDialogChrome.CreateTrailingFieldRow(surface.FontFamilyLabel, _fontFamilyBox, 150));
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

    private static double? ParseOptional(string? text, string surface)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            CultureInfo.CurrentCulture,
            value => double.IsFinite(value) && value > 0,
            $"{surface} must be a positive finite number or blank.");
    }

    private static string FormatOptional(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture, "0.###");
}
