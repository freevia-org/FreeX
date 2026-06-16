namespace FreeX.App.Presentation.Text;

/// <summary>
/// Portable measurement result: the width and height a run of text occupies, in device-independent
/// units. Carries no platform types so it can flow between the portable layout engine and any of
/// the desktop hosts' text stacks.
/// </summary>
public readonly record struct TextSize(double Width, double Height)
{
    /// <summary>A zero-extent size, used for empty/blank text.</summary>
    public static readonly TextSize Empty = new(0, 0);
}
