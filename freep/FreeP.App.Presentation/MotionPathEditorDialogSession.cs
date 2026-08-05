using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record MotionPathEditorRowEnablement(
    bool KindEnabled,
    bool DeleteEnabled,
    bool EndpointEnabled,
    bool ControlPointsEnabled);

public static class MotionPathEditorRowProjection
{
    public static string Format(double value, CultureInfo? culture = null) =>
        value.ToString("G", culture ?? CultureInfo.CurrentCulture);

    public static MotionPathEditorRowEnablement BuildEnablement(
        MotionPathSegmentKind kind,
        bool isFirstRow = false) =>
        new(
            KindEnabled: !isFirstRow,
            DeleteEnabled: !isFirstRow,
            EndpointEnabled: kind != MotionPathSegmentKind.Close,
            ControlPointsEnabled: kind == MotionPathSegmentKind.Cubic);

    public static bool CanRemove(int rowIndex) => rowIndex > 0;

    public static bool TryParse(
        MotionPathSegmentKind kind,
        string? x,
        string? y,
        string? x1,
        string? y1,
        string? x2,
        string? y2,
        out MotionPathSegmentEdit edit,
        out string error,
        CultureInfo? culture = null)
    {
        var effectiveCulture = culture ?? CultureInfo.CurrentCulture;
        if (!TryParseValue(x, "X", effectiveCulture, out var xValue, out error)
            || !TryParseValue(y, "Y", effectiveCulture, out var yValue, out error)
            || !TryParseValue(x1, "X1", effectiveCulture, out var x1Value, out error)
            || !TryParseValue(y1, "Y1", effectiveCulture, out var y1Value, out error)
            || !TryParseValue(x2, "X2", effectiveCulture, out var x2Value, out error)
            || !TryParseValue(y2, "Y2", effectiveCulture, out var y2Value, out error))
        {
            edit = default!;
            return false;
        }

        edit = new MotionPathSegmentEdit(
            kind,
            xValue,
            yValue,
            x1Value,
            y1Value,
            x2Value,
            y2Value);
        error = string.Empty;
        return true;
    }

    private static bool TryParseValue(
        string? text,
        string name,
        CultureInfo culture,
        out double value,
        out string error)
    {
        if (double.TryParse(text, NumberStyles.Float, culture, out value))
        {
            error = string.Empty;
            return true;
        }

        error = $"{name} must be a number.";
        return false;
    }
}

public sealed class MotionPathEditorDialogSession
{
    private readonly EditingSession _editor;
    private readonly int _animationIndex;
    private readonly string _origin;
    private readonly string? _ptsTypes;

    public MotionPathEditorDialogSession(
        EditingSession editor,
        int animationIndex)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var plan = MotionPathEditingPlanner.BuildPlan(
            editor.CurrentSlideAnimations,
            animationIndex);
        if (!plan.CanEdit)
            throw new InvalidOperationException(plan.Message);

        _animationIndex = plan.AnimationIndex;
        _origin = plan.Origin;
        _ptsTypes = plan.PtsTypes;
        InitialSegments = plan.Segments.ToArray();
    }

    public IReadOnlyList<MotionPathSegmentEdit> InitialSegments { get; }

    public MotionPathSegmentEdit CreateLineAfter(
        IReadOnlyList<MotionPathSegmentEdit> segments) =>
        MotionPathEditingPlanner.CreateLineAfter(segments);

    public MotionPathSegmentEdit CreateCubicAfter(
        IReadOnlyList<MotionPathSegmentEdit> segments) =>
        MotionPathEditingPlanner.CreateCubicAfter(segments);

    public bool TryApply(
        IEnumerable<MotionPathSegmentEdit> segments,
        out string error) =>
        MotionPathEditingPlanner.TryApply(
            _editor,
            _animationIndex,
            segments,
            _origin,
            _ptsTypes,
            out error);
}
