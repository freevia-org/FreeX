using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Focused regression test for FreeX cleanup batch MED10 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED10Tests
{
    /// <summary>
    /// P45: after a FreeX-internal copy, the OS clipboard being overwritten by an image from
    /// another application (so a subsequent text read comes back null, not merely empty) must
    /// invalidate the internal clipboard snapshot rather than being treated as "unchanged". Before
    /// the fix, ShouldPreferExternalClipboardImage's `text is not null &amp;&amp;` guard made a null
    /// read unreachable, so a stale internal copy would still win over a freshly copied image.
    /// </summary>
    [Fact]
    public void ShouldPreferExternalClipboardImage_PrefersImage_WhenOsClipboardTextReadIsNull()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        var sheet = session.Workbook.Sheets.Single();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a1"));
        session.SelectRange(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));

        // Copy A1 in FreeX: captures an internal clipboard snapshot.
        session.CopySelectedRangeText();

        // Simulate another app then putting an image on the OS clipboard: a text read comes back
        // null (not empty string), because the clipboard no longer holds a text format at all.
        session.ShouldPreferExternalClipboardImage(null).Should().BeTrue(
            "a null OS-clipboard text read signals the clipboard now holds non-text content (e.g. " +
            "an image copied in another app), which must not be mistaken for \"clipboard unchanged\"");

        // Sanity check the non-buggy branches still behave: unrelated non-empty text is external,
        // and re-reading the exact same internally-copied text keeps preferring the internal paste.
        session.ShouldPreferExternalClipboardImage("some other text").Should().BeFalse();
    }
}
