using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart-wide default text formatting dialog.</summary>
public sealed class ChartTextOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartTextOptionsPlanner _planner;
    private readonly TextBox _fontFamilyBox;
    private readonly TextBox _fontSizeBox;
    private readonly ComboBox _boldCombo;
    private readonly ComboBox _italicCombo;
    private readonly TextBox _colorBox;

    public ChartTextOptionsDialog(EditingSession editor, ChartTextTarget target = ChartTextTarget.Chart)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartTextOptionsPlanner.FromChart(chart, target);
        var surface = ChartTextOptionsPlanner.BuildSurfacePlan(target);

        Title = surface.Title;
        Width = ChartTextOptionsPlanner.DefaultDialogWidth;
        Height = ChartTextOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _fontFamilyBox = new TextBox { Text = _planner.FontFamily ?? string.Empty, MinWidth = 180 };
        _fontSizeBox = new TextBox { Text = Format(_planner.FontSizePt), MinWidth = 180 };
        _boldCombo = BuildBooleanCombo(_planner.Bold);
        _italicCombo = BuildBooleanCombo(_planner.Italic);
        _colorBox = new TextBox { Text = _planner.ColorText, MinWidth = 180 };

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FontFamilyLabel, _fontFamilyBox, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FontSizeLabel, _fontSizeBox, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.BoldLabel, _boldCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ItalicLabel, _italicCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ColorLabel, _colorBox, 180));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartTextOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartTextOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetFontFamily(_fontFamilyBox.Text);
        _planner.SetFontSizePt(ChartTextOptionsPlanner.ParseOptionalFontSize(_fontSizeBox.Text));
        _planner.SetBold(ReadBoolean(_boldCombo));
        _planner.SetItalic(ReadBoolean(_italicCombo));
        _planner.SetColor(_colorBox.Text);
    }

    private static ComboBox BuildBooleanCombo(bool? value) => new()
    {
        ItemsSource = ChartTextOptionsPlanner.BooleanOptions,
        DisplayMemberPath = nameof(ChartTextBooleanOption.Label),
        SelectedIndex = ChartDialogOptionProjection.FindIndex(
            ChartTextOptionsPlanner.BooleanOptions,
            value,
            option => option.Value),
        MinWidth = 180,
    };

    private static bool? ReadBoolean(ComboBox combo) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartTextOptionsPlanner.BooleanOptions,
            combo.SelectedIndex,
            option => option.Value,
            default(bool?));

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
