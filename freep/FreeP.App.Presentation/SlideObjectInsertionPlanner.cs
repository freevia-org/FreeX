using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideObjectInsertionKind
{
    TextBox,
    AutoShape,
    Picture,
    Media,
    Table,
    Chart
}

public sealed record SlideObjectPicturePayload(
    byte[] Bytes,
    string ContentType);

public sealed record SlideObjectMediaPayload(
    byte[] Bytes,
    bool IsVideo,
    string ContentType);

public sealed record SlideObjectInsertionPlan(
    string CommandId,
    SlideObjectInsertionKind Kind,
    DrawingShapeKind? AutoShapeKind = null,
    int TableRows = 0,
    int TableColumns = 0,
    ChartType ChartKind = ChartType.ColumnClustered)
{
    public bool RequiresPicturePayload => Kind == SlideObjectInsertionKind.Picture;

    public bool RequiresMediaPayload => Kind == SlideObjectInsertionKind.Media;
}

public static class SlideObjectInsertionPlanner
{
    public const string TextBoxCommandId = "freep.text-box";
    public const string RectangleCommandId = "freep.shape-rectangle";
    public const string EllipseCommandId = "freep.shape-ellipse";
    public const string PictureCommandId = "freep.picture";
    public const string VideoCommandId = "freep.video";
    public const string AudioCommandId = "freep.audio";
    public const string Table3x3CommandId = "freep.insert-table-3x3";
    public const string Table2x2CommandId = "freep.insert-table-2x2";
    public const string Table4x4CommandId = "freep.insert-table-4x4";
    public const string ChartColumnCommandId = "freep.insert-chart-column";
    public const string ChartBarCommandId = "freep.insert-chart-bar";
    public const string ChartLineCommandId = "freep.insert-chart-line";
    public const string ChartPieCommandId = "freep.insert-chart-pie";

    private static readonly SlideObjectInsertionPlan[] Plans =
    [
        new(TextBoxCommandId, SlideObjectInsertionKind.TextBox),
        new(RectangleCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Rectangle),
        new(EllipseCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Ellipse),
        new(PictureCommandId, SlideObjectInsertionKind.Picture),
        new(VideoCommandId, SlideObjectInsertionKind.Media),
        new(AudioCommandId, SlideObjectInsertionKind.Media),
        new(Table3x3CommandId, SlideObjectInsertionKind.Table, TableRows: 3, TableColumns: 3),
        new(Table2x2CommandId, SlideObjectInsertionKind.Table, TableRows: 2, TableColumns: 2),
        new(Table4x4CommandId, SlideObjectInsertionKind.Table, TableRows: 4, TableColumns: 4),
        new(ChartColumnCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.ColumnClustered),
        new(ChartBarCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.BarClustered),
        new(ChartLineCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Line),
        new(ChartPieCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Pie),
    ];

    public static IReadOnlyList<SlideObjectInsertionPlan> BuiltInPlans { get; } =
        Array.AsReadOnly(Plans);

    public static IReadOnlyList<string> BuiltInCommandIds { get; } =
        Array.AsReadOnly(Plans.Select(plan => plan.CommandId).ToArray());

    private static readonly IReadOnlyDictionary<string, SlideObjectInsertionPlan> PlansByCommandId =
        Plans.ToDictionary(plan => plan.CommandId, StringComparer.Ordinal);

    public static bool TryCreatePlan(string commandId, out SlideObjectInsertionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        return PlansByCommandId.TryGetValue(commandId, out plan!);
    }

    public static SlideShape? ApplyCommand(
        EditingSession editor,
        string commandId,
        SlideObjectPicturePayload? picturePayload = null,
        SlideObjectMediaPayload? mediaPayload = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(commandId);

        return TryCreatePlan(commandId, out var plan)
            ? Apply(editor, plan, picturePayload, mediaPayload)
            : null;
    }

    public static SlideShape? Apply(
        EditingSession editor,
        SlideObjectInsertionPlan plan,
        SlideObjectPicturePayload? picturePayload = null,
        SlideObjectMediaPayload? mediaPayload = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Kind switch
        {
            SlideObjectInsertionKind.TextBox => editor.InsertDefaultTextBox(),
            SlideObjectInsertionKind.AutoShape => ApplyAutoShape(editor, plan.AutoShapeKind),
            SlideObjectInsertionKind.Picture => picturePayload is null
                ? null
                : editor.InsertPicture(picturePayload.Bytes, picturePayload.ContentType),
            SlideObjectInsertionKind.Media => mediaPayload is null
                ? null
                : editor.InsertMedia(
                    mediaPayload.Bytes,
                    mediaPayload.IsVideo,
                    mediaPayload.ContentType),
            SlideObjectInsertionKind.Table => editor.InsertTable(plan.TableRows, plan.TableColumns),
            SlideObjectInsertionKind.Chart => editor.InsertChart(plan.ChartKind),
            _ => null,
        };
    }

    public static SlideObjectPicturePayload CreatePicturePayload(
        byte[] imageBytes,
        string? fileNameOrExtension)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        return new SlideObjectPicturePayload(
            imageBytes,
            InferPictureContentType(fileNameOrExtension));
    }

    public static SlideObjectMediaPayload CreateMediaPayload(
        byte[] mediaBytes,
        string? fileNameOrExtension,
        bool isVideo)
    {
        ArgumentNullException.ThrowIfNull(mediaBytes);
        return new SlideObjectMediaPayload(
            mediaBytes,
            isVideo,
            InferMediaContentType(fileNameOrExtension, isVideo));
    }

    public static string InferPictureContentType(string? fileNameOrExtension)
    {
        var extension = Path.GetExtension(fileNameOrExtension);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = fileNameOrExtension ?? string.Empty;
        }

        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "svg" => "image/svg+xml",
            _ => "image/png",
        };
    }

    public static string InferMediaContentType(string? fileNameOrExtension, bool isVideo)
    {
        var extension = Path.GetExtension(fileNameOrExtension ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
            extension = (fileNameOrExtension ?? string.Empty).TrimStart('.').ToLowerInvariant();

        return extension switch
        {
            "mp4" => "video/mp4",
            "mov" => "video/quicktime",
            "avi" => "video/x-msvideo",
            "wmv" => "video/x-ms-wmv",
            "m4v" => "video/x-m4v",
            "mp3" => "audio/mpeg",
            "m4a" => "audio/mp4",
            "wav" => "audio/wav",
            "wma" => "audio/x-ms-wma",
            _ => isVideo ? "video/mp4" : "audio/mpeg",
        };
    }

    private static SlideShape? ApplyAutoShape(
        EditingSession editor,
        DrawingShapeKind? shapeKind)
    {
        return shapeKind switch
        {
            DrawingShapeKind.Rectangle => editor.InsertDefaultRectangle(),
            DrawingShapeKind.Ellipse => editor.InsertDefaultEllipse(),
            _ => null,
        };
    }
}
