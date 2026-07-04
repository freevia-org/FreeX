namespace FreeP.Core.Model;

/// <summary>
/// A single comment on a slide (legacy p:cm schema — ppt/comments/commentN.xml).
///
/// Comments are positioned in EMU from the slide's top-left corner (<see cref="Xemu"/>,
/// <see cref="Yemu"/>), hold an <see cref="Author"/> display name and short
/// <see cref="Initials"/>, and a plain-text <see cref="Text"/> body.
///
/// The <see cref="Idx"/> field is the comment index within a slide (1-based; used by
/// the IO layer for round-trip identity — two comments on the same slide have different
/// indices).  <see cref="AuthorId"/> is the numeric author id from commentAuthors.xml
/// (automatically managed by the writer).
/// </summary>
public sealed class SlideComment
{
    // ── Author ────────────────────────────────────────────────────────────────────

    /// <summary>Numeric author id matching a cmAuthor entry. Assigned by the IO layer on write.</summary>
    public int AuthorId { get; set; }

    /// <summary>Author display name (p:cmAuthor name=).</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Author initials (p:cmAuthor initials=). Usually 1-3 characters.</summary>
    public string Initials { get; set; } = string.Empty;

    // ── Content ───────────────────────────────────────────────────────────────────

    /// <summary>Plain-text comment body (p:text inside p:cm).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Creation or modification timestamp. Null if not specified in the source.</summary>
    public DateTime? DateTime { get; set; }

    /// <summary>True when the thread has been resolved in the review workflow.</summary>
    public bool IsResolved { get; set; }

    /// <summary>Timestamp for the latest resolve action. Null when the thread is open.</summary>
    public DateTime? ResolvedDateTime { get; set; }

    /// <summary>Reviewer display name for the latest resolve action. Empty when not supplied.</summary>
    public string ResolvedBy { get; set; } = string.Empty;

    /// <summary>True when this comment came from or should be saved as a modern PowerPoint comment part.</summary>
    public bool UsesModernCommentSchema { get; set; }

    /// <summary>Modern PowerPoint comment id from the p188:cm id attribute.</summary>
    public string ModernCommentId { get; set; } = string.Empty;

    /// <summary>Modern PowerPoint author id from the p188:cm authorId attribute.</summary>
    public string ModernAuthorId { get; set; } = string.Empty;

    /// <summary>Modern PowerPoint author user id from the p188:author userId attribute.</summary>
    public string ModernAuthorUserId { get; set; } = string.Empty;

    /// <summary>Modern PowerPoint author provider id from the p188:author providerId attribute.</summary>
    public string ModernAuthorProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Local element name for the modern PowerPoint comment anchor, such as
    /// <c>unknownAnchor</c>. Empty for legacy comments.
    /// </summary>
    public string ModernAnchorKind { get; set; } = string.Empty;

    /// <summary>Raw modern PowerPoint comment anchor XML for lossless package round-trip.</summary>
    public string ModernAnchorXml { get; set; } = string.Empty;

    /// <summary>Modern-comment style replies attached to this thread.</summary>
    public List<SlideCommentReply> Replies { get; } = new();

    // ── Position ──────────────────────────────────────────────────────────────────

    /// <summary>Horizontal position from the slide left edge, in EMU (p:pos x=).</summary>
    public long Xemu { get; set; }

    /// <summary>Vertical position from the slide top edge, in EMU (p:pos y=).</summary>
    public long Yemu { get; set; }

    // ── IO round-trip identity ────────────────────────────────────────────────────

    /// <summary>
    /// 1-based comment index within the slide (p:cm idx=).
    /// Multiple comments on one slide have distinct indices.
    /// Set by the reader; recalculated from list position on write.
    /// </summary>
    public int Idx { get; set; }
}

public sealed class SlideCommentReply
{
    public int AuthorId { get; set; }

    public string ModernReplyId { get; set; } = string.Empty;

    public string ModernAuthorId { get; set; } = string.Empty;

    public string ModernAuthorUserId { get; set; } = string.Empty;

    public string ModernAuthorProviderId { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Initials { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTime? DateTime { get; set; }
}
