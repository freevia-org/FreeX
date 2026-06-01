using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetThreadedCommentMapper
{
    private const string ThreadedCommentsContentType = "application/vnd.ms-excel.threadedcomments+xml";
    private const string PersonContentType = "application/vnd.ms-excel.person+xml";
    private const string ThreadedCommentsRelationshipType = "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment";
    private const string PersonRelationshipType = "http://schemas.microsoft.com/office/2017/10/relationships/person";
    private const string WorkbookPath = "xl/workbook.xml";
    private const string WorkbookRelsPath = "xl/_rels/workbook.xml.rels";
    private const string PersonsPath = "xl/persons/person.xml";

    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static bool HasThreadedComments(Sheet sheet) => sheet.ThreadedComments.Count > 0;

    public static IReadOnlyList<(uint Row, uint Col, ThreadedComment Comment)> Read(
        ZipArchive archive,
        string worksheetPath)
    {
        var threadedCommentPartPaths = ReadThreadedCommentPartPaths(archive, worksheetPath);
        if (threadedCommentPartPaths.Count == 0)
            return [];

        var authorsByPersonId = ReadPersons(archive);
        var comments = new List<(uint Row, uint Col, ThreadedComment Comment)>();
        foreach (var partPath in threadedCommentPartPaths)
        {
            var entry = archive.GetEntry(partPath);
            if (entry is null)
                continue;

            ReadThreadedComments(entry, authorsByPersonId, comments);
        }

        return comments;
    }

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null || !workbook.Sheets.Any(HasThreadedComments))
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var authorsByName = CreateAuthorIds(workbook);
        if (authorsByName.Count == 0)
            return;

        WritePersonsPart(archive, authorsByName);
        EnsureWorkbookPersonRelationship(archive);

        for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
        {
            var sheet = workbook.Sheets[sheetIndex];
            if (sheet.ThreadedComments.Count == 0)
                continue;

            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var threadedCommentPath = $"xl/threadedComments/threadedComment{sheetIndex + 1}.xml";
            WriteThreadedCommentsPart(archive, threadedCommentPath, sheet, authorsByName);
            EnsureWorksheetThreadedCommentRelationship(archive, worksheetPath, threadedCommentPath);
        }
    }

    private static IReadOnlyDictionary<string, string> CreateAuthorIds(Workbook workbook)
    {
        var authors = workbook.Sheets
            .SelectMany(sheet => sheet.ThreadedComments.Values.Select(comment => NormalizeAuthor(comment.Author)))
            .Where(author => author.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToDictionary(author => author, author => CreateStableGuid("person", author), StringComparer.Ordinal);

        return authors;
    }

    private static IReadOnlyList<string> ReadThreadedCommentPartPaths(ZipArchive archive, string worksheetPath)
    {
        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is null)
            return [];

        try
        {
            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            return relationshipsXml.Root?
                .Elements(PackageRelNs + "Relationship")
                .Where(element =>
                    string.Equals(element.Attribute("Type")?.Value, ThreadedCommentsRelationshipType, StringComparison.OrdinalIgnoreCase) &&
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

    private static IReadOnlyDictionary<string, string> ReadPersons(ZipArchive archive)
    {
        var personPartPaths = ReadPersonPartPaths(archive);
        if (personPartPaths.Count == 0 && archive.GetEntry(PersonsPath) is not null)
            personPartPaths = [PersonsPath];

        var authorsByPersonId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var personPartPath in personPartPaths)
        {
            var entry = archive.GetEntry(personPartPath);
            if (entry is null)
                continue;

            try
            {
                var personsXml = XlsxPackageXmlEditor.LoadXml(entry);
                foreach (var person in personsXml.Root?.Elements(ThreadedCommentNs + "person") ?? [])
                {
                    var id = person.Attribute("id")?.Value;
                    var displayName = person.Attribute("displayName")?.Value;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(displayName))
                        authorsByPersonId[id] = displayName;
                }
            }
            catch
            {
                // Threaded comments are optional package metadata; ignore malformed person parts.
            }
        }

        return authorsByPersonId;
    }

    private static IReadOnlyList<string> ReadPersonPartPaths(ZipArchive archive)
    {
        var workbookRelsEntry = archive.GetEntry(WorkbookRelsPath);
        if (workbookRelsEntry is null)
            return [];

        try
        {
            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(workbookRelsEntry);
            return relationshipsXml.Root?
                .Elements(PackageRelNs + "Relationship")
                .Where(element =>
                    string.Equals(element.Attribute("Type")?.Value, PersonRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(element.Attribute("Target")?.Value))
                .Select(element => XlsxPackagePath.ResolveRelationshipTarget(WorkbookPath, element.Attribute("Target")!.Value))
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

    private static void ReadThreadedComments(
        ZipArchiveEntry entry,
        IReadOnlyDictionary<string, string> authorsByPersonId,
        List<(uint Row, uint Col, ThreadedComment Comment)> comments)
    {
        try
        {
            var commentsXml = XlsxPackageXmlEditor.LoadXml(entry);
            foreach (var comment in commentsXml.Root?.Elements(ThreadedCommentNs + "threadedComment") ?? [])
            {
                if (comment.Attribute("parentId") is not null)
                    continue;

                var reference = comment.Attribute("ref")?.Value;
                if (string.IsNullOrWhiteSpace(reference) ||
                    !CellAddress.TryParse(reference, SheetId.New(), out var address))
                {
                    continue;
                }

                var text = comment.Element(ThreadedCommentNs + "text")?.Value ?? "";
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var personId = comment.Attribute("personId")?.Value ?? "";
                var author = authorsByPersonId.TryGetValue(personId, out var displayName)
                    ? displayName
                    : "FreeX";
                comments.Add((address.Row, address.Col, new ThreadedComment(text, author)
                {
                    CreatedAtUtc = ParseDateTimeOffset(comment.Attribute("dT")?.Value),
                    ModifiedAtUtc = ParseDateTimeOffset(comment.Attribute("dT")?.Value),
                    IsResolved = XlsxWorksheetXmlValueParser.IsTruthy(comment.Attribute("done")?.Value)
                }));
            }
        }
        catch
        {
            // Keep workbook load resilient if a threaded-comment part is malformed.
        }
    }

    private static void WritePersonsPart(ZipArchive archive, IReadOnlyDictionary<string, string> authorsByName)
    {
        var personsXml = new XDocument(
            new XElement(
                ThreadedCommentNs + "personList",
                authorsByName.Select(pair =>
                    new XElement(
                        ThreadedCommentNs + "person",
                        new XAttribute("displayName", pair.Key),
                        new XAttribute("id", pair.Value)))));

        XlsxPackageXmlEditor.ReplaceXml(archive, PersonsPath, personsXml);
        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, PersonsPath, PersonContentType);
    }

    private static void EnsureWorkbookPersonRelationship(ZipArchive archive)
    {
        var workbookRelsEntry = archive.GetEntry(WorkbookRelsPath);
        var workbookRelsXml = workbookRelsEntry is null
            ? new XDocument(new XElement(PackageRelNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(workbookRelsEntry);

        XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            workbookRelsXml,
            PackageRelNs,
            WorkbookPath,
            PersonsPath,
            PersonRelationshipType);
        XlsxPackageXmlEditor.ReplaceXml(archive, WorkbookRelsPath, workbookRelsXml);
    }

    private static void WriteThreadedCommentsPart(
        ZipArchive archive,
        string threadedCommentPath,
        Sheet sheet,
        IReadOnlyDictionary<string, string> authorsByName)
    {
        var commentsXml = new XDocument(
            new XElement(
                ThreadedCommentNs + "ThreadedComments",
                sheet.ThreadedComments
                    .OrderBy(pair => pair.Key.Row)
                    .ThenBy(pair => pair.Key.Col)
                    .Select(pair => ToThreadedCommentElement(sheet, pair.Key, pair.Value, authorsByName))));

        XlsxPackageXmlEditor.ReplaceXml(archive, threadedCommentPath, commentsXml);
        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, threadedCommentPath, ThreadedCommentsContentType);
    }

    private static XElement ToThreadedCommentElement(
        Sheet sheet,
        CellAddress address,
        ThreadedComment comment,
        IReadOnlyDictionary<string, string> authorsByName)
    {
        var author = NormalizeAuthor(comment.Author);
        var element = new XElement(
            ThreadedCommentNs + "threadedComment",
            new XAttribute("ref", address.ToA1()),
            new XAttribute("personId", authorsByName[author]),
            new XAttribute("id", CreateStableGuid("comment", $"{sheet.Name}!{address.ToA1()}:{comment.Text}")),
            new XElement(ThreadedCommentNs + "text", comment.Text));

        if (comment.CreatedAtUtc is { } createdAt)
            element.SetAttributeValue("dT", FormatDateTimeOffset(createdAt));
        if (comment.IsResolved)
            element.SetAttributeValue("done", "1");

        return element;
    }

    private static void EnsureWorksheetThreadedCommentRelationship(
        ZipArchive archive,
        string worksheetPath,
        string threadedCommentPath)
    {
        var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
        var worksheetRelsXml = worksheetRelsEntry is null
            ? new XDocument(new XElement(PackageRelNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry);

        XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            worksheetRelsXml,
            PackageRelNs,
            worksheetPath,
            threadedCommentPath,
            ThreadedCommentsRelationshipType);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static string NormalizeAuthor(string? author) =>
        string.IsNullOrWhiteSpace(author) ? "FreeX" : author.Trim();

    private static string CreateStableGuid(string scope, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"FreeX:{scope}:{value}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, bytes.Length).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return $"{{{new Guid(bytes).ToString("D").ToUpperInvariant()}}}";
    }

    private static string FormatDateTimeOffset(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
