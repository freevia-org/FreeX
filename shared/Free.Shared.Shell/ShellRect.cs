namespace Free.Shared.Shell;

/// <summary>
/// A neutral, platform-agnostic rectangle (work-area-relative window bounds) returned by the
/// portable shell layout planners. Deliberately WPF-free so the planners can live in the portable
/// <c>Free.Shared.Shell</c> tier and be reused from non-WPF hosts (Avalonia/Linux/macOS).
/// WPF hosts translate this to <c>System.Windows.Rect</c> at the platform boundary
/// (<c>new Rect(r.X, r.Y, r.Width, r.Height)</c>); <see cref="Left"/>/<see cref="Top"/> mirror
/// <c>Rect</c>'s member names so existing call sites read unchanged.
/// </summary>
public readonly record struct ShellRect(double X, double Y, double Width, double Height)
{
    /// <summary>The x-coordinate of the left edge (alias of <see cref="X"/>, mirrors <c>Rect.Left</c>).</summary>
    public double Left => X;

    /// <summary>The y-coordinate of the top edge (alias of <see cref="Y"/>, mirrors <c>Rect.Top</c>).</summary>
    public double Top => Y;
}
