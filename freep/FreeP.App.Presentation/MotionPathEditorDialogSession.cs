using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record MotionPathEditorRowEnablement(
    bool KindEnabled,
    bool DeleteEnabled,
    bool EndpointEnabled,
    bool ControlPointsEnabled);

public enum MotionPathEditorDialogField
{
    Introduction,
    SegmentKind,
    X,
    Y,
    X1,
    Y1,
    X2,
    Y2,
    Validation,
}

public enum MotionPathEditorDialogAction
{
    AddLine,
    AddCurve,
    Delete,
    Accept,
    Cancel,
}

public sealed record MotionPathEditorDialogSurfacePlan(
    PresentationDialogSurfacePlan<MotionPathEditorDialogField, MotionPathEditorDialogAction> Schema,
    string StartRowLabel,
    string SegmentRowLabel,
    IReadOnlyList<MotionPathSegmentKind> SegmentKinds)
{
    public string Title => Schema.Title;

    public string Introduction => Field(MotionPathEditorDialogField.Introduction).Label;

    public string AddLineLabel => Action(MotionPathEditorDialogAction.AddLine).Label;

    public string AddCurveLabel => Action(MotionPathEditorDialogAction.AddCurve).Label;

    public string AcceptLabel => Action(MotionPathEditorDialogAction.Accept).Label;

    public string CancelLabel => Action(MotionPathEditorDialogAction.Cancel).Label;

    public string XLabel => Field(MotionPathEditorDialogField.X).Label;

    public string YLabel => Field(MotionPathEditorDialogField.Y).Label;

    public string X1Label => Field(MotionPathEditorDialogField.X1).Label;

    public string Y1Label => Field(MotionPathEditorDialogField.Y1).Label;

    public string X2Label => Field(MotionPathEditorDialogField.X2).Label;

    public string Y2Label => Field(MotionPathEditorDialogField.Y2).Label;

    public string DeleteLabel => Action(MotionPathEditorDialogAction.Delete).Label;

    public PresentationDialogFieldPlan<MotionPathEditorDialogField> Field(
        MotionPathEditorDialogField field) => Schema.Field(field);

    public PresentationDialogFieldPlan<MotionPathEditorDialogField> Field(
        MotionPathEditorDialogField field,
        int rowIndex) => Schema.Field(
            field,
            rowIndex.ToString(CultureInfo.InvariantCulture));

    public PresentationDialogActionPlan<MotionPathEditorDialogAction> Action(
        MotionPathEditorDialogAction action) => Schema.Action(action);

    public PresentationDialogActionPlan<MotionPathEditorDialogAction> Action(
        MotionPathEditorDialogAction action,
        int rowIndex) => Schema.Action(
            action,
            rowIndex.ToString(CultureInfo.InvariantCulture));
}

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

public sealed record MotionPathEditorRowPlan(
    int RowIndex,
    string RowLabel,
    MotionPathSegmentKind Kind,
    string X,
    string Y,
    string X1,
    string Y1,
    string X2,
    string Y2,
    MotionPathEditorRowEnablement Enablement);

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

    public static MotionPathEditorRowPlan BuildPlan(
        MotionPathEditorDialogSurfacePlan surface,
        MotionPathSegmentEdit segment,
        int rowIndex,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(segment);
        var isFirstRow = rowIndex == 0;
        return new(
            rowIndex,
            isFirstRow ? surface.StartRowLabel : surface.SegmentRowLabel,
            segment.Kind,
            Format(segment.X, culture),
            Format(segment.Y, culture),
            Format(segment.X1, culture),
            Format(segment.Y1, culture),
            Format(segment.X2, culture),
            Format(segment.Y2, culture),
            BuildEnablement(segment.Kind, isFirstRow));
    }

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
            new PresentationDialogSurfacePlan<MotionPathEditorDialogField, MotionPathEditorDialogAction>(
                "Edit Motion Path",
                "Edit Motion Path dialog",
                "FreeP.MotionPath.Window",
                [
                    Field(MotionPathEditorDialogField.Introduction,
                        PresentationDialogControlKind.Label,
                        "Coordinates are relative to the animated shape. Edit endpoints and curve control points, then press OK.",
                        "Motion path editing guidance"),
                    Field(MotionPathEditorDialogField.SegmentKind,
                        PresentationDialogControlKind.Choice, "Type", "Motion path segment type"),
                    Field(MotionPathEditorDialogField.X,
                        PresentationDialogControlKind.Text, "X", "Segment endpoint X"),
                    Field(MotionPathEditorDialogField.Y,
                        PresentationDialogControlKind.Text, "Y", "Segment endpoint Y"),
                    Field(MotionPathEditorDialogField.X1,
                        PresentationDialogControlKind.Text, "X1", "First control point X"),
                    Field(MotionPathEditorDialogField.Y1,
                        PresentationDialogControlKind.Text, "Y1", "First control point Y"),
                    Field(MotionPathEditorDialogField.X2,
                        PresentationDialogControlKind.Text, "X2", "Second control point X"),
                    Field(MotionPathEditorDialogField.Y2,
                        PresentationDialogControlKind.Text, "Y2", "Second control point Y"),
                    Field(MotionPathEditorDialogField.Validation,
                        PresentationDialogControlKind.Status, string.Empty, "Motion path validation status"),
                ],
                [
                    Action(MotionPathEditorDialogAction.AddLine, "Add line", "Add line segment"),
                    Action(MotionPathEditorDialogAction.AddCurve, "Add curve", "Add curve segment"),
                    Action(MotionPathEditorDialogAction.Delete, "Delete", "Delete segment"),
                    Action(MotionPathEditorDialogAction.Accept, "OK", "Apply motion path", isDefault: true),
                    Action(MotionPathEditorDialogAction.Cancel, "Cancel", "Cancel motion path", isCancel: true),
                ]),
            StartRowLabel: "Start",
            SegmentRowLabel: "Segment",
            SegmentKinds: Enum.GetValues<MotionPathSegmentKind>());

    private static PresentationDialogFieldPlan<MotionPathEditorDialogField> Field(
        MotionPathEditorDialogField id,
        PresentationDialogControlKind kind,
        string label,
        string accessibleName) =>
        new(id, kind, label, accessibleName, $"FreeP.MotionPath.{id}");

    private static PresentationDialogActionPlan<MotionPathEditorDialogAction> Action(
        MotionPathEditorDialogAction id,
        string label,
        string accessibleName,
        bool isDefault = false,
        bool isCancel = false) =>
        new(id, label, accessibleName, $"FreeP.MotionPath.{id}",
            IsDefault: isDefault, IsCancel: isCancel);
}
