namespace FreeP.App.Compositor;

/// <summary>
/// Framework-free encapsulation of the uniform-fit slide→screen transform used by both
/// the WPF and Avalonia renderers.
///
/// Slide DIP space: origin (0,0) at top-left of the slide, axes in 96-DPI device-independent
/// pixels (1 EMU = 1/9525 DIP).
///
/// Screen / element space: the coordinate system of the rendered control.
///
/// The mapping is: screenPt = slidePt * Scale + Offset
///
/// Unlike <c>FreeP.App.Rendering.Wpf.SlideTransform</c> (which returns <c>System.Windows.Point</c>),
/// this version returns plain value tuples so it can be used from any framework.
/// </summary>
public sealed class SlideTransformCore
{
    /// <summary>Uniform scale factor applied to all slide DIP coordinates.</summary>
    public double Scale { get; }

    /// <summary>X offset (letterbox) in screen pixels.</summary>
    public double OffsetX { get; }

    /// <summary>Y offset (letterbox) in screen pixels.</summary>
    public double OffsetY { get; }

    /// <summary>Slide width in DIP (before scale).</summary>
    public double SlideWidthDip { get; }

    /// <summary>Slide height in DIP (before scale).</summary>
    public double SlideHeightDip { get; }

    public SlideTransformCore(double scale, double offsetX, double offsetY,
                              double slideWidthDip, double slideHeightDip)
    {
        Scale          = scale;
        OffsetX        = offsetX;
        OffsetY        = offsetY;
        SlideWidthDip  = slideWidthDip;
        SlideHeightDip = slideHeightDip;
    }

    /// <summary>Converts a point in slide DIP space to screen (element) space.</summary>
    public (double X, double Y) SlideToScreen(double x, double y)
        => (x * Scale + OffsetX, y * Scale + OffsetY);

    /// <summary>Converts a point in screen (element) space to slide DIP space.</summary>
    public (double X, double Y) ScreenToSlide(double x, double y)
        => Scale == 0 ? (0, 0) : ((x - OffsetX) / Scale, (y - OffsetY) / Scale);

    /// <summary>Converts a scalar distance (e.g. width) from slide DIP to screen pixels.</summary>
    public double ScaleDipToScreen(double dip) => dip * Scale;

    /// <summary>Converts a scalar distance from screen pixels to slide DIP.</summary>
    public double ScaleScreenToDip(double px) => Scale == 0 ? 0 : px / Scale;

    // ── EMU ↔ screen helpers ───────────────────────────────────────────────────────────────────
    // 1 DIP = 9525 EMU

    private const double EmuPerDip = 9525.0;

    /// <summary>Converts a DIP value to EMU (rounds to nearest long).</summary>
    public static long DipToEmu(double dip) => (long)Math.Round(dip * EmuPerDip);

    /// <summary>Converts EMU to DIP.</summary>
    public static double EmuToDip(long emu) => emu / EmuPerDip;

    /// <summary>Converts EMU to screen pixels using the current scale.</summary>
    public double EmuToScreen(long emu) => EmuToDip(emu) * Scale;

    /// <summary>Converts a screen-space delta to EMU.</summary>
    public long ScreenDeltaToEmu(double screenDelta)
        => Scale == 0 ? 0L : DipToEmu(screenDelta / Scale);

    /// <summary>True identity transform (for when no slide is loaded).</summary>
    public static readonly SlideTransformCore Identity = new(1, 0, 0, 0, 0);

    /// <summary>
    /// Computes the transform from the control's actual render size and the known slide DIP dimensions.
    /// </summary>
    public static SlideTransformCore Compute(double renderW, double renderH,
                                             double slideWidthDip, double slideHeightDip)
    {
        if (renderW <= 0 || renderH <= 0 || slideWidthDip <= 0 || slideHeightDip <= 0)
            return Identity;

        double scale   = Math.Min(renderW / slideWidthDip, renderH / slideHeightDip);
        double offsetX = (renderW - slideWidthDip  * scale) / 2;
        double offsetY = (renderH - slideHeightDip * scale) / 2;
        return new SlideTransformCore(scale, offsetX, offsetY, slideWidthDip, slideHeightDip);
    }
}
