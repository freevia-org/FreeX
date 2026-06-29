using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public sealed record DrawingObjectFormatTarget(
    DrawingObjectTarget Target,
    FormatPicturePlanner.FormatObjectValues Values)
{
    public DrawingObjectTargetKind Kind => Target.Kind;
    public Guid Id => Target.Id;
}

/// <summary>
/// Shared policy for object-format commands. Hosts own dialogs, status text, and focus; this class owns
/// selected-object resolution, supported object kinds, default object colors, and command composition.
/// </summary>
public static class DrawingObjectFormatCommandPolicy
{
    public static DrawingObjectSelectionResult<DrawingObjectFormatTarget> ResolveSelectedFormatTarget(
        Sheet? sheet,
        SelectionPaneObjectKind? selectedKind,
        Guid? selectedObjectId,
        bool requireVisible = true)
    {
        if (selectedKind is null || selectedObjectId is not { } id || id == Guid.Empty)
            return DrawingObjectSelectionResult<DrawingObjectFormatTarget>.MissingSelection();

        return selectedKind.Value switch
        {
            SelectionPaneObjectKind.Picture => Map(
                DrawingTargetResolver.ResolveSelectedPicture(sheet, selectedKind, id, requireVisible),
                picture => new DrawingObjectFormatTarget(
                    DrawingObjectTarget.FromPicture(picture),
                    FormatPicturePlanner.Capture(picture))),
            SelectionPaneObjectKind.Shape => Map(
                DrawingTargetResolver.ResolveSelectedDrawingShape(sheet, selectedKind, id, requireVisible),
                shape => new DrawingObjectFormatTarget(
                    DrawingObjectTarget.FromShape(shape),
                    FormatPicturePlanner.Capture(shape))),
            SelectionPaneObjectKind.TextBox => Map(
                DrawingTargetResolver.ResolveSelectedTextBox(sheet, selectedKind, id, requireVisible),
                textBox => new DrawingObjectFormatTarget(
                    DrawingObjectTarget.FromTextBox(textBox),
                    FormatPicturePlanner.Capture(textBox))),
            _ => DrawingObjectSelectionResult<DrawingObjectFormatTarget>.MissingSelection()
        };
    }

    public static bool SupportsFillAndOutline(DrawingObjectTargetKind kind) =>
        kind is DrawingObjectTargetKind.Shape or DrawingObjectTargetKind.TextBox;

    public static bool SupportsGradientAndEffects(DrawingObjectTargetKind kind) =>
        kind == DrawingObjectTargetKind.Shape;

    public static IReadOnlyList<IWorkbookCommand> BuildFormatCommands(
        SheetId sheetId,
        DrawingObjectFormatTarget target,
        FormatPicturePlanner.FormatObjectResult result)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(result);

        var commands = new List<IWorkbookCommand>
        {
            DrawingObjectCommandPlanner.BuildResizeCommand(sheetId, target.Kind, target.Id, result.Width, result.Height),
            DrawingObjectCommandPlanner.BuildRotateCommand(sheetId, target.Kind, target.Id, result.RotationDegrees)
        };

        if (target.Kind == DrawingObjectTargetKind.Picture)
            commands.Add(new SetPictureLockAspectRatioCommand(sheetId, target.Id, result.LockAspectRatio));

        commands.Add(DrawingObjectCommandPlanner.BuildAltTextCommand(sheetId, target.Kind, target.Id, result.AltText));
        return commands;
    }

    public static CellColor? ResolveFillColor(DrawingObjectTarget target, WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(theme);

        if (!target.HasFill)
            return null;

        return target.Kind switch
        {
            DrawingObjectTargetKind.Shape =>
                target.FillThemeColor?.Resolve(theme) ??
                target.FillColor ??
                DrawingShapeModel.ResolveDefaultFillColor(theme),
            DrawingObjectTargetKind.TextBox =>
                target.FillThemeColor?.Resolve(theme) ??
                target.FillColor ??
                CellColor.White,
            _ => CellColor.White
        };
    }

    public static CellColor ResolveOutlineColor(DrawingObjectTarget target, WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(theme);

        return target.Kind switch
        {
            DrawingObjectTargetKind.Shape =>
                target.OutlineThemeColor?.Resolve(theme) ??
                target.OutlineColor ??
                DrawingShapeModel.ResolveDefaultOutlineColor(theme),
            DrawingObjectTargetKind.TextBox =>
                target.OutlineThemeColor?.Resolve(theme) ??
                target.OutlineColor ??
                new CellColor(89, 89, 89),
            _ => CellColor.Black
        };
    }

    private static DrawingObjectSelectionResult<DrawingObjectFormatTarget> Map<T>(
        DrawingObjectSelectionResult<T> result,
        Func<T, DrawingObjectFormatTarget> mapper)
        where T : class
    {
        if (result.Target is { } target)
            return DrawingObjectSelectionResult<DrawingObjectFormatTarget>.Found(mapper(target));

        return new DrawingObjectSelectionResult<DrawingObjectFormatTarget>(null, result.Failure);
    }
}
