using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Free.Shared.Ribbon;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

// Avalonia (Linux/macOS) implementation of View ▸ Window ▸ View Side by Side + Synchronous Scrolling.
//
// Design mirrors the WPF host's WorkbookWindowRegistry approach, but without that registry (which is a
// WPF-only DI singleton that depends on IWorkbookWindow/Rect/SystemParameters).  Instead we keep a
// portable WorkbookSideBySideCoordinator for pair and synchronous-scroll policy, while keeping the
// native window tiling and scroll application in this shell. _suppressScrollBroadcast is a local
// re-entrancy guard that prevents the receiving window from echoing a native scroll event back.
//
// Scroll sync is hooked into the two places that change the viewport origin in the Avalonia shell:
//   1. WorksheetScrollBar_ValueChanged (scrollbar drag / click)
//   2. SheetScrollViewer_PointerWheelChanged (mouse-wheel pan)
//
// Both paths call RefreshShell() which triggers _refreshRibbonToggleStates. We broadcast via
// BroadcastScrollOffsetToSideBySidePartner() which is called from the two scroll handlers
// after they update the session viewport.  The broadcast is suppressed when the partner is
// applying an incoming scroll position to avoid infinite feedback loops.
//
// Window positioning uses SideBySideLayoutPlanner (Free.Shared.Shell) — the same WPF-free geometry
// helper the WPF host uses — so the geometry behaviour is identical on all platforms.

public sealed partial class MainWindow : Window
{
    // ── Static side-by-side pair state ────────────────────────────────────────
    // Static so that both windows in a pair can observe each other's state.
    // (The Avalonia shell does not have a shared DI registry like the WPF host; the pairing is
    // stored as object references directly into the two MainWindow instances.)
    private static readonly WorkbookSideBySideCoordinator<MainWindow> SideBySideCoordinator = new();

    // Per-instance guard: this window is currently applying an incoming scroll offset and must
    // not echo it back to the partner.
    private bool _suppressScrollBroadcast;

    // ── State queries ─────────────────────────────────────────────────────────

    private static bool IsSideBySideActive => SideBySideCoordinator.IsActive;

    private static bool IsSynchronousScrollActive => SideBySideCoordinator.IsSynchronousScrollActive;

    private bool IsInSideBySidePair => SideBySideCoordinator.Contains(this);

    // ── View Side by Side toggle ──────────────────────────────────────────────

    // Command handler for "View Side by Side"
    private void ToggleViewSideBySide()
    {
        if (IsSideBySideActive)
        {
            // Toggle off: clear the pair (leave windows where they are, matching WPF).
            DisableSideBySide();
        }
        else
        {
            // Toggle on: find another visible window to pair with.
            var partner = FindSideBySidePartner();
            if (partner is null)
            {
                // No second window available: show the same message the WPF host shows.
                RefreshShell(UiText.Get("MainWindowMessage_SideBySideNeedsSecondWindow"));
                return;
            }

            EnableSideBySide(partner);
        }

        // Refresh the ribbon toggle state in both windows of the (former) pair.
        RefreshSideBySideRibbonState();
    }

