namespace FreeP.Core.Model;

/// <summary>
/// FreeP's minimal presentation model: an ordered list of <see cref="Slide"/> plus document
/// <see cref="Properties"/>. Deliberately tiny — this is scaffold, just enough to round-trip through the
/// stub <c>.fxp</c> reader/writer and to prove the shared app tier hosts a second sister app. The real
/// presentation domain (slide rendering, shape geometry, transitions, .pptx import/export) is intentionally
/// out of scope and lands in a follow-up session.
/// </summary>
public sealed class Presentation
{
    /// <summary>The slides, in presentation order.</summary>
    public List<Slide> Slides { get; } = new();

    /// <summary>Core document properties (title/author/subject/...), mirroring FreeW's DocumentProperties.</summary>
    public PresentationProperties Properties { get; } = new();

    /// <summary>An empty presentation seeded with a single blank title slide (the only "template" FreeP ships).</summary>
    public static Presentation CreateEmpty()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Title = "Slide 1" });
        return presentation;
    }
}
