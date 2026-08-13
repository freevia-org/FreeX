namespace FreeW.Core.Model;

/// <summary>
/// Semantic roles shared by the built-in paragraph styles that participate in FreeW's portable
/// document-formatting policies. <see cref="None"/> keeps gallery-only styles out of those policies.
/// </summary>
public enum BuiltInStyleRole
{
    None,
    Normal,
    Title,
    Subtitle,
    Heading,
    Quote,
}

/// <summary>
/// The catalog of built-in Word styles surfaced by the Home &gt; Styles gallery, and a helper that
/// seeds any that are missing from a document so that <see cref="Paragraph.StyleId"/> /
/// run-level style application resolves to real formatting.
///
/// <para>
/// <see cref="TextDocument.CreateEmpty"/> seeds the role catalog (Normal, Heading 1–4, Title,
/// Subtitle, Quote). This catalog also includes the broader gallery set Word shows (No Spacing,
/// Strong, Emphasis, Subtle/Intense Emphasis, Intense Quote, List Paragraph) — both paragraph and
/// character styles — and a <see cref="EnsureSeeded"/> entry point
/// that the editor calls before applying a named style so a freshly-loaded document (e.g. one read
/// from a .docx that lacks one of these) still resolves the look. Seeded styles round-trip through
/// the existing <c>DocxWriter.BuildStyles</c> path like any other catalog entry.
/// </para>
/// </summary>
public static class BuiltInStyles
{
    /// <summary>
    /// The full gallery, in Word's familiar order. Each descriptor names the style's id (the catalog
    /// key + <see cref="Paragraph.StyleId"/> value), its display name, whether it is a character or
    /// paragraph style, and a factory for its definition when it must be seeded.
    /// </summary>
    public sealed record Descriptor(
        string Id,
        string Name,
        StyleType Type,
        Func<DocumentStyle> Create,
        BuiltInStyleRole Role = BuiltInStyleRole.None,
        int? HeadingLevel = null);

    private const string Accent = "#2F5496";
    private const string AccentDark = "#1F3864";
    private const string Grey = "#404040";

