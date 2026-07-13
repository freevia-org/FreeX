using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCommentReader
{
    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";

    // Real Excel 365 writes a legacy comments1.xml/VML "note" shim for every threaded comment so
    // pre-2018 readers still see something. That shim always uses the literal author "tc={GUID}"
    // (never a real display name) and a fixed compatibility banner as its text ("[Threaded
    // comment]\n\nYour version of Excel allows you to read this threaded comment; however, any
    // edits made to it will get removed if the file is opened in a newer version of Excel...").
    // Excel itself never surfaces this shim to the user -- only the threaded conversation is
    // shown -- so it must never enter Sheet.Comments/CommentAuthors; otherwise every
    // Excel-authored threaded comment also shows up as a bogus duplicate "Note" (Mixed indicator,
    // garbage Show-Notes row, garbage line in printed comment summaries, etc). Filter it out here,
    // at the source, so every downstream consumer (indicators, note navigation, print) is correct
    // without needing to know about threaded comments at all.
    private const string LegacyThreadedCommentAuthorPrefix = "tc=";
    private const string LegacyThreadedCommentBannerPrefix = "[Threaded comment]";

    public static IReadOnlyList<(uint Row, uint Col, string Text, string Author)> Read(ZipArchive archive, string worksheetPath)
    {
        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is null)
            return [];

        var commentPartPaths = ReadCommentPartPaths(archive, relationshipsEntry, worksheetPath);
        if (commentPartPaths.Count == 0)
            return [];

        var comments = new List<(uint Row, uint Col, string Text, string Author)>();
        foreach (var commentPartPath in commentPartPaths)
        {
            var commentEntry = archive.GetEntry(commentPartPath);
            if (commentEntry is null)
                continue;

            ReadComments(commentEntry, comments);
        }

        return comments;
    }

    private static IReadOnlyList<string> ReadCommentPartPaths(
        ZipArchive archive,
        ZipArchiveEntry relationshipsEntry,
        string worksheetPath)
    {
        try
        {
            var relationshipsXml = LoadXml(relationshipsEntry);
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            return relationshipsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Where(element =>
                    string.Equals(element.Attribute("Type")?.Value, CommentsRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(element.Attribute("Target")?.Value))
                .Select(element => XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, element.Attribute("Target")!.Value))
                .Where(path => archive.GetEntry(path) is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void ReadComments(ZipArchiveEntry commentEntry, List<(uint Row, uint Col, string Text, string Author)> comments)
    {
        try
        {
            var commentsXml = LoadXml(commentEntry);
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            // Build an index from authorId (0-based) to author name from the <authors> list.
            var authors = commentsXml.Root?
                .Element(worksheetNs + "authors")?
                .Elements(worksheetNs + "author")
                .Select(element => element.Value)
                .ToList()
                ?? [];

            foreach (var comment in commentsXml.Root?
                         .Element(worksheetNs + "commentList")?
                         .Elements(worksheetNs + "comment") ?? [])
            {
                var reference = comment.Attribute("ref")?.Value;
                if (string.IsNullOrWhiteSpace(reference) ||
                    !CellAddress.TryParse(reference, SheetId.New(), out var address))
                {
                    continue;
                }

                // R37-io-comments-legacy-vml-2-3: the <text> element is CT_Rst, which allows
                // <rPh>/<t> phonetic-guide (furigana/pinyin reading-hint) runs alongside the
                // visible <r>/<t> runs. Real Excel only ever displays the visible run text as the
                // comment's text -- a plain Descendants("t") would also pull in the <rPh> text and
                // corrupt the modeled comment text for Japanese/Chinese-authored comments that
                // carry a phonetic guide, so exclude any <t> whose parent is <rPh>.
                var text = string.Concat(comment
                    .Element(worksheetNs + "text")?
                    .Descendants(worksheetNs + "t")
                    .Where(t => t.Parent?.Name != worksheetNs + "rPh")
                    .Select(element => element.Value) ?? []);
                if (text.Length == 0)
                    continue;

                var authorId = comment.Attribute("authorId")?.Value;
                var author = "";
                if (authorId is not null &&
                    int.TryParse(authorId, out var authorIndex) &&
                    authorIndex >= 0 &&
                    authorIndex < authors.Count)
                {
                    author = authors[authorIndex] ?? "";
                }

                if (IsLegacyThreadedCommentShim(author, text))
                    continue;

                comments.Add((address.Row, address.Col, text, author));
            }
        }
        catch
        {
            // Comments are optional metadata. Keep workbook load resilient if a comment part is malformed.
        }
    }

    /// <summary>
    /// True when this legacy comment is Excel's backward-compat mirror of a threaded comment
    /// rather than a genuine, independently-authored Note. Excel identifies the shim two ways
    /// that are both checked here (either is sufficient): the legacy author is literally
    /// "tc={GUID}" (Excel never uses that as a real display name), or the text starts with the
    /// fixed "[Threaded comment]" compatibility banner Excel always prepends to the shim.
    /// </summary>
    private static bool IsLegacyThreadedCommentShim(string author, string text) =>
        author.StartsWith(LegacyThreadedCommentAuthorPrefix, StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith(LegacyThreadedCommentBannerPrefix, StringComparison.Ordinal);

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
    }
}
