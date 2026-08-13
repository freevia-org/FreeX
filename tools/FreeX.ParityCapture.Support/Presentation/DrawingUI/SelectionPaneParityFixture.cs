using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public static class SelectionPaneParityFixture
{
    public const string ChartName = "Revenue Chart";
    public const string ShapeName = "Rectangle 1";

    public static IReadOnlyList<SelectionPaneItem> CreateDialogItems(
        Guid chartId,
        Guid shapeId,
        bool chartIsVisible = true,
        bool shapeIsVisible = true) =>
    [
        new SelectionPaneItem(
            SelectionPaneObjectKind.Chart,
            chartId,
            ChartName,
            chartIsVisible,
            CanMoveUp: false,
            CanMoveDown: false),
        new SelectionPaneItem(
            SelectionPaneObjectKind.Shape,
            shapeId,
            ShapeName,
            shapeIsVisible,
            CanMoveUp: false,
            CanMoveDown: false),
    ];
}
