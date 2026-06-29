using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum ObjectFormatTarget
{
    Picture,
    Shape
}

public enum ObjectFormatTransformKind
{
    Rotate,
    FlipHorizontal,
    FlipVertical
}

public enum ObjectFormatSizeDimension
{
    Width,
    Height
}

public sealed record ObjectFormatWrapCommand(string CommandId, ImageWrapping Wrapping);

public sealed record ObjectFormatTransformCommand(
    string CommandId,
    ObjectFormatTransformKind Kind,
    double RotationDeltaDegrees = 0);

public sealed record ObjectFormatZOrderCommand(string CommandId, ZOrderOperation Operation);

public sealed record ObjectFormatSizeCommand(string CommandId, ObjectFormatSizeDimension Dimension);

public static class ObjectFormatCommandPlanner
{
    private static readonly IReadOnlyList<(string Suffix, ImageWrapping Wrapping)> WrapCatalog =
    [
        ("inline", ImageWrapping.Inline),
        ("square", ImageWrapping.Square),
        ("tight", ImageWrapping.Tight),
        ("top-bottom", ImageWrapping.TopAndBottom),
        ("behind", ImageWrapping.Behind),
        ("front", ImageWrapping.InFront),
    ];

    private static readonly IReadOnlyList<(string Suffix, ObjectFormatTransformKind Kind, double RotationDeltaDegrees)> TransformCatalog =
    [
        ("rotate-right90", ObjectFormatTransformKind.Rotate, +90),
        ("rotate-left90", ObjectFormatTransformKind.Rotate, -90),
        ("flip-vertical", ObjectFormatTransformKind.FlipVertical, 0),
        ("flip-horizontal", ObjectFormatTransformKind.FlipHorizontal, 0),
    ];

    private static readonly IReadOnlyList<(string Suffix, ZOrderOperation Operation)> ZOrderCatalog =
    [
        ("bring-to-front", ZOrderOperation.BringToFront),
        ("send-to-back", ZOrderOperation.SendToBack),
        ("bring-forward", ZOrderOperation.BringForward),
        ("send-backward", ZOrderOperation.SendBackward),
    ];

    private static readonly IReadOnlyList<(string Suffix, ObjectFormatSizeDimension Dimension)> SizeCatalog =
    [
        ("width", ObjectFormatSizeDimension.Width),
        ("height", ObjectFormatSizeDimension.Height),
    ];

    public static IReadOnlyList<ObjectFormatTarget> Targets { get; } =
    [
        ObjectFormatTarget.Picture,
        ObjectFormatTarget.Shape,
    ];

    public static string PrefixFor(ObjectFormatTarget target) => target switch
    {
        ObjectFormatTarget.Picture => "image",
        ObjectFormatTarget.Shape => "shape",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
    };

    public static string WrapDropdownCommandId(ObjectFormatTarget target) =>
        BuildCommandId(target, "wrap");

    public static string TransformDropdownCommandId(ObjectFormatTarget target) =>
        BuildCommandId(target, "rotate");

    public static IReadOnlyList<ObjectFormatWrapCommand> WrapCommands(ObjectFormatTarget target) =>
        WrapCatalog
            .Select(item => new ObjectFormatWrapCommand(
                BuildCommandId(target, $"wrap-{item.Suffix}"),
                item.Wrapping))
            .ToArray();

    public static IReadOnlyList<ObjectFormatTransformCommand> TransformCommands(ObjectFormatTarget target) =>
        TransformCatalog
            .Select(item => new ObjectFormatTransformCommand(
                BuildCommandId(target, item.Suffix),
                item.Kind,
                item.RotationDeltaDegrees))
            .ToArray();

    public static IReadOnlyList<ObjectFormatZOrderCommand> ZOrderCommands(ObjectFormatTarget target) =>
        ZOrderCatalog
            .Select(item => new ObjectFormatZOrderCommand(
                BuildCommandId(target, item.Suffix),
                item.Operation))
            .ToArray();

    public static IReadOnlyList<ObjectFormatSizeCommand> SizeCommands(ObjectFormatTarget target) =>
        SizeCatalog
            .Select(item => new ObjectFormatSizeCommand(
                BuildCommandId(target, item.Suffix),
                item.Dimension))
            .ToArray();

    public static bool TryParseSizePoints(string? text, out double points)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(
            trimmed,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out points) && points > 0;
    }

    private static string BuildCommandId(ObjectFormatTarget target, string suffix) =>
        $"freew.{PrefixFor(target)}-{suffix}";
}
