namespace FreeP.Core.Model;

/// <summary>
/// Placeholder types from PresentationML <c>p:ph type="..."</c>.
/// </summary>
public enum PlaceholderType
{
    Body = 0,
    Title = 1,
    CenteredTitle = 2,
    SubTitle = 3,
    DateTime = 4,
    Footer = 5,
    SlideNumber = 6,
    Header = 7,
    Object = 8,
    Chart = 9,
    Table = 10,
    ClipArt = 11,
    Diagram = 12,
    Media = 13,
    Picture = 14
}

/// <summary>
/// Identifies a shape as a placeholder, enabling position/size/style inheritance from the
/// matching placeholder on the slide layout, then the slide master.
/// </summary>
public sealed class Placeholder
{
    /// <summary>Placeholder type. Default is Body for un-typed placeholders.</summary>
    public PlaceholderType Type { get; set; } = PlaceholderType.Body;

    /// <summary>
    /// Placeholder index (p:ph idx). Used to correlate layout/master placeholder with slide placeholder.
    /// 0 = primary (title), 1 = body, etc.
    /// </summary>
    public int Idx { get; set; }
}
