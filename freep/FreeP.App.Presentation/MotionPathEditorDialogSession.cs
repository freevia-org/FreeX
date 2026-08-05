using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record MotionPathEditorRowEnablement(
    bool KindEnabled,
    bool DeleteEnabled,
    bool EndpointEnabled,
    bool ControlPointsEnabled);

public sealed record MotionPathEditorDialogSurfacePlan(
    string Title,
    string Introduction,
    string AddLineLabel,
    string AddCurveLabel,
    string AcceptLabel,
    string CancelLabel,
    string StartRowLabel,
    string SegmentRowLabel,
    string XLabel,
    string YLabel,
    string X1Label,
    string Y1Label,
    string X2Label,
    string Y2Label,
    string DeleteLabel,
    IReadOnlyList<MotionPathSegmentKind> SegmentKinds);

public sealed record MotionPathEditorRowInput(
    MotionPathSegmentKind? Kind,
    string? X,
    string? Y,
    string? X1,
    string? Y1,
    string? X2,
    string? Y2);

public sealed record MotionPathEditorDialogTransition(
    IReadOnlyList<MotionPathSegmentEdit> Segments,
    bool Succeeded,
    bool ShouldRenderRows,
    bool ShouldClose,
    string ValidationMessage);

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
    private readonly CultureInfo _culture;
    private IReadOnlyList<MotionPathSegmentEdit> _segments;

    public MotionPathEditorDialogSession(
        EditingSession editor,
        int animationIndex,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture ?? CultureInfo.CurrentCulture;
        var plan = MotionPathEditingPlanner.BuildPlan(
            editor.CurrentSlideAnimations,
            animationIndex);
        if (!plan.CanEdit)
            throw new InvalidOperationException(plan.Message);

        _animationIndex = plan.AnimationIndex;
        _origin = plan.Origin;
        _ptsTypes = plan.PtsTypes;
        InitialSegments = plan.Segments.ToArray();
        _segments = InitialSegments;
        Surface = BuildSurfacePlan();
    }

    public MotionPathEditorDialogSurfacePlan Surface { get; }

    public IReadOnlyList<MotionPathSegmentEdit> InitialSegments { get; }

    public MotionPathSegmentEdit CreateLineAfter(
        IReadOnlyList<MotionPathSegmentEdit> segments) =>
        MotionPathEditingPlanner.CreateLineAfter(segments);

    public MotionPathSegmentEdit CreateCubicAfter(
        IReadOnlyList<MotionPathSegmentEdit> segments) =>
        MotionPathEditingPlanner.CreateCubicAfter(segments);

    public MotionPathEditorDialogTransition AddLine(
        IEnumerable<MotionPathEditorRowInput> rows) =>
        Add(rows, MotionPathEditingPlanner.CreateLineAfter);

    public MotionPathEditorDialogTransition AddCurve(
        IEnumerable<MotionPathEditorRowInput> rows) =>
        Add(rows, MotionPathEditingPlanner.CreateCubicAfter);

    public MotionPathEditorDialogTransition Remove(
        IEnumerable<MotionPathEditorRowInput> rows,
        int rowIndex)
    {
        if (!MotionPathEditorRowProjection.CanRemove(rowIndex))
            return Success(shouldRenderRows: false);

        if (!TryParseRows(rows, out var segments, out var error))
            return Invalid(error);
        if (rowIndex >= segments.Count)
            return Success(shouldRenderRows: false);

        var updated = segments.ToList();
        updated.RemoveAt(rowIndex);
        _segments = updated;
        return Success(shouldRenderRows: true);
    }

    public MotionPathEditorDialogTransition Submit(
        IEnumerable<MotionPathEditorRowInput> rows)
    {
        if (!TryParseRows(rows, out var segments, out var error))
            return Invalid(error);
        if (!TryApply(segments, out error))
            return Invalid(error);

        _segments = segments;
        return new MotionPathEditorDialogTransition(
            _segments,
            Succeeded: true,
            ShouldRenderRows: false,
            ShouldClose: true,
            ValidationMessage: string.Empty);
    }

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

    private MotionPathEditorDialogTransition Add(
        IEnumerable<MotionPathEditorRowInput> rows,
        Func<IReadOnlyList<MotionPathSegmentEdit>, MotionPathSegmentEdit> createSegment)
    {
        if (!TryParseRows(rows, out var segments, out var error))
            return Invalid(error);

        _segments = [.. segments, createSegment(segments)];
        return Success(shouldRenderRows: true);
    }

    private bool TryParseRows(
        IEnumerable<MotionPathEditorRowInput> rows,
        out IReadOnlyList<MotionPathSegmentEdit> segments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var parsed = new List<MotionPathSegmentEdit>();
        foreach (var row in rows)
        {
            if (!MotionPathEditorRowProjection.TryParse(
                    row.Kind ?? MotionPathSegmentKind.Line,
                    row.X,
                    row.Y,
                    row.X1,
                    row.Y1,
                    row.X2,
                    row.Y2,
                    out var segment,
                    out error,
                    _culture))
            {
                segments = Array.Empty<MotionPathSegmentEdit>();
                return false;
            }

            parsed.Add(segment);
        }

        segments = parsed;
        error = string.Empty;
        return true;
    }

    private MotionPathEditorDialogTransition Success(bool shouldRenderRows) =>
        new(
            _segments,
            Succeeded: true,
            ShouldRenderRows: shouldRenderRows,
            ShouldClose: false,
            ValidationMessage: string.Empty);

    private MotionPathEditorDialogTransition Invalid(string error) =>
        new(
            _segments,
            Succeeded: false,
            ShouldRenderRows: false,
            ShouldClose: false,
            ValidationMessage: error);

    private static MotionPathEditorDialogSurfacePlan BuildSurfacePlan() =>
        new(
            Title: "Edit Motion Path",
            Introduction: "Coordinates are relative to the animated shape. Edit endpoints and curve control points, then press OK.",
            AddLineLabel: "Add line",
            AddCurveLabel: "Add curve",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            StartRowLabel: "Start",
            SegmentRowLabel: "Segment",
            XLabel: "X",
            YLabel: "Y",
            X1Label: "X1",
            Y1Label: "Y1",
            X2Label: "X2",
            Y2Label: "Y2",
            DeleteLabel: "Delete",
            SegmentKinds: Enum.GetValues<MotionPathSegmentKind>());
}
