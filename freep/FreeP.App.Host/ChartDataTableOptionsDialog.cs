using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart data-table options dialog.</summary>
public sealed class ChartDataTableOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
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

    public ChartDataTableOptionsDialog(EditingSession editor)
    {
        _session = new ChartDataTableOptionsDialogSession(editor);
        var state = _session.State;
        var surface = ChartDataTableOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDataTableOptionsPlanner.DefaultDialogWidth;
        Height = ChartDataTableOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _showTableCheck = new CheckBox { Content = surface.ShowDataTableLabel, IsChecked = state.ShowDataTable };
        _horizontalBorderCheck = new CheckBox { Content = surface.HorizontalBorderLabel, IsChecked = state.ShowHorizontalBorder };
        _verticalBorderCheck = new CheckBox { Content = surface.VerticalBorderLabel, IsChecked = state.ShowVerticalBorder };
        _outlineBorderCheck = new CheckBox { Content = surface.OutlineBorderLabel, IsChecked = state.ShowOutlineBorder };
        _legendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, IsChecked = state.ShowLegendKeys };
        _backgroundColorBox = CreateTextBox(state.BackgroundColor);
        _borderColorBox = CreateTextBox(state.BorderColor);
        _borderWidthBox = CreateTextBox(FormatOptional(state.BorderWidthPt));
        _textColorBox = CreateTextBox(state.TextColor);
        _fontSizeBox = CreateTextBox(FormatOptional(state.FontSizePt));
        _fontFamilyBox = CreateTextBox(state.FontFamily);
        _boldCheck = new CheckBox { Content = surface.BoldLabel, IsThreeState = true, IsChecked = state.Bold };
        _italicCheck = new CheckBox { Content = surface.ItalicLabel, IsThreeState = true, IsChecked = state.Italic };

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
        => _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (!result.Succeeded)
        {
            MessageBox.Show(this, result.Error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
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

    private static TextBox CreateTextBox(string value) => new() { Text = value, MinWidth = 150 };

    private static string FormatOptional(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture, "0.###");
}
