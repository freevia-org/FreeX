using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Owns the transient SmartArt Design gallery transaction. The first hover freezes one diagram target
/// and captures every gallery-controlled field. Switching previews restores that complete baseline first;
/// cancel stays outside history, and commit delegates exactly one edit to the shared object coordinator.
/// </summary>
public sealed class DocumentSmartArtDesignPreviewSession
{
    private readonly DocumentEditingSession _session;
    private DocumentObjectTarget? _target;
    private SmartArtDesignBaseline? _baseline;

    internal DocumentSmartArtDesignPreviewSession(DocumentEditingSession session) => _session = session;

    public bool HasActivePreview => _baseline is not null;

    public DocumentObjectTarget? ActiveTarget => _target;

    public bool PreviewLayout(DocumentObjectTarget target, SmartArtLayoutPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return Preview(target, smartArt =>
        {
            smartArt.Kind = preset.Kind;
            smartArt.LayoutId = preset.Id;
        });
    }

    public bool PreviewColorScheme(DocumentObjectTarget target, SmartArtColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        return Preview(target, smartArt => smartArt.ColorSchemeId = scheme.Id);
    }

    public bool PreviewStyle(DocumentObjectTarget target, SmartArtStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return Preview(target, smartArt => smartArt.StyleId = style.Id);
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

    public DocumentObjectEditResult CommitLayout(
        DocumentObjectTarget currentTarget,
        SmartArtLayoutPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var target = PrepareCommit(currentTarget);
        return _session.Objects.SetSmartArtLayout(target, preset.Kind, preset.Id);
    }

    public DocumentObjectEditResult CommitColorScheme(
        DocumentObjectTarget currentTarget,
        SmartArtColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var target = PrepareCommit(currentTarget);
        return _session.Objects.SetSmartArtColor(target, scheme.Id);
    }

    public DocumentObjectEditResult CommitStyle(
        DocumentObjectTarget currentTarget,
        SmartArtStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var target = PrepareCommit(currentTarget);
        return _session.Objects.SetSmartArtStyle(target, style.Id);
    }

    private bool Preview(DocumentObjectTarget target, Action<SmartArt> apply)
    {
        if (_baseline is null)
        {
            if (_session.Objects.ResolveSmartArt(target) is not { } initialSmartArt)
                return false;

            _target = target;
            _baseline = new SmartArtDesignBaseline(
                initialSmartArt.Kind,
                initialSmartArt.LayoutId,
                initialSmartArt.ColorSchemeId,
                initialSmartArt.StyleId);
        }
        else
        {
            RestoreBaseline();
        }

        if (_target is not { } captured || _session.Objects.ResolveSmartArt(captured) is not { } smartArt)
        {
            Clear();
            return false;
        }

        apply(smartArt);
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
            || _session.Objects.ResolveSmartArt(target) is not { } smartArt)
        {
            return;
        }

        smartArt.Kind = baseline.Kind;
        smartArt.LayoutId = baseline.LayoutId;
        smartArt.ColorSchemeId = baseline.ColorSchemeId;
        smartArt.StyleId = baseline.StyleId;
    }

    private void Clear()
    {
        _target = null;
        _baseline = null;
    }

    private sealed record SmartArtDesignBaseline(
        SmartArtKind Kind,
        string? LayoutId,
        string? ColorSchemeId,
        string? StyleId);
}
