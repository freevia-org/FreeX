using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

using SharedSelectionPanePlanner = FreeX.App.Services.SelectionPanePlanner;

public static class SelectionPanePlanner
{
    public static IReadOnlyList<SelectionPaneItem> BuildItems(Sheet sheet) =>
        SharedSelectionPanePlanner.BuildItems(sheet, CreateText());

    private static SelectionPanePlannerText CreateText() =>
        new(
            UiText.Get("SelectionPane_DefaultChartName"),
            UiText.Get("SelectionPane_DefaultPictureName"),
            UiText.Get("SelectionPane_DefaultTextBoxName"),
            UiText.Get("SelectionPane_DefaultShapeNameFormat"),
            UiText.Get("SelectionPane_DefaultEllipseName"),
            UiText.Get("SelectionPane_DefaultLineName"),
            UiText.Get("SelectionPane_DefaultRectangleName"));
}
