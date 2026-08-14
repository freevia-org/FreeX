namespace Free.Shared.PageSetup;

/// <summary>Renderer-neutral page orientation shared by the sibling page-setup surfaces.</summary>
public enum SharedPageOrientation
{
    Portrait,
    Landscape,
}

/// <summary>
/// The two orientation rules the sibling page-setup dialogs use.
/// <para>
/// <b>Swap-when-landscape</b> (FreeX's paper table and FreeW's unified dialog): the stored geometry is
/// authored portrait-first and unconditionally transposed for landscape. Applying it twice for the
/// same orientation is an involution.
/// </para>
/// <para>
/// <b>Normalize-to-orientation</b> (FreeW's "NormalizeToOrientation" geometry mode): the pair is
/// re-ordered so portrait is short×long and landscape is long×short, whatever order it arrived in.
/// This one is idempotent rather than an involution.
/// </para>
/// </summary>
public static class PageOrientationRules
{
    /// <summary>Transposes a width/height pair.</summary>
    public static (double Width, double Height) Swap((double Width, double Height) size) =>
        (size.Height, size.Width);

    /// <summary>Transposes a width/height pair.</summary>
    public static (double Width, double Height) Swap(double width, double height) =>
        (height, width);

    /// <summary>The opposite orientation.</summary>
    public static SharedPageOrientation Opposite(SharedPageOrientation orientation) =>
        orientation == SharedPageOrientation.Landscape
            ? SharedPageOrientation.Portrait
            : SharedPageOrientation.Landscape;

    /// <summary>Transposes portrait-authored dimensions when <paramref name="orientation"/> is landscape.</summary>
    public static (double Width, double Height) ApplySwapWhenLandscape(
        (double Width, double Height) size,
        SharedPageOrientation orientation) =>
        orientation == SharedPageOrientation.Landscape ? Swap(size) : size;

    /// <summary>Transposes portrait-authored dimensions when <paramref name="landscape"/> is set.</summary>
    public static (double Width, double Height) ApplySwapWhenLandscape(
        double width,
        double height,
        bool landscape) =>
        landscape ? (height, width) : (width, height);

    /// <summary>
    /// Re-orders the pair so portrait reads short×long and landscape reads long×short. Idempotent.
    /// </summary>
    public static (double Width, double Height) NormalizeToOrientation(
        double width,
        double height,
        bool landscape)
    {
        if (landscape && width < height)
            return (height, width);
        if (!landscape && width > height)
            return (height, width);
        return (width, height);
    }

    /// <summary>The pair with the smaller value first — the orientation-independent portrait form.</summary>
    public static (double Short, double Long) ToPortrait(double width, double height) =>
        (Math.Min(width, height), Math.Max(width, height));
}
