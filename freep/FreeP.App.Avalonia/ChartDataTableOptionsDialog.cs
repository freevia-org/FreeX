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
    private readonly ChartDataTableOptionsDialogSession _session;
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
        _session = new ChartDataTableOptionsDialogSession(editor);
        var state = _session.State;
        var surface = ChartDataTableOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDataTableOptionsPlanner.DefaultDialogWidth;
        Height = ChartDataTableOptionsPlanner.DefaultDialogHeight;
        MinWidth = 340;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _showTableCheck = new CheckBox { Content = surface.ShowDataTableLabel, IsChecked = state.ShowDataTable };
        _horizontalBorderCheck = new CheckBox { Content = surface.HorizontalBorderLabel, IsChecked = state.ShowHorizontalBorder };
        _verticalBorderCheck = new CheckBox { Content = surface.VerticalBorderLabel, IsChecked = state.ShowVerticalBorder };
        _outlineBorderCheck = new CheckBox { Content = surface.OutlineBorderLabel, IsChecked = state.ShowOutlineBorder };
        _legendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, IsChecked = state.ShowLegendKeys };
        _backgroundColorBox = CreateTextBox(state.BackgroundColor);
        _borderColorBox = CreateTextBox(state.BorderColor);
        _borderWidthBox = CreateTextBox(Format(state.BorderWidthPt));
        _textColorBox = CreateTextBox(state.TextColor);
        _fontSizeBox = CreateTextBox(Format(state.FontSizePt));
        _fontFamilyBox = CreateTextBox(state.FontFamily);
        _boldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, IsChecked = state.Bold };
        _italicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, IsChecked = state.Italic };

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
        => _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

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
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
    }

    private ChartDataTableOptionsDialogInput ReadInput() => new(
        _showTableCheck.IsChecked == true,
        _horizontalBorderCheck.IsChecked == true,
        _verticalBorderCheck.IsChecked == true,
        _outlineBorderCheck.IsChecked == true,
        _legendKeysCheck.IsChecked == true,
        _backgroundColorBox.Text,
        _borderColorBox.Text,
        _borderWidthBox.Text,
        _textColorBox.Text,
        _fontSizeBox.Text,
        _fontFamilyBox.Text,
        _boldCheck.IsChecked,
        _italicCheck.IsChecked);

    private static TextBox CreateTextBox(string value) => new() { Text = value };

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
