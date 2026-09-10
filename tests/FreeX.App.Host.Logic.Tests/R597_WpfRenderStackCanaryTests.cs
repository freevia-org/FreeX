using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Host.Tests;

/// <summary>
/// r597: a canary for the render stack itself, so that a dead one reports ONCE and clearly instead of
/// as several dozen unrelated-looking pixel assertions.
///
/// <para>r596's lane came back with 42 failures across <c>FreeX.App.Host.Logic.Tests</c> (31) and
/// <c>FreeP.App.Host.Tests</c> (11) — "the blue fill glyphs must be visible" against a null match,
/// <c>CountPixelDifferences</c> returning 0, <c>CountNearBlackPixels</c> returning 0 for plain
/// undecorated text. Every one is a symptom of the same single fact, and none of them says it: WPF's
/// <see cref="RenderTargetBitmap"/> is producing an entirely transparent surface on this machine, so
/// nothing any renderer draws survives to the bitmap.</para>
///
/// <para>Finding that took a long detour through hypotheses this test would have settled in seconds:
/// a stale binary (rebuilt — still failed), a source regression (nothing rendering-related had
/// changed), the test added to that assembly in the previous round (removed — still failed), a
/// missing Calibri (installed, and <c>FormattedText</c> measures it correctly, because MEASUREMENT
/// does not need the render pipeline), and the text brush resolving to something invisible (it
/// resolves to <c>Brushes.Black</c>).</para>
///
/// <para>The detour also produced a false positive worth recording here, because the same mistake is
/// easy to repeat: the first version of this check counted "non-white" pixels WITHOUT testing alpha.
/// A fully transparent Pbgra32 surface reads as (0,0,0,0), so every pixel counted as ink and the
/// probe reported a healthy 20,000 — which is exactly 200x100, the whole bitmap. A render check must
/// require <c>alpha &gt; 0</c>; without it, "everything drew" and "nothing drew" are the same
/// number.</para>
/// </summary>
public sealed class R597_WpfRenderStackCanaryTests
{
    [Fact]
    public void RenderTargetBitmap_ActuallyProducesInk()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(Brushes.Black, null, new Rect(10, 10, 50, 30));

        var bitmap = new RenderTargetBitmap(200, 100, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var opaque = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 0)
                opaque++;
        }

        // A 50x30 rectangle is 1500 pixels. Zero means the render stack is not drawing at all, and
        // every pixel-comparison test in this assembly and in FreeP.App.Host.Tests will fail for that
        // reason and no other.
        opaque.Should().BeGreaterThan(1000,
            "WPF's RenderTargetBitmap produced a transparent surface for a solid black rectangle, so " +
            "the render stack itself is not drawing — every pixel assertion in this assembly and in " +
            "FreeP.App.Host.Tests fails for that reason and for no reason in the code under test");
    }
}
