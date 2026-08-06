using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartDataTableOptionsDialog : Window
{
    private readonly ChartDataTableOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartDataTableOptionsDialog(EditingSession editor)
    {
        _session = new ChartDataTableOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan(CultureInfo.CurrentCulture);
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false));

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        CanResize = plan.IsResizable;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        Content = _form.Content;
    }

    internal ChartDataTableOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

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
        _form.SetChecked(ChartOptionsDialogFieldId.ShowDataTable, showDataTable);
        _form.SetChecked(ChartOptionsDialogFieldId.HorizontalBorder, showHorizontalBorder);
        _form.SetChecked(ChartOptionsDialogFieldId.VerticalBorder, showVerticalBorder);
        _form.SetChecked(ChartOptionsDialogFieldId.OutlineBorder, showOutlineBorder);
        _form.SetChecked(ChartOptionsDialogFieldId.LegendKeys, showLegendKeys);
        _form.SetText(ChartOptionsDialogFieldId.BackgroundColor, backgroundColor);
        _form.SetText(ChartOptionsDialogFieldId.BorderColor, borderColor);
        _form.SetText(ChartOptionsDialogFieldId.BorderWidth, Format(borderWidthPt));
        _form.SetText(ChartOptionsDialogFieldId.TextColor, textColor);
        _form.SetText(ChartOptionsDialogFieldId.FontSize, Format(fontSizePt));
        _form.SetText(ChartOptionsDialogFieldId.FontFamily, fontFamily);
        _form.SetChecked(ChartOptionsDialogFieldId.Bold, bold);
        _form.SetChecked(ChartOptionsDialogFieldId.Italic, italic);
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
    }

    private ChartDataTableOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
