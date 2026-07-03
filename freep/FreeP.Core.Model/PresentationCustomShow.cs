namespace FreeP.Core.Model;

/// <summary>
/// A named custom slide show definition stored in ppt/presentation.xml.
/// </summary>
public sealed class PresentationCustomShow
{
    /// <summary>
    /// PresentationML custom show id. The writer normalizes duplicate ids when exporting.
    /// </summary>
    public uint Id { get; set; }

    /// <summary>Display name shown by custom-show launch surfaces.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of presentation-level slide ids. Each value equals <see cref="Slide.Id"/>.
    /// </summary>
    public List<string> SlideIds { get; } = new();
}
