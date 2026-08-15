using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Owns the transient Chart Design gallery transaction. The first hover freezes one chart target and
/// captures all three gallery-controlled values. Switching previews always restores that baseline first;
/// cancel never enters history, and commit delegates exactly one edit to the object coordinator.
/// </summary>
public sealed class DocumentChartDesignPreviewSession
{
    private readonly DocumentEditingSession _session;
    private DocumentObjectTarget? _target;
    private ChartDesignBaseline? _baseline;

    internal DocumentChartDesignPreviewSession(DocumentEditingSession session) => _session = session;

    public bool HasActivePreview => _baseline is not null;

    public DocumentObjectTarget? ActiveTarget => _target;

    public bool PreviewStyle(DocumentObjectTarget target, ChartStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return Preview(target, chart => chart.StyleId = style.Id);
    }

    public bool PreviewColorScheme(DocumentObjectTarget target, ChartColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        return Preview(target, chart => chart.ColorSchemeId = scheme.Id);
    }

    public bool PreviewQuickLayout(DocumentObjectTarget target, ChartQuickLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Preview(target, chart => chart.QuickLayoutId = layout.Id);
    }

    public DocumentObjectTarget? Cancel()
    {
        if (_baseline is null)
            return null;

        var target = _target;
        RestoreBaseline();
        Clear();
        return target;
    }

    public DocumentObjectEditResult CommitStyle(DocumentObjectTarget currentTarget, ChartStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var target = PrepareCommit(currentTarget);
        return _session.Objects.SetChartStyle(target, style.Id);
    }

    public DocumentObjectEditResult CommitColorScheme(
        DocumentObjectTarget currentTarget,
        ChartColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var target = PrepareCommit(currentTarget);
        return _session.Objects.SetChartColorScheme(target, scheme.Id);
    }

    public DocumentObjectEditResult CommitQuickLayout(
        DocumentObjectTarget currentTarget,
        ChartQuickLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var target = PrepareCommit(currentTarget);
        return _session.Objects.SetChartQuickLayout(target, layout);
    }

    private bool Preview(DocumentObjectTarget target, Action<Chart> apply)
    {
        if (_baseline is null)
        {
            if (_session.Objects.ResolveChart(target) is not { } initialChart)
                return false;

            _target = target;
            _baseline = new ChartDesignBaseline(
                initialChart.StyleId,
                initialChart.ColorSchemeId,
                initialChart.QuickLayoutId);
        }
        else
        {
            RestoreBaseline();
        }

        if (_target is not { } captured || _session.Objects.ResolveChart(captured) is not { } chart)
        {
            Clear();
            return false;
        }

        apply(chart);
        return true;
    }

    private DocumentObjectTarget PrepareCommit(DocumentObjectTarget currentTarget)
    {
        var target = _target ?? currentTarget;
        if (_baseline is not null)
            RestoreBaseline();
        Clear();
        return target;
    }

    private void RestoreBaseline()
    {
        if (_target is not { } target
            || _baseline is not { } baseline
            || _session.Objects.ResolveChart(target) is not { } chart)
        {
            return;
        }

        chart.StyleId = baseline.StyleId;
        chart.ColorSchemeId = baseline.ColorSchemeId;
        chart.QuickLayoutId = baseline.QuickLayoutId;
    }

    private void Clear()
    {
        _target = null;
        _baseline = null;
    }

    private sealed record ChartDesignBaseline(
        int StyleId,
        string? ColorSchemeId,
        int QuickLayoutId);
}
