using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA tests proving that <see cref="DocumentView"/>'s adorner overlays (formatting marks, page
/// gridlines, etc.) unsubscribe from <c>LayoutUpdated</c> when they are removed from the
/// <see cref="AdornerLayer"/>. Each overlay is a small nested <c>Adorner</c> that repaints on
/// <c>LayoutUpdated</c> so it tracks scrolling/relayout; before the fix that subscription was made
/// with an anonymous lambda that was never unhooked, so every toggle-off left the old instance
/// referenced forever by the view's event delegate list (a leak for the life of the window, and
/// wasted layout work on every pass since the leaked instance keeps invalidating itself).
///
/// The most direct, user-reachable way to exercise this is the real toggle commands
/// (<see cref="DocumentView.ToggleFormattingMarks"/>, used by the View ribbon's "Show ¶" button, and
/// <see cref="DocumentView.TogglePageGridlines"/>, used by the View ribbon's Gridlines toggle) rather
/// than calling the private Sync*/adorner constructors directly. The <see cref="AdornerDecorator"/> +
/// explicit Measure/Arrange/UpdateLayout host mirrors the pattern already used by
/// <c>ColumnLayoutTests.LineBetween_AddsANonInteractivePixelAlignedAdornerInPrintLayout</c> to get a
/// real <see cref="AdornerLayer"/> without opening an actual window.
/// </summary>
public sealed class DocumentViewAdornerLeakTests
{
    private static (DocumentView View, AdornerDecorator Host) NewHostedView()
    {
        var view = new DocumentView();
        var host = new AdornerDecorator { Child = view };
        host.Measure(new Size(816, 1056));
        host.Arrange(new Rect(0, 0, 816, 1056));
        host.UpdateLayout();
        return (view, host);
    }

    // Pump the dispatcher queue up to ContextIdle priority. WPF's own layout/adorner machinery
    // schedules routine housekeeping (e.g. arrange invalidation) via Dispatcher.BeginInvoke, and those
    // queued operations hold their captured arguments alive until the dispatcher gets a chance to run
    // them — exactly as it would between two user actions in a real, message-pumping application. An
    // STA xunit test never runs that pump on its own, so without this drain a GC check would see
    // objects as "still referenced" by WPF's own transient queued work rather than by anything our fix
    // is responsible for.
    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    // Toggle the formatting-marks overlay on then off once, using the same public entry point the
    // View ribbon's "Show ¶" command uses, and hand back a WeakReference to the adorner instance that
    // was created for that cycle. Kept in its own (non-inlined) method so the local strong reference to
    // the adorner does not linger as a stack-frame GC root past the call — otherwise a Debug build could
    // keep it "alive" for reasons unrelated to the subscription leak under test.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ToggleFormattingMarksOnAndOffCaptureAdorner(DocumentView view)
    {
        view.ToggleFormattingMarks().Should().BeTrue("this call turns the overlay on");
        var field = typeof(DocumentView).GetField("_formattingMarksAdorner", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("DocumentView must still keep its formatting-marks adorner field");
        var adorner = field!.GetValue(view);
        adorner.Should().NotBeNull("toggling on must create the overlay while the view is in a visual tree");

        view.ToggleFormattingMarks().Should().BeFalse("this call turns the overlay back off");

        return new WeakReference(adorner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TogglePageGridlinesOnAndOffCaptureAdorner(DocumentView view)
    {
        view.TogglePageGridlines().Should().BeTrue("this call turns gridlines on");
        var field = typeof(DocumentView).GetField("_pageGridlinesAdorner", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("DocumentView must still keep its page-gridlines adorner field");
        var adorner = field!.GetValue(view);
        adorner.Should().NotBeNull("toggling on must create the overlay while the view is in a visual tree");

        view.TogglePageGridlines().Should().BeFalse("this call turns gridlines back off");

        return new WeakReference(adorner);
    }

    /// <summary>
    /// Toggling the formatting-marks overlay on/off repeatedly (the "Show ¶" ribbon gesture) must not
    /// leak one adorner instance per toggle. Before the fix, <c>FormattingMarksAdorner</c> subscribed to
    /// <c>LayoutUpdated</c> with an anonymous lambda in its constructor and <c>SyncFormattingMarksAdorner</c>
    /// only called <c>layer.Remove(...)</c> on toggle-off — never unsubscribing — so the view's own
    /// <c>LayoutUpdated</c> delegate list kept every removed instance alive forever.
    /// </summary>
    [StaFact]
    public void ToggleFormattingMarks_RepeatedCycles_DoNotLeakAdornerInstances()
    {
        var (view, _) = NewHostedView();

        var weakRefs = new List<WeakReference>();
        for (var i = 0; i < 5; i++)
            weakRefs.Add(ToggleFormattingMarksOnAndOffCaptureAdorner(view));

        DrainDispatcher();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        weakRefs.Count(reference => reference.IsAlive).Should().Be(0,
            "every toggle-off must release its adorner once removed from the AdornerLayer; " +
            "a still-subscribed LayoutUpdated handler would keep it referenced by the view forever");
    }

    /// <summary>
    /// Sibling no-regression coverage for a different adorner in the same file (<c>PageGridlinesAdorner</c>,
    /// driven by the View ribbon's Gridlines toggle) so the fix is not special-cased to just the
    /// formatting-marks overlay.
    /// </summary>
    [StaFact]
    public void TogglePageGridlines_RepeatedCycles_DoNotLeakAdornerInstances()
    {
        var (view, _) = NewHostedView();

        var weakRefs = new List<WeakReference>();
        for (var i = 0; i < 5; i++)
            weakRefs.Add(TogglePageGridlinesOnAndOffCaptureAdorner(view));

        DrainDispatcher();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        weakRefs.Count(reference => reference.IsAlive).Should().Be(0,
            "every toggle-off must release its adorner once removed from the AdornerLayer; " +
            "a still-subscribed LayoutUpdated handler would keep it referenced by the view forever");
    }

    /// <summary>
    /// No-regression: the overlay must still actually work while it is showing — toggling on adds
    /// exactly one adorner to the layer and toggling off removes it — so the leak fix (unsubscribing on
    /// removal) did not also break normal add/remove/repaint behaviour.
    /// </summary>
    [StaFact]
    public void ToggleFormattingMarks_StillAddsAndRemovesFromAdornerLayer()
    {
        var (view, _) = NewHostedView();

        view.ToggleFormattingMarks().Should().BeTrue();
        var layer = AdornerLayer.GetAdornerLayer(view);
        layer.Should().NotBeNull();
        (layer!.GetAdorners(view) ?? []).Should().HaveCount(1,
            "turning the overlay on must still attach exactly one adorner to the layer");

        view.ToggleFormattingMarks().Should().BeFalse();
        (layer.GetAdorners(view) ?? []).Should().BeEmpty(
            "turning the overlay back off must still detach it from the layer");
    }
}
