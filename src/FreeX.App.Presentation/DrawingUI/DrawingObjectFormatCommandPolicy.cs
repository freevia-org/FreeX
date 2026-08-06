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

    public static IReadOnlyList<IWorkbookCommand> BuildPictureFormatCommands(
        SheetId sheetId,
        PictureModel picture,
        FormatPicturePlanner.PictureFormatResult result)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(result);

        var target = new DrawingObjectFormatTarget(
            DrawingObjectTarget.FromPicture(picture),
            FormatPicturePlanner.Capture(picture));
        var commands = new List<IWorkbookCommand>(BuildFormatCommands(sheetId, target, result.Format));
        if (picture.Kind == PictureKind.Image)
        {
            commands.Add(new SetPictureCropCommand(
                sheetId,
                picture.Id,
                result.Crop.Left,
                result.Crop.Top,
                result.Crop.Right,
                result.Crop.Bottom));
        }

        return commands;
    }

    public static IWorkbookCommand BuildPictureFormatCommand(
        SheetId sheetId,
        PictureModel? picture,
        FormatPicturePlanner.PictureFormatResult result,
        string commandTitle,
        string missingPictureMessage)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(missingPictureMessage);

        return picture is null
            ? new MissingPictureFormatTargetCommand(missingPictureMessage)
            : new CompositeWorkbookCommand(commandTitle, BuildPictureFormatCommands(sheetId, picture, result));
    }

    public static IWorkbookCommand BuildResizeCommand(
        SheetId sheetId,
        DrawingObjectFormatTarget target,
        ObjectSizeDialogSize size)
    {
        ArgumentNullException.ThrowIfNull(target);
        return BuildResizeCommand(sheetId, target.Kind, target.Id, size);
    }

    public static IWorkbookCommand BuildResizeCommand(
        SheetId sheetId,
        DrawingObjectTarget target,
        ObjectSizeDialogSize size)
    {
        ArgumentNullException.ThrowIfNull(target);
        return BuildResizeCommand(sheetId, target.Kind, target.Id, size);
    }

    public static IWorkbookCommand BuildResizeCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        ObjectSizeDialogSize size) =>
        DrawingObjectCommandPlanner.BuildResizeCommand(sheetId, kind, objectId, size.Width, size.Height);

    public static IWorkbookCommand BuildRotationCommand(
        SheetId sheetId,
        DrawingObjectFormatTarget target,
        FormatPicturePlanner.RotationResult rotation)
    {
        ArgumentNullException.ThrowIfNull(target);
        return DrawingObjectCommandPlanner.BuildRotateCommand(sheetId, target.Kind, target.Id, rotation.Degrees);
    }

    public static IWorkbookCommand BuildRotationCommand(
        SheetId sheetId,
        DrawingObjectTarget target,
        FormatPicturePlanner.RotationResult rotation)
    {
        ArgumentNullException.ThrowIfNull(target);
        return DrawingObjectCommandPlanner.BuildRotateCommand(sheetId, target.Kind, target.Id, rotation.Degrees);
    }

    public static IWorkbookCommand BuildRotationCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        FormatPicturePlanner.RotationResult rotation) =>
        DrawingObjectCommandPlanner.BuildRotateCommand(sheetId, kind, objectId, rotation.Degrees);

    public static IWorkbookCommand BuildAltTextCommand(
        SheetId sheetId,
        DrawingObjectFormatTarget target,
        string? altText)
    {
        ArgumentNullException.ThrowIfNull(target);
        return BuildAltTextCommand(sheetId, target.Kind, target.Id, altText);
    }

    public static IWorkbookCommand BuildAltTextCommand(
        SheetId sheetId,
        DrawingObjectAltTextTarget target,
        string? altText)
    {
        ArgumentNullException.ThrowIfNull(target);
        return BuildAltTextCommand(sheetId, target.Kind, target.Id, altText);
    }

    public static IWorkbookCommand BuildAltTextCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        string? altText) =>
        DrawingObjectCommandPlanner.BuildAltTextCommand(
            sheetId,
            kind,
            objectId,
            FormatPicturePlanner.NormalizeAltText(altText));

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

    private sealed class MissingPictureFormatTargetCommand(string message) : IWorkbookCommand
    {
        public string Label => "Unavailable";

        public CommandOutcome Apply(ICommandContext context) => new(false, message);

        public void Revert(ICommandContext context)
        {
        }
    }
}
