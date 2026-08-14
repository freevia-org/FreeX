using System.Diagnostics.CodeAnalysis;

namespace FreeX.App.Presentation.Shell;

/// <summary>
/// Renderer-neutral ownership and synchronous-scroll state for a pair of workbook windows.
/// Native hosts retain responsibility for selecting work areas, applying tile bounds, and
/// translating scroll offsets into framework controls.
/// </summary>
public sealed class WorkbookSideBySideCoordinator<TWindow>
    where TWindow : class
{
    private TWindow? _primary;
    private TWindow? _partner;
    private bool _synchronousScroll;
    private bool _applyingSynchronousScroll;

    public bool IsActive => _primary is not null && _partner is not null;

    public bool IsSynchronousScrollActive => IsActive && _synchronousScroll;

    public bool IsActiveFor(TWindow requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        return Contains(requester);
    }

    public bool IsSynchronousScrollActiveFor(TWindow requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        return Contains(requester) && _synchronousScroll;
    }

    public bool Enable(TWindow primary, TWindow partner)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(partner);

        if (ReferenceEquals(primary, partner))
            return false;

        _primary = primary;
        _partner = partner;
        _synchronousScroll = false;
        return true;
    }

    public void Disable()
    {
        _primary = null;
        _partner = null;
        _synchronousScroll = false;
    }

    public bool DisableFor(TWindow requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (!Contains(requester))
            return false;

        Disable();
        return true;
    }

    public bool Contains(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return IsActive && (ReferenceEquals(window, _primary) || ReferenceEquals(window, _partner));
    }

    public TWindow? PartnerOf(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsActive)
            return null;
        if (ReferenceEquals(window, _primary))
            return _partner;
        if (ReferenceEquals(window, _partner))
            return _primary;
        return null;
    }

    public bool TryGetPair(
        [NotNullWhen(true)] out TWindow? primary,
        [NotNullWhen(true)] out TWindow? partner)
    {
        primary = _primary;
        partner = _partner;
        return primary is not null && partner is not null;
    }

    public bool TryGetPairFor(
        TWindow requester,
        [NotNullWhen(true)] out TWindow? primary,
        [NotNullWhen(true)] out TWindow? partner)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (!Contains(requester))
        {
            primary = null;
            partner = null;
            return false;
        }

        return TryGetPair(out primary, out partner);
    }

    public bool SetSynchronousScroll(bool active)
    {
        if (active && !IsActive)
            return false;

        _synchronousScroll = active;
        return true;
    }

    public bool ToggleSynchronousScroll()
    {
        if (!IsActive)
            return false;

        _synchronousScroll = !_synchronousScroll;
        return true;
    }

    public bool ToggleSynchronousScrollFor(TWindow requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (!Contains(requester))
            return false;

        _synchronousScroll = !_synchronousScroll;
        return true;
    }

    public bool SetSynchronousScrollFor(TWindow requester, bool active)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (!Contains(requester))
            return false;

        _synchronousScroll = active;
        return true;
    }

    public bool ApplyToSynchronousPartner<TState>(
        TWindow origin,
        TState state,
        Action<TWindow, TState> apply)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(apply);

        if (!IsSynchronousScrollActive || _applyingSynchronousScroll)
            return false;

        var target = PartnerOf(origin);
        if (target is null)
            return false;

        _applyingSynchronousScroll = true;
        try
        {
            apply(target, state);
            return true;
        }
        finally
        {
            _applyingSynchronousScroll = false;
        }
    }
}