    /// <summary>The gallery entries, in display order. Both paragraph and character styles.</summary>
    public static readonly IReadOnlyList<Descriptor> Gallery =
    [
        new("Normal",     "Normal",     StyleType.Paragraph,
            () => new DocumentStyle { Id = "Normal", Name = "Normal" },
            BuiltInStyleRole.Normal),

        new("NoSpacing",  "No Spacing", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "NoSpacing", Name = "No Spacing", BasedOnStyleId = "Normal",
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 0, SpaceAfterPt = 0, SpaceBeforeIsSet = true, SpaceAfterIsSet = true,
                },
            }),

        new("Heading1", "Heading 1", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "Heading1", Name = "Heading 1", BasedOnStyleId = "Normal", NextStyleId = "Normal",
                OutlineLevel = 0,
                Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = Accent },
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 12, SpaceAfterPt = 4, SpaceBeforeIsSet = true, SpaceAfterIsSet = true,
                },
            }, BuiltInStyleRole.Heading, HeadingLevel: 1),

        new("Heading2", "Heading 2", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "Heading2", Name = "Heading 2", BasedOnStyleId = "Normal", NextStyleId = "Normal",
                OutlineLevel = 1,
                Run = new RunFormatting { Bold = true, FontSizePt = 13, ColorHex = Accent },
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 10, SpaceAfterPt = 4, SpaceBeforeIsSet = true, SpaceAfterIsSet = true,
                },
            }, BuiltInStyleRole.Heading, HeadingLevel: 2),

        new("Heading3", "Heading 3", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "Heading3", Name = "Heading 3", BasedOnStyleId = "Normal", NextStyleId = "Normal",
                OutlineLevel = 2,
                Run = new RunFormatting { Bold = true, FontSizePt = 12, ColorHex = AccentDark },
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 8, SpaceAfterPt = 4, SpaceBeforeIsSet = true, SpaceAfterIsSet = true,
                },
            }, BuiltInStyleRole.Heading, HeadingLevel: 3),

        new("Heading4", "Heading 4", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "Heading4", Name = "Heading 4", BasedOnStyleId = "Normal", NextStyleId = "Normal",
                OutlineLevel = 3,
                Run = new RunFormatting { Bold = true, Italic = true, FontSizePt = 11, ColorHex = AccentDark },
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 6, SpaceAfterPt = 2, SpaceBeforeIsSet = true, SpaceAfterIsSet = true,
                },
            }, BuiltInStyleRole.Heading, HeadingLevel: 4),

        new("Title", "Title", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "Title", Name = "Title", BasedOnStyleId = "Normal",
                Run = new RunFormatting { Bold = true, FontSizePt = 28 },
                Paragraph = new ParagraphFormatting { SpaceAfterPt = 8, SpaceAfterIsSet = true },
            }, BuiltInStyleRole.Title),

        new("Subtitle", "Subtitle", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "Subtitle", Name = "Subtitle", BasedOnStyleId = "Normal",
                Run = new RunFormatting { Italic = true, FontSizePt = 15, ColorHex = "#5A5A5A" },
                Paragraph = new ParagraphFormatting { SpaceAfterPt = 8, SpaceAfterIsSet = true },
            }, BuiltInStyleRole.Subtitle),

        new("ListParagraph", "List Paragraph", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "ListParagraph", Name = "List Paragraph", BasedOnStyleId = "Normal",
                Paragraph = new ParagraphFormatting { IndentLeftPt = 36, SpaceAfterPt = 0, SpaceAfterIsSet = true },
            }),

        new("Quote", "Quote", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "Quote", Name = "Quote", BasedOnStyleId = "Normal",
                Run = new RunFormatting { Italic = true, ColorHex = Grey },
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 10, SpaceAfterPt = 10, SpaceBeforeIsSet = true, SpaceAfterIsSet = true,
                    IndentLeftPt = 36, IndentRightPt = 36,
                },
            }, BuiltInStyleRole.Quote),

        new("IntenseQuote", "Intense Quote", StyleType.Paragraph,
            () => new DocumentStyle
            {
                Id = "IntenseQuote", Name = "Intense Quote", BasedOnStyleId = "Normal",
                Run = new RunFormatting { Bold = true, Italic = true, ColorHex = Accent },
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 10, SpaceAfterPt = 10, SpaceBeforeIsSet = true, SpaceAfterIsSet = true,
                    IndentLeftPt = 43, IndentRightPt = 43,
                },
            }),

        // ── Character styles (apply as direct run formatting over the selection) ─────────────────
        new("Emphasis", "Emphasis", StyleType.Character,
            () => new DocumentStyle
            {
                Id = "Emphasis", Name = "Emphasis", Type = StyleType.Character,
                Run = new RunFormatting { Italic = true },
            }),

        new("Strong", "Strong", StyleType.Character,
            () => new DocumentStyle
            {
                Id = "Strong", Name = "Strong", Type = StyleType.Character,
                Run = new RunFormatting { Bold = true },
            }),

        new("SubtleEmphasis", "Subtle Emphasis", StyleType.Character,
            () => new DocumentStyle
            {
                Id = "SubtleEmphasis", Name = "Subtle Emphasis", Type = StyleType.Character,
                Run = new RunFormatting { Italic = true, ColorHex = "#595959" },
            }),

        new("IntenseEmphasis", "Intense Emphasis", StyleType.Character,
            () => new DocumentStyle
            {
                Id = "IntenseEmphasis", Name = "Intense Emphasis", Type = StyleType.Character,
                Run = new RunFormatting { Bold = true, Italic = true, ColorHex = Accent },
            }),
    ];

    /// <summary>
    /// The built-in paragraph styles owned by the shared document-formatting policies. Derived from
    /// <see cref="Gallery"/> so role membership and each style's seed definition cannot drift apart.
    /// </summary>
    public static readonly IReadOnlyList<Descriptor> RoleCatalog =
        Gallery.Where(descriptor => descriptor.Role != BuiltInStyleRole.None).ToArray();

    /// <summary>Look up a gallery descriptor by style id, or null when the id is not a built-in gallery style.</summary>
    public static Descriptor? Find(string styleId) =>
        Gallery.FirstOrDefault(d => string.Equals(d.Id, styleId, StringComparison.Ordinal));

    /// <summary>
    /// Find the catalog descriptor for Title (level 0) or a registered Heading level. Deeper outline
    /// levels remain valid Word outline styles but are not portable document-formatting roles.
    /// </summary>
    public static Descriptor? FindByOutlineLevel(int level) =>
        RoleCatalog.FirstOrDefault(descriptor => level == 0
            ? descriptor.Role == BuiltInStyleRole.Title
            : descriptor.Role == BuiltInStyleRole.Heading && descriptor.HeadingLevel == level);

    /// <summary>
    /// Ensure the gallery style <paramref name="styleId"/> exists in <paramref name="doc"/>'s catalog,
    /// seeding it (and its <c>Normal</c> base, if missing) from the built-in definition when absent.
    /// Returns the resolved <see cref="DocumentStyle"/>, or null when <paramref name="styleId"/> is not a
    /// known gallery style. Idempotent: an existing style of the same id is returned unchanged so a
    /// document's own customised definition is never overwritten.
    /// </summary>
    public static DocumentStyle? EnsureSeeded(TextDocument doc, string styleId)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrEmpty(styleId))
            return null;
        if (doc.Styles.TryGetValue(styleId, out var existing))
            return existing;
        if (Find(styleId) is not { } descriptor)
            return null;

        // Seed the based-on chain first (so a freshly-seeded Heading resolves Normal's run too).
        var created = descriptor.Create();
        if (created.BasedOnStyleId is { Length: > 0 } baseId && !doc.Styles.ContainsKey(baseId))
            EnsureSeeded(doc, baseId);

        doc.Styles[styleId] = created;
        return created;
    }

    /// <summary>Seed every gallery style that is missing from <paramref name="doc"/>. Idempotent.</summary>
    public static void EnsureAllSeeded(TextDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        foreach (var descriptor in Gallery)
            EnsureSeeded(doc, descriptor.Id);
    }
}
