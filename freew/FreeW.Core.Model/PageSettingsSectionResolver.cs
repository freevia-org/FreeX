namespace FreeW.Core.Model;

/// <summary>
/// Resolves the <see cref="PageSettings"/> instance that a page-setup mutation (Layout ribbon,
/// Page Setup dialog, page border/color/watermark, ...) should read from and write to for a given
/// section index, shared by every page-settings <see cref="IDocumentCommand"/> so they all agree on
/// which section's settings a caret-scoped edit targets.
///
/// <para>
/// A negative <paramref name="sectionIndex"/> preserves the historical (pre section-awareness)
/// behavior of always targeting <see cref="TextDocument.Page"/> (the final/body-level section) — this
/// keeps existing callers that do not yet resolve a caret section (and existing tests) unchanged.
/// A non-negative index is clamped into range and resolved via <see cref="TextDocument.Sections"/>,
/// whose <see cref="Section.Page"/> is the real, persisted <see cref="PageSettings"/> instance for
/// that section (either <see cref="TextDocument.Page"/> for the final section, or the owning
/// paragraph's <see cref="Paragraph.SectionBreak"/> page for an earlier one) — never a copy.
/// </para>
/// </summary>
public static class PageSettingsSectionResolver
{
    public static PageSettings Resolve(TextDocument document, int sectionIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (sectionIndex < 0)
            return document.Page;

        var sections = document.Sections;
        var clamped = Math.Clamp(sectionIndex, 0, sections.Count - 1);
        return sections[clamped].Page;
    }
}
