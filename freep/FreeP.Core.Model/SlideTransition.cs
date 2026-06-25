namespace FreeP.Core.Model;

/// <summary>
/// Specifies the visual transition played when this slide enters during a slideshow.
/// Maps to the <c>p:transition</c> element in PresentationML.
/// </summary>
public sealed class SlideTransition
{
    /// <summary>The transition effect kind.</summary>
    public TransitionKind Kind { get; set; } = TransitionKind.None;

    /// <summary>
    /// Direction modifier used by directional transitions (Push, Wipe, Cover, etc.).
    /// Null for non-directional effects (Fade, Cut, Dissolve, …).
    /// </summary>
    public TransitionDirection? Direction { get; set; }

    /// <summary>
    /// Duration of the transition animation in milliseconds.
    /// Corresponds to <c>spd</c> (slow≈1500/med≈750/fast≈500) or a <c>dur</c> attribute in newer schemas.
    /// Default 500 ms maps to <c>fast</c>.
    /// </summary>
    public int DurationMs { get; set; } = 500;

    /// <summary>
    /// Whether a mouse click advances to the next slide.
    /// Corresponds to absence/presence of <c>advClick="0"</c> on p:transition (default is true/click advances).
    /// </summary>
    public bool AdvanceOnClick { get; set; } = true;

    /// <summary>
    /// If non-null, the slide automatically advances after this many milliseconds.
    /// Corresponds to <c>advTm</c> on p:transition.
    /// </summary>
    public int? AdvanceAfterMs { get; set; }
}

/// <summary>Identifies the transition effect element name in PresentationML.</summary>
public enum TransitionKind
{
    None,
    Fade,
    Cut,
    Push,
    Wipe,
    Cover,
    Uncover,
    Split,
    Blinds,
    Dissolve,
    Zoom,
    Wheel,
    RandomBar,
    Strips,
    Fly,
    Random,
}

/// <summary>
/// Direction modifier for transitions that accept a directional argument.
/// Maps to attributes like <c>dir</c> on child elements such as <c>p:push</c>, <c>p:wipe</c>, etc.
/// </summary>
public enum TransitionDirection
{
    Left,
    Right,
    Up,
    Down,
    LeftUp,
    LeftDown,
    RightUp,
    RightDown,
    Horizontal,
    Vertical,
    In,
    Out,
}
