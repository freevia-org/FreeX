namespace FreeW.App.Presentation.DocumentView;

public readonly record struct ReviewBalloonColor(byte Red, byte Green, byte Blue);

public sealed record ReviewBalloonCardStyle(
    ReviewBalloonColor Fill,
    ReviewBalloonColor Stroke);

/// <summary>
/// Renderer-neutral semantic colors for review balloons. Native brush creation remains toolkit-owned.
/// </summary>
public static class ReviewBalloonStyleCatalog
{
    public static readonly ReviewBalloonColor PaneBackground = new(0xF5, 0xF5, 0xF8);
    public static readonly ReviewBalloonColor Leader = new(0xA0, 0xA0, 0xA0);
    public static readonly ReviewBalloonColor AuthorText = new(0x17, 0x32, 0x4D);
    public static readonly ReviewBalloonColor BodyText = new(0x30, 0x30, 0x30);
    public static readonly ReviewBalloonColor MetadataText = new(0x66, 0x66, 0x66);
    public static readonly ReviewBalloonColor OpenBadge = new(0x25, 0x63, 0xEB);
    public static readonly ReviewBalloonColor ResolvedBadge = new(0x6B, 0x72, 0x80);
    public static readonly ReviewBalloonColor BadgeText = new(0xFF, 0xFF, 0xFF);

    private static readonly ReviewBalloonCardStyle Comment = new(
        new ReviewBalloonColor(0xFF, 0xF4, 0xCE),
        new ReviewBalloonColor(0xE5, 0xC3, 0x65));

    private static readonly ReviewBalloonCardStyle Insertion = new(
        new ReviewBalloonColor(0xD9, 0xF0, 0xE0),
        new ReviewBalloonColor(0x60, 0xA9, 0x70));

    private static readonly ReviewBalloonCardStyle Deletion = new(
        new ReviewBalloonColor(0xFD, 0xDE, 0xDE),
        new ReviewBalloonColor(0xC5, 0x50, 0x50));

    private static readonly ReviewBalloonCardStyle Formatting = new(
        new ReviewBalloonColor(0xE8, 0xE8, 0xF8),
        new ReviewBalloonColor(0x80, 0x80, 0xC8));

    private static readonly ReviewBalloonCardStyle Resolved = new(
        new ReviewBalloonColor(0xE5, 0xE7, 0xEB),
        new ReviewBalloonColor(0x9C, 0xA3, 0xAF));

    public static ReviewBalloonCardStyle Resolve(ReviewBalloonKind kind, bool resolved)
    {
        if (resolved)
            return Resolved;

        return kind switch
        {
            ReviewBalloonKind.Comment => Comment,
            ReviewBalloonKind.Insertion => Insertion,
            ReviewBalloonKind.Deletion => Deletion,
            ReviewBalloonKind.Formatting => Formatting,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static ReviewBalloonColor ResolveBadge(bool resolved) =>
        resolved ? ResolvedBadge : OpenBadge;
}
