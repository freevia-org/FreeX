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

public enum ObjectFormatShapeFillKind
{
    NoFill,
    GradientBlue,
    GradientOrange,
    PatternDiagonalCross
}

public enum ObjectFormatShapeOutlineKind
{
    NoOutline,
    Solid,
    Dash,
    Dot
}

public sealed record ObjectFormatWrapCommand(string CommandId, ImageWrapping Wrapping);

public sealed record ObjectFormatTransformCommand(
    string CommandId,
    ObjectFormatTransformKind Kind,
    double RotationDeltaDegrees = 0);

public sealed record ObjectFormatZOrderCommand(string CommandId, ZOrderOperation Operation);

public sealed record ObjectFormatSizeCommand(string CommandId, ObjectFormatSizeDimension Dimension);

public sealed record ObjectFormatShapeFillCommand(string CommandId, ObjectFormatShapeFillKind Kind);

public sealed record ObjectFormatShapeOutlineCommand(string CommandId, ObjectFormatShapeOutlineKind Kind);

public sealed record ObjectFormatShapeOutlinePlan(string? ColorHex, double WidthPt, string? Dash);

public static class ObjectFormatCommandPlanner
{
    public const double MinimumShapeOutlineWidthPt = 0.75;

    public static string ShapePositionDialogTitle(bool isGroupLocal) =>
        isGroupLocal ? "Shape Position in Group" : "Shape Position";

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

    private static readonly IReadOnlyList<(string Suffix, ObjectFormatShapeFillKind Kind)> ShapeFillCatalog =
    [
        ("no-fill", ObjectFormatShapeFillKind.NoFill),
        ("gradient-blue", ObjectFormatShapeFillKind.GradientBlue),
        ("gradient-orange", ObjectFormatShapeFillKind.GradientOrange),
        ("pattern-diag", ObjectFormatShapeFillKind.PatternDiagonalCross),
    ];

    private static readonly IReadOnlyList<(string Suffix, ObjectFormatShapeOutlineKind Kind)> ShapeOutlineCatalog =
    [
        ("no-outline", ObjectFormatShapeOutlineKind.NoOutline),
        ("solid", ObjectFormatShapeOutlineKind.Solid),
        ("dash", ObjectFormatShapeOutlineKind.Dash),
        ("dot", ObjectFormatShapeOutlineKind.Dot),
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

    public static string ShapeFillCommandId => BuildCommandId(ObjectFormatTarget.Shape, "fill");

    public static string ShapeOutlineCommandId => BuildCommandId(ObjectFormatTarget.Shape, "outline");

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

    public static IReadOnlyList<ObjectFormatShapeFillCommand> ShapeFillCommands() =>
        ShapeFillCatalog
            .Select(item => new ObjectFormatShapeFillCommand(
                BuildCommandId(ObjectFormatTarget.Shape, $"fill-{item.Suffix}"),
                item.Kind))
            .ToArray();

    public static IReadOnlyList<ObjectFormatShapeOutlineCommand> ShapeOutlineCommands() =>
        ShapeOutlineCatalog
            .Select(item => new ObjectFormatShapeOutlineCommand(
                BuildCommandId(ObjectFormatTarget.Shape, $"outline-{item.Suffix}"),
                item.Kind))
            .ToArray();

    public static bool CanFormatShapeFillOutline(ShapeKind? selectedShapeKind) =>
        selectedShapeKind is ShapeKind.Rectangle
            or ShapeKind.RoundedRectangle
            or ShapeKind.Ellipse
            or ShapeKind.TextBox;

    public static bool UsesExtendedShapeFill(ObjectFormatShapeFillKind kind) =>
        kind is ObjectFormatShapeFillKind.GradientBlue
            or ObjectFormatShapeFillKind.GradientOrange
            or ObjectFormatShapeFillKind.PatternDiagonalCross;

    public static ShapeFill? BuildShapeExtendedFill(ObjectFormatShapeFillKind kind) => kind switch
    {
        ObjectFormatShapeFillKind.NoFill => null,
        ObjectFormatShapeFillKind.GradientBlue => ShapeFill.LinearGradient(
            5400000,
            new GradientStop(0, "#4472C4"),
            new GradientStop(100000, "#1F4E79")),
        ObjectFormatShapeFillKind.GradientOrange => ShapeFill.LinearGradient(
            5400000,
            new GradientStop(0, "#ED7D31"),
            new GradientStop(100000, "#C55A11")),
        ObjectFormatShapeFillKind.PatternDiagonalCross => ShapeFill.Patterned("diagCross", "#4472C4", "#FFFFFF"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static ObjectFormatShapeOutlinePlan PlanShapeOutline(
        ObjectFormatShapeOutlineKind kind,
        string? currentColorHex,
        double currentWidthPt) => kind switch
        {
            ObjectFormatShapeOutlineKind.NoOutline => new ObjectFormatShapeOutlinePlan(null, 0, null),
            ObjectFormatShapeOutlineKind.Solid => BuildShapeOutlinePlan(currentColorHex, currentWidthPt, null),
            ObjectFormatShapeOutlineKind.Dash => BuildShapeOutlinePlan(currentColorHex, currentWidthPt, "dash"),
            ObjectFormatShapeOutlineKind.Dot => BuildShapeOutlinePlan(currentColorHex, currentWidthPt, "sysDot"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public static bool TryParseSizePoints(string? text, out double points)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(
            trimmed,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out points) && points > 0;
    }

    private static ObjectFormatShapeOutlinePlan BuildShapeOutlinePlan(
        string? currentColorHex,
        double currentWidthPt,
        string? dash)
    {
        var color = string.IsNullOrWhiteSpace(currentColorHex) ? "000000" : currentColorHex;
        var width = Math.Max(MinimumShapeOutlineWidthPt, currentWidthPt);
        return new ObjectFormatShapeOutlinePlan(color, width, dash);
    }

    private static string BuildCommandId(ObjectFormatTarget target, string suffix) =>
        $"freew.{PrefixFor(target)}-{suffix}";
}
