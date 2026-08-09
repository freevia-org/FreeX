using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationDomainDialogKind
{
    ChartData,
    ChartDisplayOptions,
    ChartAxisOptions,
    ChartSeriesOptions,
    ChartPointOptions,
    ChartLayoutOptions,
    ChartExSeriesLayout,
    ChartDataTableOptions,
    ChartBubbleOptions,
    ChartPieOptions,
    ChartPlotStyleOptions,
    Chart3DViewOptions,
    ChartTextOptions,
    ChartAreaOptions,
    ChartProtectionOptions,
    RotationOptions,
}

/// <summary>Owns renderer-neutral availability rules for domain-specific native dialogs.</summary>
public static class PresentationDomainDialogLaunchPlanner
{
    public static bool CanOpen(
        EditingSession editor,
        PresentationDomainDialogKind dialogKind)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return dialogKind switch
        {
            PresentationDomainDialogKind.ChartData => editor.CanEditSelectedChartData,
            PresentationDomainDialogKind.ChartDisplayOptions or
            PresentationDomainDialogKind.ChartAxisOptions or
            PresentationDomainDialogKind.ChartSeriesOptions or
            PresentationDomainDialogKind.ChartPointOptions or
            PresentationDomainDialogKind.ChartLayoutOptions or
            PresentationDomainDialogKind.ChartDataTableOptions or
            PresentationDomainDialogKind.Chart3DViewOptions or
            PresentationDomainDialogKind.ChartTextOptions or
            PresentationDomainDialogKind.ChartAreaOptions =>
                editor.CanEditSelectedChartFormatting,
            PresentationDomainDialogKind.ChartExSeriesLayout =>
                editor.CanEditSelectedChartFormatting &&
                ChartExSeriesLayoutPlanner.CanEdit(editor.SelectedChart),
            PresentationDomainDialogKind.ChartBubbleOptions =>
                editor.CanEditSelectedChartFormatting &&
                editor.SelectedChart is { ChartType: ChartType.Bubble },
            PresentationDomainDialogKind.ChartPieOptions =>
                editor.CanEditSelectedChartFormatting &&
                editor.SelectedChart is { ChartType: ChartType.Pie or ChartType.Doughnut },
            PresentationDomainDialogKind.ChartPlotStyleOptions =>
                editor.CanEditSelectedChartFormatting &&
                editor.SelectedChart is { ChartType: ChartType.Scatter or ChartType.Radar },
            PresentationDomainDialogKind.ChartProtectionOptions =>
                editor.SelectedChart is not null,
            PresentationDomainDialogKind.RotationOptions =>
                editor.SelectedShapeIds.Count > 0,
            _ => throw new ArgumentOutOfRangeException(nameof(dialogKind), dialogKind, null),
        };
    }
}