    private void EnableSideBySide(MainWindow partner)
    {
        var (workArea, scaling) = GetPrimaryWorkAreaMetrics();
        var (primaryBounds, partnerBounds) = SideBySideLayoutPlanner.Tile(
            AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Width, scaling),
            AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Height, scaling));
        var tiles = AvaloniaWindowBoundsTranslator.Translate(
            workArea,
            scaling,
            [primaryBounds, partnerBounds]);

        SideBySideCoordinator.Enable(this, partner);

        // Restore Normal state and position both windows.
        TileThisWindowToWorkArea(tiles[0]);
        partner.TileThisWindowToWorkArea(tiles[1]);
    }

    private static void DisableSideBySide() => SideBySideCoordinator.Disable();

    // ── Synchronous Scrolling toggle ──────────────────────────────────────────

    // Command handler for "Synchronous Scrolling"
    private void ToggleSynchronousScrolling()
    {
        if (SideBySideCoordinator.ToggleSynchronousScroll())
            RefreshSideBySideRibbonState();
    }

    // ── Scroll broadcasting ───────────────────────────────────────────────────

    /// <summary>
    /// Called after every scroll operation in this window.  When sync scrolling is active and this
    /// window is one half of the current pair, pushes the current scroll position to the partner.
    /// The partner sets <see cref="_suppressScrollBroadcast"/> before applying it so it cannot loop.
    /// </summary>
    internal void BroadcastScrollOffsetToSideBySidePartner()
    {
        if (_suppressScrollBroadcast)
            return;

        var row = _verticalWorksheetScrollBar.Value;
        var col = _horizontalWorksheetScrollBar.Value;
        SideBySideCoordinator.ApplyToSynchronousPartner(
            this,
            (Row: row, Column: col),
            static (partner, offset) => partner.ApplySynchronizedScrollOffset(offset.Row, offset.Column));
    }

    private void ApplySynchronizedScrollOffset(double row, double col)
    {
        _suppressScrollBroadcast = true;
        try
        {
            // Clamp to the partner's own scroll range so differing content sizes never throw.
            var clampedRow = Math.Clamp(row, _verticalWorksheetScrollBar.Minimum, _verticalWorksheetScrollBar.Maximum);
            var clampedCol = Math.Clamp(col, _horizontalWorksheetScrollBar.Minimum, _horizontalWorksheetScrollBar.Maximum);

            // Only update if there is actually a meaningful change (avoids a no-op RefreshShell round-trip).
            var rowChanged = Math.Abs(_verticalWorksheetScrollBar.Value - clampedRow) > 0.001;
            var colChanged = Math.Abs(_horizontalWorksheetScrollBar.Value - clampedCol) > 0.001;
            if (!rowChanged && !colChanged)
                return;

            // Drive the session viewport directly, mirroring WorksheetScrollBar_ValueChanged.
            var (topRow, leftCol) = WorkbookViewportScrollPlanner.CalculateViewportOrigin(
                _session.ActiveSheet,
                clampedRow,
                clampedCol);
            if (_session.SetViewportOrigin(topRow, leftCol))
                RefreshShell("Ready");
        }
        finally
        {
            _suppressScrollBroadcast = false;
        }
    }

    // ── Ribbon toggle-state helpers ───────────────────────────────────────────

    /// <summary>
    /// Returns the ribbon state for "View Side by Side".
    /// Enabled when a pair is active OR when a second visible window exists.
    /// Checked when a pair is active.
    /// </summary>
    private RibbonCommandState GetSideBySideRibbonState()
    {
        var active = IsSideBySideActive;
        var canActivate = active || VisibleWorkbookWindowCount() > 1;
        return new RibbonCommandState(IsEnabled: canActivate, IsChecked: active);
    }

    /// <summary>
    /// Returns the ribbon state for "Synchronous Scrolling".
    /// Enabled only when Side by Side is active. Checked when sync scrolling is on.
    /// </summary>
    private RibbonCommandState GetSynchronousScrollingRibbonState() =>
        new(IsEnabled: IsSideBySideActive, IsChecked: IsSynchronousScrollActive);

    // ── Window cleanup ────────────────────────────────────────────────────────

    /// <summary>
    /// Call from OnClosed (via the existing WindowManagement OnClosed override) to ensure
    /// that if this window is part of a side-by-side pair the pair is cleared, so the partner
    /// is not left trying to broadcast to a closed window.
    /// </summary>
    private void CleanUpSideBySideOnClose()
    {
        if (IsInSideBySidePair)
        {
            var wasPartner = SideBySidePartnerOf(this);
            DisableSideBySide();
            // Notify the remaining window to refresh its ribbon state.
            wasPartner?._refreshRibbonToggleStates?.Invoke();
        }
    }

    // ── Private geometry / utility helpers ────────────────────────────────────

    /// <summary>
    /// Finds the best window to pair with for Side by Side: the first visible MainWindow
    /// that is not this window.
    /// </summary>
    private MainWindow? FindSideBySidePartner()
    {
        foreach (var w in VisibleMainWindows())
        {
            if (!ReferenceEquals(w, this))
                return w;
        }
        return null;
    }

    private static MainWindow? SideBySidePartnerOf(MainWindow window)
        => SideBySideCoordinator.PartnerOf(window);

    /// <summary>Applies translated SideBySideLayoutPlanner bounds while preserving local window policy.</summary>
    private void TileThisWindowToWorkArea(AvaloniaWindowTile tile)
    {
        WindowState = WindowState.Normal;
        Position = tile.Position;
        Width = Math.Max(MinWidth, tile.Width);
        Height = Math.Max(MinHeight, tile.Height);
    }

    private IReadOnlyList<MainWindow> VisibleMainWindows() =>
        (DesktopLifetime?.Windows ?? Array.Empty<Window>())
            .OfType<MainWindow>()
            .Where(static w => w.IsVisible)
            .ToList();

    private int VisibleWorkbookWindowCount() =>
        (int)(DesktopLifetime?.Windows.Count(static w => w.IsVisible) ?? 1);

    private void RefreshSideBySideRibbonState()
    {
        // Trigger a ribbon-toggle-state refresh in this window so the ribbon buttons update.
        _refreshRibbonToggleStates?.Invoke();
        // Also refresh the partner window's ribbon state if a pair is still active.
        SideBySidePartnerOf(this)?._refreshRibbonToggleStates?.Invoke();
    }
}
