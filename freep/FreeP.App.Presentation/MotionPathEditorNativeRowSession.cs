using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Owns native Motion Path row value projection and enablement.</summary>
public sealed class MotionPathEditorNativeRowSession<TKindControl, TTextControl>
    where TKindControl : class
    where TTextControl : class
{
    private readonly TKindControl _kind;
    private readonly TTextControl[] _values;
    private readonly Func<TKindControl, MotionPathSegmentKind?> _readKind;
    private readonly Action<TKindControl, IReadOnlyList<MotionPathSegmentKind>> _setKinds;
    private readonly Action<TKindControl, MotionPathSegmentKind> _setKind;
    private readonly Func<TTextControl, string?> _readText;
    private readonly Action<TTextControl, string> _setText;
    private readonly Action<object, bool> _setEnabled;
    private bool _isFirst;

    public MotionPathEditorNativeRowSession(
        TKindControl kind,
        IReadOnlyList<TTextControl> values,
        Func<TKindControl, MotionPathSegmentKind?> readKind,
        Action<TKindControl, IReadOnlyList<MotionPathSegmentKind>> setKinds,
        Action<TKindControl, MotionPathSegmentKind> setKind,
        Func<TTextControl, string?> readText,
        Action<TTextControl, string> setText,
        Action<object, bool> setEnabled)
    {
        _kind = kind ?? throw new ArgumentNullException(nameof(kind));
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != 6)
            throw new ArgumentException("A Motion Path row requires six coordinate controls.", nameof(values));
        _values = values.ToArray();
        _readKind = readKind ?? throw new ArgumentNullException(nameof(readKind));
        _setKinds = setKinds ?? throw new ArgumentNullException(nameof(setKinds));
        _setKind = setKind ?? throw new ArgumentNullException(nameof(setKind));
        _readText = readText ?? throw new ArgumentNullException(nameof(readText));
        _setText = setText ?? throw new ArgumentNullException(nameof(setText));
        _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
    }

    public MotionPathEditorRowPlan Initialize(
        MotionPathEditorDialogSurfacePlan surface,
        MotionPathSegmentEdit value,
        int rowIndex)
    {
        var plan = MotionPathEditorRowProjection.BuildPlan(surface, value, rowIndex);
        _isFirst = rowIndex == 0;
        _setKinds(_kind, surface.SegmentKinds);
        _setKind(_kind, plan.Kind);
        var values = new[] { plan.X, plan.Y, plan.X1, plan.Y1, plan.X2, plan.Y2 };
        for (var index = 0; index < _values.Length; index++)
            _setText(_values[index], values[index]);
        ApplyEnablement(plan.Enablement);
        return plan;
    }

    public MotionPathEditorRowInput CaptureInput() => new(
        _readKind(_kind),
        _readText(_values[0]),
        _readText(_values[1]),
        _readText(_values[2]),
        _readText(_values[3]),
        _readText(_values[4]),
        _readText(_values[5]));

    public void RefreshEnablement()
    {
        var kind = _readKind(_kind) ?? MotionPathSegmentKind.Line;
        ApplyEnablement(MotionPathEditorRowProjection.BuildEnablement(kind, _isFirst));
    }

    private void ApplyEnablement(MotionPathEditorRowEnablement enablement)
    {
        _setEnabled(_kind, enablement.KindEnabled);
        for (var index = 2; index < _values.Length; index++)
            _setEnabled(_values[index], enablement.ControlPointsEnabled);
        _setEnabled(_values[0], enablement.EndpointEnabled);
        _setEnabled(_values[1], enablement.EndpointEnabled);
    }
}
