namespace FreeW.Core.Model;

/// <summary>
/// Snapshot of a shape's preset-geometry identity (preset token, adjust values, the cached outline and
/// its signature), captured at Apply so a command can restore exactly what was there.
/// </summary>
internal sealed class ShapePresetState
{
    private string? _presetName;
    private ShapeAdjustment[] _adjustments = [];
    private string? _signature;
    private CustomGeometry? _geometry;

    public static ShapePresetState Capture(Shape shape) => new()
    {
        _presetName = shape.PresetName,
        _adjustments = [.. shape.PresetAdjustments],
        _signature = shape.PresetOutlineSignature,
        _geometry = shape.CustomGeometry,
    };

    /// <summary>
    /// Drops the preset identity. The cached outline goes with it only while it still is the preset's
    /// own outline; a user-edited freeform outline is left alone.
    /// </summary>
    public static void Clear(Shape shape)
    {
        if (shape.PresetName is null)
            return;
        if (shape.HasIntactPresetGeometry)
            shape.CustomGeometry = null;
        shape.PresetName = null;
        shape.PresetAdjustments.Clear();
        shape.PresetOutlineSignature = null;
    }

    public void Restore(Shape shape)
    {
        shape.PresetName = _presetName;
        shape.PresetAdjustments.Clear();
        shape.PresetAdjustments.AddRange(_adjustments);
        shape.PresetOutlineSignature = _signature;
        shape.CustomGeometry = _geometry;
    }
}

/// <summary>
/// Change the shape at the given paragraph/run indices to a DrawingML preset geometry (Change Shape
/// gallery), replacing any previous preset or freeform outline. Captures the prior geometry at Apply.
/// </summary>
public sealed class SetShapePresetCommand(
    int paragraphIndex,
    int runIndex,
    string presetName,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private ShapeKind _previousKind;
    private ShapePresetState? _previous;
    private bool _applied;

    public string Label => "Change Shape";

    public bool HasEffect(IDocumentCommandContext context) =>
        !string.IsNullOrEmpty(presetName)
        && ShapeAt(context) is { } shape
        && !(shape.HasIntactPresetGeometry
             && string.Equals(shape.PresetName, presetName, StringComparison.Ordinal)
             && shape.PresetAdjustments.Count == 0);

    public void Apply(IDocumentCommandContext context)
    {
        if (string.IsNullOrEmpty(presetName) || ShapeAt(context) is not { } shape) return;
        _previousKind = shape.Kind;
        _previous = ShapePresetState.Capture(shape);
        PresetShapeFactory.ApplyPreset(shape, presetName);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.Kind = _previousKind;
        _previous?.Restore(shape);
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        ShapeCommandTargetResolver.TryGetShape(context, paragraphIndex, runIndex, childPath, out var shape)
            ? shape : null;
}

/// <summary>Creates and re-targets shapes by DrawingML preset token.</summary>
public static class PresetShapeFactory
{
    /// <summary>
    /// Makes <paramref name="shape"/> the given preset. rect / roundRect / ellipse map onto the named
    /// <see cref="ShapeKind"/> values (a text box stays a text box for a plain rect); every other
    /// preset keeps a rectangle kind and carries the preset token plus its synthesized outline.
    /// </summary>
    public static void ApplyPreset(Shape shape, string presetName)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentException.ThrowIfNullOrEmpty(presetName);

        shape.PresetAdjustments.Clear();
        switch (presetName)
        {
            case "rect":
                shape.Kind = shape.HasText ? ShapeKind.TextBox : ShapeKind.Rectangle;
                break;
            case "roundRect":
                shape.Kind = ShapeKind.RoundedRectangle;
                break;
            case "ellipse":
                shape.Kind = ShapeKind.Ellipse;
                break;
            default:
                shape.Kind = shape.HasText ? ShapeKind.TextBox : ShapeKind.Rectangle;
                shape.PresetName = presetName;
                PresetShapeGeometry.ApplyPresetOutline(shape);
                return;
        }

        shape.PresetName = null;
        shape.PresetOutlineSignature = null;
        shape.CustomGeometry = null;
    }

    /// <summary>Creates a new shape of the given preset and size.</summary>
    public static Shape Create(string presetName, double widthPt, double heightPt, string? fillColorHex = null)
    {
        var shape = new Shape(ShapeKind.Rectangle, widthPt, heightPt, fillColorHex);
        ApplyPreset(shape, presetName);
        return shape;
    }
}
