using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartPieOptionsDialog : Window
{
    private readonly ChartPieOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartPieOptionsDialog(EditingSession editor)
    {
        _session = new ChartPieOptionsDialogSession(editor);
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

    internal ChartPieOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(int? firstSliceAngleDegrees, int doughnutHolePercent)
    {
        _form.SetText(ChartOptionsDialogFieldId.FirstSliceAngle, Format(firstSliceAngleDegrees ?? 0));
        _form.SetText(ChartOptionsDialogFieldId.DoughnutHole, Format(doughnutHolePercent));
    }

    internal void SetOfPieOptionsForTests(
        OfPieType type,
        OfPieSplitType splitType,
        double? splitPosition,
        int secondPieSizePercent,
        string customPointIndices,
        int? gapWidthPercent = null,
        bool seriesLines = false)
    {
        EnsureOfPie();
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.OfPieType, type == OfPieType.Bar ? 1 : 0);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.OfPieSplitType, (int)splitType);
        _form.SetText(ChartOptionsDialogFieldId.OfPieSplitPosition, Format(splitPosition ?? 0));
        _form.SetText(ChartOptionsDialogFieldId.OfPieSecondPieSize, Format(secondPieSizePercent));
        _form.SetText(ChartOptionsDialogFieldId.OfPieCustomPointIndices, customPointIndices);
        _form.SetText(ChartOptionsDialogFieldId.OfPieGapWidth, Format(gapWidthPercent));
        _form.SetChecked(ChartOptionsDialogFieldId.OfPieSeriesLines, seriesLines);
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
    }

    private ChartPieOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());

    private void EnsureOfPie()
    {
        if (!_session.State.IsOfPie)
            throw new InvalidOperationException("The selected chart is not an OfPie chart.");
    }

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
