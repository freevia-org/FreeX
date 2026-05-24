using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetCommentReader
{
    private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";

    public static IReadOnlyList<(uint Row, uint Col, string Text)> Read(ZipArchive archive, string worksheetPath)
    {
        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is null)
            return [];

        var commentPartPaths = ReadCommentPartPaths(archive, relationshipsEntry, worksheetPath);
        if (commentPartPaths.Count == 0)
            return [];

        var comments = new List<(uint Row, uint Col, string Text)>();
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

    private static void ReadComments(ZipArchiveEntry commentEntry, List<(uint Row, uint Col, string Text)> comments)
    {
        try
        {
            var commentsXml = LoadXml(commentEntry);
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

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

                var text = string.Concat(comment
                    .Element(worksheetNs + "text")?
                    .Descendants(worksheetNs + "t")
                    .Select(element => element.Value) ?? []);
                if (text.Length == 0)
                    continue;

                comments.Add((address.Row, address.Col, text));
            }
        }
        catch
        {
            // Comments are optional metadata. Keep workbook load resilient if a comment part is malformed.
        }
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
