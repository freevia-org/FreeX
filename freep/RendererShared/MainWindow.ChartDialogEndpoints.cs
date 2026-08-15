using FreeP.App.Compositor;
using FreeP.Core.Model;

#if FREEP_WPF_RENDERER
using NativeWindow = System.Windows.Window;
namespace FreeP.App.Host;
#elif FREEP_AVALONIA_RENDERER
using NativeWindow = Avalonia.Controls.Window;
namespace FreeP.App.Avalonia;
#else
#error A FreeP renderer symbol is required.
#endif

public sealed partial class MainWindow
{
    internal void OpenChartDataDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartData, () => new ChartDataDialog(Editor));

    internal void OpenChartDisplayOptionsDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartDisplayOptions, () => new ChartDisplayOptionsDialog(Editor));

    internal void OpenChartAxisOptionsDialog() => OpenChartAxisOptionsDialog(null);

    internal void OpenChartAxisOptionsDialog(ChartAxisKind? initialAxis) =>
        OpenChartDialog(PresentationDomainDialogKind.ChartAxisOptions, () => new ChartAxisOptionsDialog(Editor, initialAxis));

    internal void OpenChartSeriesOptionsDialog() => OpenChartSeriesOptionsDialog(null);

    internal void OpenChartSeriesOptionsDialog(int? initialSeriesIndex) =>
        OpenChartDialog(PresentationDomainDialogKind.ChartSeriesOptions, () => new ChartSeriesOptionsDialog(Editor, initialSeriesIndex));

    private void OnChartPointDoubleClick(ChartPointHit hit)
    {
        Editor.Select(hit.ShapeId);
        OpenChartPointOptionsDialog(hit.SeriesIndex, hit.PointIndex);
    }

    internal void OpenChartPointOptionsDialog(int? seriesIndex = null, int? pointIndex = null) =>
        OpenChartDialog(PresentationDomainDialogKind.ChartPointOptions, () => new ChartPointOptionsDialog(Editor, seriesIndex, pointIndex));

    internal void OpenChartLayoutOptionsDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartLayoutOptions, () => new ChartLayoutOptionsDialog(Editor));

    internal void OpenChartExSeriesLayoutDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartExSeriesLayout, () => new ChartExSeriesLayoutDialog(Editor));

    internal void OpenChartDataTableOptionsDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartDataTableOptions, () => new ChartDataTableOptionsDialog(Editor));

    internal void OpenChartBubbleOptionsDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartBubbleOptions, () => new ChartBubbleOptionsDialog(Editor));

    internal void OpenChartPieOptionsDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartPieOptions, () => new ChartPieOptionsDialog(Editor));

    internal void OpenChartPlotStyleOptionsDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartPlotStyleOptions, () => new ChartPlotStyleOptionsDialog(Editor));

    internal void OpenChart3DViewOptionsDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.Chart3DViewOptions, () => new Chart3DViewOptionsDialog(Editor));

    internal void OpenChartTextOptionsDialog() => OpenChartTextOptionsDialog(ChartTextTarget.Chart);

    internal void OpenChartTextOptionsDialog(ChartTextTarget target) =>
        OpenChartDialog(PresentationDomainDialogKind.ChartTextOptions, () => new ChartTextOptionsDialog(Editor, target));

    internal void OpenChartAreaOptionsDialog() => OpenChartAreaOptionsDialog(null);

    internal void OpenChartAreaOptionsDialog(ChartAreaFormattingTarget? initialTarget) =>
        OpenChartDialog(PresentationDomainDialogKind.ChartAreaOptions, () => new ChartAreaOptionsDialog(Editor, initialTarget));

    internal void OpenChartProtectionOptionsDialog() =>
        OpenChartDialog(PresentationDomainDialogKind.ChartProtectionOptions, () => new ChartProtectionOptionsDialog(Editor));

    private void OpenChartDialog(PresentationDomainDialogKind kind, Func<NativeWindow> createDialog)
    {
        ArgumentNullException.ThrowIfNull(createDialog);
        if (!_workareaSession.CanOpenDomainDialog(kind))
            return;

        ShowDomainDialog(createDialog());
    }
}
