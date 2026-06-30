using FreeP.App.Compositor;
using System.Windows;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// WPF adapter for the shared uniform-fit slide-to-screen transform used by
/// <see cref="SlideCanvas"/> and all interaction / adorner code that needs to map
/// between slide DIP space and WPF element (screen) space.
///
/// Slide DIP space: origin (0,0) at top-left of the slide, axes in 96-DPI device-independent
/// pixels as produced by SlideCompositor (1 EMU = 1/9525 DIP).
///
/// Screen / element space: the coordinate system of SlideCanvas.ActualWidth/Height.
///
/// The mapping is: screenPt = slidePt * Scale + Offset.
/// </summary>
public sealed class SlideTransform
{
    internal SlideTransformCore Core { get; }

    /// <summary>Uniform scale factor applied to all slide DIP coordinates.</summary>
    public double Scale => Core.Scale;

    /// <summary>X offset (letterbox) in screen pixels.</summary>
    public double OffsetX => Core.OffsetX;

    /// <summary>Y offset (letterbox) in screen pixels.</summary>
    public double OffsetY => Core.OffsetY;

    /// <summary>Slide width in DIP (before scale).</summary>
    public double SlideWidthDip => Core.SlideWidthDip;

    /// <summary>Slide height in DIP (before scale).</summary>
    public double SlideHeightDip => Core.SlideHeightDip;

    public SlideTransform(double scale, double offsetX, double offsetY,
                          double slideWidthDip, double slideHeightDip)
        : this(new SlideTransformCore(scale, offsetX, offsetY, slideWidthDip, slideHeightDip))
    {
    }

    private SlideTransform(SlideTransformCore core)
    {
        Core = core ?? throw new ArgumentNullException(nameof(core));
    }

    /// <summary>Converts a point in slide DIP space to screen (element) space.</summary>
    public Point SlideToScreen(double x, double y)
    {
        var point = Core.SlideToScreen(x, y);
        return new Point(point.X, point.Y);
    }

    /// <summary>Converts a point in screen (element) space to slide DIP space.</summary>
    public Point ScreenToSlide(double x, double y)
    {
        var point = Core.ScreenToSlide(x, y);
        return new Point(point.X, point.Y);
    }

    /// <summary>Converts a scalar distance (e.g. width) from slide DIP to screen pixels.</summary>
    public double ScaleDipToScreen(double dip) => Core.ScaleDipToScreen(dip);

    /// <summary>Converts a scalar distance from screen pixels to slide DIP.</summary>
    public double ScaleScreenToDip(double px) => Core.ScaleScreenToDip(px);

    // EMU <-> screen helpers. 1 DIP = 9525 EMU.

    /// <summary>Converts a DIP value to EMU (rounds to nearest long).</summary>
    public static long DipToEmu(double dip) => SlideTransformCore.DipToEmu(dip);

    /// <summary>Converts EMU to DIP.</summary>
    public static double EmuToDip(long emu) => SlideTransformCore.EmuToDip(emu);

    /// <summary>Converts EMU to screen pixels using the current scale.</summary>
    public double EmuToScreen(long emu) => Core.EmuToScreen(emu);

    /// <summary>Converts a screen-space delta to EMU.</summary>
    public long ScreenDeltaToEmu(double screenDelta) => Core.ScreenDeltaToEmu(screenDelta);

    /// <summary>True identity transform (for when no slide is loaded).</summary>
    public static readonly SlideTransform Identity = new(SlideTransformCore.Identity);

    /// <summary>
    /// Computes the transform from the canvas's actual render size and the known slide DIP dimensions.
    /// </summary>
    public static SlideTransform Compute(double renderW, double renderH,
                                          double slideWidthDip, double slideHeightDip)
    {
        var core = SlideTransformCore.Compute(renderW, renderH, slideWidthDip, slideHeightDip);
        return ReferenceEquals(core, SlideTransformCore.Identity) ? Identity : new SlideTransform(core);
    }
}
