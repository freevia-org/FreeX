using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// R74-io-comments-threaded-4-2: a source-package <c>&lt;person&gt;</c> record's full identity, as
/// read by <see cref="XlsxWorksheetThreadedCommentMapper.ReadPersonRecords"/> -- <c>UserId</c>/
/// <c>ProviderId</c>/<c>ExtLstXml</c> are captured in ADDITION to <c>DisplayName</c> (which
/// <see cref="XlsxWorksheetThreadedCommentMapper"/>'s own person-id resolution already needs) so a
/// caller can round-trip them through <see cref="XlsxWorksheetThreadedCommentMapper.Save"/>'s
/// optional <c>sourcePersonRecordsById</c> parameter.
/// </summary>
internal readonly record struct PersonRecord(string DisplayName, string? UserId, string? ProviderId, string? ExtLstXml);

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

    public static IReadOnlySet<string> GetSourcePackagePartExclusions(ZipArchive archive, Workbook workbook)
    {
        // Always exclude the source package's referenced threaded-comment/person parts, even when
        // the in-memory model no longer has ANY threaded comments (e.g. the user deleted every
        // comment before saving). Save/NormalizePackageGraph below both early-return in that case
        // (nothing left to (re)write), so if these source parts were not excluded here,
        // XlsxPackageMetadataMerger.CopyUnknownPackageParts/MergeRelationshipParts would copy the
        // deleted comments' XML and worksheet/workbook relationships straight back from the source
        // package, silently resurrecting a deletion the user just made. When the model still has
        // comments, excluding the stale source parts is harmless: Save writes fresh replacements.
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetReferencedThreadedCommentAndPersonPartPaths(archive))
        {
            excluded.Add(path);
            excluded.Add(XlsxPackagePath.GetRelationshipPartPath(path));
        }

        return excluded;
    }

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
        XlsxWorkbookWorksheetPathMap? worksheetPathMap,
        IReadOnlyDictionary<string, PersonRecord>? sourcePersonRecordsById = null)
    {
        if (worksheetPathMap is null || !workbook.Sheets.Any(HasThreadedComments))
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var authorsByName = CreateAuthorIds(workbook);
        if (authorsByName.Count == 0)
            return;

        // R56-io-comments-threaded-5-2: the full set of person ids actually referenced by any
        // comment/reply -- NOT just authorsByName's one-id-per-displayName fallback -- so two
        // distinct real authors sharing a displayName each still get their own <person> record
        // (see ResolvePersonIdentitiesById).
        var personIdentitiesById = ResolvePersonIdentitiesById(workbook, authorsByName);
        // R74-io-comments-threaded-4-2: sourcePersonRecordsById (see ReadPersonRecords), when the
        // caller supplies it, lets WritePersonsPart re-emit each surviving person's ORIGINAL
        // userId/providerId/extLst instead of silently dropping them on every save that rewrites
        // this part.
        WritePersonsPart(
            archive,
            personIdentitiesById,
            GetNonAuthoringMentionedPersons(workbook, personIdentitiesById.Keys),
            sourcePersonRecordsById);
        EnsureWorkbookPersonRelationship(archive);

        // Allocate next-free threaded comment part indices, checking existing archive entries to
        // avoid overwriting an unrelated part if indices drift after a sheet reorder.
        var usedThreadedCommentIndices = GetUsedThreadedCommentIndices(archive);
        var nextIndex = 1;

        for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
        {
            var sheet = workbook.Sheets[sheetIndex];
            if (sheet.ThreadedComments.Count == 0)
                continue;

            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            // Prefer a path derived from the worksheet index so stable saves look the same, but
            // always pick the next-free slot to avoid collisions after sheet reorders.
            while (usedThreadedCommentIndices.Contains(nextIndex))
                nextIndex++;

            var threadedCommentPath = $"xl/threadedComments/threadedComment{nextIndex}.xml";
            usedThreadedCommentIndices.Add(nextIndex);
            nextIndex++;
            WriteThreadedCommentsPart(archive, threadedCommentPath, sheet, authorsByName);
            EnsureWorksheetThreadedCommentRelationship(archive, worksheetPath, threadedCommentPath);
        }
    }

    public static void NormalizePackageGraph(
        Stream xlsxStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null || !workbook.Sheets.Any(HasThreadedComments))
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        EnsureWorkbookPersonRelationship(archive);

        // NormalizePackageGraph only updates relationships — it does not write new threaded comment
        // parts. Use simple sequential indexing (sheetIndex + 1) so the relationship target matches
        // the part that was already written during the initial save.
        var sheetThreadedCommentIndex = 1;
        for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
        {
            var sheet = workbook.Sheets[sheetIndex];
            if (sheet.ThreadedComments.Count == 0)
                continue;

            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var threadedCommentPath = $"xl/threadedComments/threadedComment{sheetThreadedCommentIndex}.xml";
            sheetThreadedCommentIndex++;
            EnsureWorksheetThreadedCommentRelationship(archive, worksheetPath, threadedCommentPath);
        }
    }

    private static HashSet<int> GetUsedThreadedCommentIndices(ZipArchive archive)
    {
        var used = new HashSet<int>();
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.StartsWith("xl/threadedComments/threadedComment", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                var stem = name["xl/threadedComments/threadedComment".Length..^".xml".Length];
                if (int.TryParse(stem, out var index))
                    used.Add(index);
            }
        }

        return used;
    }

    /// <summary>
    /// Computes the DEFAULT/fallback personId for each distinct author displayName -- used by
    /// <see cref="ResolvePersonId"/> only for a comment/reply whose own <c>SourcePersonId</c> is
    /// null (e.g. a freshly-authored in-memory comment with no preserved source id at all). A
    /// comment/reply that DOES carry its own preserved <c>SourcePersonId</c> (see
    /// <see cref="ThreadedComment.SourcePersonId"/>, captured unconditionally on load as of
    /// R56-io-comments-threaded-5-2) always uses that id directly instead, which is what keeps two
    /// distinct real authors who happen to share a displayName apart even though this dictionary
    /// itself can only ever hold ONE id per displayName.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CreateAuthorIds(Workbook workbook)
    {
        // Prefer a preserved source personId over a freshly minted per-author guid, so a preserved
        // mentionpersonId reference still resolves to a person id present in the rewritten
        // xl/persons/person.xml. When an author has more than one candidate source id (e.g. a
        // genuine displayName collision between two real people), the first one encountered (in
        // the same deterministic ordering used below) wins for this FALLBACK dictionary only --
        // ResolvePersonId still resolves each individual comment/reply to its own distinct
        // SourcePersonId directly when one is present, so the collision is not lost overall.
        var sourcePersonIdsByAuthor = workbook.Sheets
            .SelectMany(sheet => sheet.ThreadedComments.Values.SelectMany(GetThreadAuthorsWithSourcePersonId))
            .Where(pair => pair.Author.Length > 0 && pair.SourcePersonId is not null)
            .GroupBy(pair => pair.Author, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SourcePersonId!, StringComparer.Ordinal);

        // Fallback source: a person id that some OTHER comment's preserved @mention captured for
        // a display name (via MentionedPersonDisplayNames, populated unconditionally on load --
        // see ReadMentionedPersonDisplayNames) that matches an author elsewhere in the workbook.
        // This covers an author who is @mentioned by someone else but whose OWN comment carries
        // no mentions of its own (e.g. Alice mentions Bob, and Bob separately authors a plain,
        // mention-free comment): without this, Bob would have no entry in sourcePersonIdsByAuthor
        // above and would get a freshly minted guid, while GetNonAuthoringMentionedPersons then
        // adds a SECOND <person> record under Bob's real id purely to keep Alice's untouched
        // mention resolvable -- splitting one real person into two records
        // (see R34-io-comments-threaded-mentions-2). When a display name has more than one
        // candidate mentioned id, the first one encountered wins, same tie-break as above.
        var mentionedPersonIdsByDisplayName = GetMentionedPersonIdsByDisplayName(workbook);

        var authors = workbook.Sheets
            .SelectMany(sheet => sheet.ThreadedComments.Values.SelectMany(GetThreadAuthorsWithSourcePersonId))
            .Select(pair => pair.Author)
            .Where(author => author.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToDictionary(
                author => author,
                author => sourcePersonIdsByAuthor.TryGetValue(author, out var sourcePersonId)
                    ? sourcePersonId
                    : mentionedPersonIdsByDisplayName.TryGetValue(author, out var mentionedPersonId)
                        ? mentionedPersonId
                        : CreateStableGuid("person", author),
                StringComparer.Ordinal);

        return authors;
    }

    private static IReadOnlyDictionary<string, string> GetMentionedPersonIdsByDisplayName(Workbook workbook)
    {
        var byDisplayName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var comment in sheet.ThreadedComments.Values)
            {
                AddMentionedPersonIdsByDisplayName(comment.MentionedPersonDisplayNames, byDisplayName);
                foreach (var reply in comment.Replies)
                    AddMentionedPersonIdsByDisplayName(reply.MentionedPersonDisplayNames, byDisplayName);
            }
        }

        return byDisplayName;
    }

    private static void AddMentionedPersonIdsByDisplayName(
        IReadOnlyDictionary<string, string>? mentionedPersonDisplayNames,
        Dictionary<string, string> byDisplayName)
    {
        if (mentionedPersonDisplayNames is null)
            return;

        foreach (var (personId, displayName) in mentionedPersonDisplayNames)
            byDisplayName.TryAdd(displayName, personId);
    }

    private static IEnumerable<(string Author, string? SourcePersonId)> GetThreadAuthorsWithSourcePersonId(
        ThreadedComment comment)
    {
        yield return (NormalizeAuthor(comment.Author), comment.SourcePersonId);
        foreach (var reply in comment.Replies)
            yield return (NormalizeAuthor(reply.Author), reply.SourcePersonId);
    }

    /// <summary>
    /// Resolves the personId to write for one specific comment/reply: its own preserved
    /// <paramref name="sourcePersonId"/> when present (trusted directly, regardless of whether
    /// <paramref name="authorsByName"/>'s single per-displayName entry agrees -- this is what keeps
    /// two distinct real authors who share a displayName apart, see
    /// R56-io-comments-threaded-5-2), otherwise the per-author fallback from
    /// <see cref="CreateAuthorIds"/>.
    /// </summary>
    private static string ResolvePersonId(
        string author,
        string? sourcePersonId,
        IReadOnlyDictionary<string, string> authorsByName) =>
        sourcePersonId ?? authorsByName[author];

    /// <summary>
    /// Computes the full set of person ids actually referenced by any comment/reply on save
    /// (personId -&gt; displayName), used to emit <see cref="WritePersonsPart"/>'s primary
    /// &lt;person&gt; entries. Usually one id per distinct author displayName (mirroring
    /// <paramref name="authorsByName"/>), but when the SAME displayName resolves (via each
    /// comment/reply's own preserved <c>SourcePersonId</c>) to two or more DISTINCT ids -- e.g. two
    /// different real people, possibly from different organizations, who happen to share a
    /// displayName -- every one of those distinct ids gets its own entry here, so the two real
    /// people stay two separate &lt;person&gt; records instead of collapsing into
    /// <paramref name="authorsByName"/>'s single id for that name (see R56-io-comments-threaded-5-2).
    /// </summary>
    private static IReadOnlyDictionary<string, string> ResolvePersonIdentitiesById(
        Workbook workbook,
        IReadOnlyDictionary<string, string> authorsByName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var comment in sheet.ThreadedComments.Values)
            {
                foreach (var (author, sourcePersonId) in GetThreadAuthorsWithSourcePersonId(comment))
                    result.TryAdd(ResolvePersonId(author, sourcePersonId, authorsByName), author);
            }
        }

        return result;
    }

    /// <summary>
    /// Collects a display name, by source person id, for every @-mentioned person captured on load
    /// (<see cref="ThreadedComment.MentionedPersonDisplayNames"/> / <see cref="CommentReply.MentionedPersonDisplayNames"/>)
    /// whose id is not already one of <paramref name="authorPersonIds"/> (every id actually
    /// resolved for a comment/reply author, see <see cref="ResolvePersonIdentitiesById"/>). Without
    /// these, a mentioned person who never posts a comment/reply would get no &lt;person&gt; record
    /// on save, leaving their preserved mentionpersonId dangling (see R12-comments-notes-3).
    /// </summary>
    private static IReadOnlyDictionary<string, string> GetNonAuthoringMentionedPersons(
        Workbook workbook,
        IEnumerable<string> authorPersonIds)
    {
        var authorPersonIdSet = authorPersonIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mentioned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var comment in sheet.ThreadedComments.Values)
            {
                AddNonAuthoringMentionedPersons(comment.MentionedPersonDisplayNames, authorPersonIdSet, mentioned);
                foreach (var reply in comment.Replies)
                    AddNonAuthoringMentionedPersons(reply.MentionedPersonDisplayNames, authorPersonIdSet, mentioned);
            }
        }

        return mentioned;
    }

    private static void AddNonAuthoringMentionedPersons(
        IReadOnlyDictionary<string, string>? mentionedPersonDisplayNames,
        IReadOnlySet<string> authorPersonIds,
        Dictionary<string, string> mentioned)
    {
        if (mentionedPersonDisplayNames is null)
            return;

        foreach (var (personId, displayName) in mentionedPersonDisplayNames)
        {
            if (!authorPersonIds.Contains(personId))
                mentioned.TryAdd(personId, displayName);
        }
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

    private static IReadOnlyDictionary<string, string> ReadPersons(ZipArchive archive) =>
        ReadPersonRecords(archive).ToDictionary(
            pair => pair.Key,
            pair => pair.Value.DisplayName,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// R74-io-comments-threaded-4-2: reads each <c>&lt;person&gt;</c> record's full identity --
    /// not just its displayName (see <see cref="ReadPersons"/>, which now delegates here) -- so a
    /// caller with access to the ORIGINAL source package (e.g. via the loaded workbook's captured
    /// source package) can pass the result to <see cref="Save"/>'s optional
    /// <c>sourcePersonRecordsById</c> parameter and have <see cref="WritePersonsPart"/> preserve
    /// each surviving person's <c>userId</c>/<c>providerId</c>/<c>extLst</c> across a save that
    /// rewrites <c>xl/persons/person.xml</c>. Excel uses <c>userId</c>/<c>providerId</c> to
    /// resolve @mentions to a real account; without this, <see cref="WritePersonsPart"/>
    /// previously re-emitted only <c>displayName</c>+<c>id</c> on every such save, silently
    /// discarding both attributes (and any <c>extLst</c>) for every threaded-comment workbook.
    /// </summary>
    public static IReadOnlyDictionary<string, PersonRecord> ReadPersonRecords(ZipArchive archive)
    {
        var personPartPaths = ReadPersonPartPaths(archive);
        if (personPartPaths.Count == 0 && archive.GetEntry(PersonsPath) is not null)
            personPartPaths = [PersonsPath];

        var recordsById = new Dictionary<string, PersonRecord>(StringComparer.OrdinalIgnoreCase);
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
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName))
                        continue;

                    recordsById[id] = new PersonRecord(
                        displayName,
                        person.Attribute("userId")?.Value,
                        person.Attribute("providerId")?.Value,
                        person.Element(ThreadedCommentNs + "extLst")?.ToString(SaveOptions.DisableFormatting));
                }
            }
            catch
            {
                // Threaded comments are optional package metadata; ignore malformed person parts.
            }
        }

        return recordsById;
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
            var parsedComments = new List<ParsedThreadedComment>();
            foreach (var comment in commentsXml.Root?.Elements(ThreadedCommentNs + "threadedComment") ?? [])
            {
                var reference = comment.Attribute("ref")?.Value;
                var address = default(CellAddress);
                var hasAddress = !string.IsNullOrWhiteSpace(reference) &&
                    CellAddress.TryParse(reference, SheetId.New(), out address);

                var parentId = NormalizeId(comment.Attribute("parentId")?.Value);
                if (parentId is null && !hasAddress)
                    continue;

                var text = comment.Element(ThreadedCommentNs + "text")?.Value ?? "";
                // Only drop whitespace-only replies here; a whitespace-only root must be kept
                // for now even though its own text is empty, because it may still have real
                // replies grouped under it below. A genuinely empty root (no text, no replies)
                // is filtered out later once repliesByParentId is known.
                if (parentId is not null && string.IsNullOrWhiteSpace(text))
                    continue;

                var personId = comment.Attribute("personId")?.Value ?? "";
                var author = authorsByPersonId.TryGetValue(personId, out var displayName)
                    ? displayName
                    : "FreeX";
                var mentionsXml = ReadMentionsXml(comment);
                parsedComments.Add(new ParsedThreadedComment(
                    hasAddress ? address.Row : null,
                    hasAddress ? address.Col : null,
                    NormalizeId(comment.Attribute("id")?.Value),
                    parentId,
                    text,
                    author,
                    ParseDateTimeOffset(comment.Attribute("dT")?.Value),
                    XlsxWorksheetXmlValueParser.IsTruthy(comment.Attribute("done")?.Value),
                    mentionsXml,
                    // R56-io-comments-threaded-5-2: always preserve the source personId, not only
                    // when this comment happens to carry @mention metadata of its own. Two distinct
                    // real authors can legitimately share the same displayName (e.g. across
                    // organizations in a cross-company review workbook); gating this on @mentions
                    // left every such pair indistinguishable once resolved to "author" (a plain
                    // displayName string) below, so CreateAuthorIds/WritePersonsPart on save could
                    // only ever see ONE of the two ids and silently merged them into one <person>.
                    // Capturing the id unconditionally lets ToThreadedCommentElement/
                    // ToThreadedCommentReplyElement (via ResolvePersonId) trust each comment's own
                    // preserved id directly on save, keeping distinct source persons distinct even
                    // when their displayName collides. (The mentionedPersonIdsByDisplayName
                    // cross-reference in CreateAuthorIds remains for the separate case of an author
                    // whose comment/reply model instance was built without a source id at all, e.g.
                    // a freshly-authored in-memory comment.)
                    NormalizeId(personId),
                    // Capture a display name for every @-mentioned person so a mention referencing
                    // someone who never authors a comment/reply still gets a <person> record
                    // written back on save (see ReadMentionedPersonDisplayNames).
                    ReadMentionedPersonDisplayNames(comment, authorsByPersonId)));
            }

            var repliesByParentId = parsedComments
                .Where(comment => comment.ParentId is not null)
                .GroupBy(comment => comment.ParentId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            // R110: tracks which repliesByParentId keys got consumed by a matching root below, so
            // the orphaned-group recovery pass after this loop knows which groups were never
            // reachable via the root-only enumeration.
            var matchedParentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in parsedComments.Where(comment => comment.ParentId is null))
            {
                if (root.Row is not { } row || root.Col is not { } col)
                    continue;

                var replies = root.Id is not null &&
                    repliesByParentId.TryGetValue(root.Id, out var parsedReplies)
                    ? parsedReplies.Select(ToCommentReply).ToList()
                    : [];

                // A whitespace-only root with no replies is genuinely empty; drop it, matching
                // prior behavior. A whitespace-only root that DOES have replies is a valid Excel
                // state (empty root comment, real reply text) and must be preserved so its
                // replies are not orphaned and dropped.
                if (replies.Count == 0 && string.IsNullOrWhiteSpace(root.Text))
                    continue;

                var threadedComment = new ThreadedComment(root.Text, root.Author)
                {
                    CreatedAtUtc = root.TimestampUtc,
                    ModifiedAtUtc = GetThreadModifiedAt(root.TimestampUtc, replies),
                    IsResolved = root.IsResolved,
                    Id = root.Id,
                    MentionsXml = root.MentionsXml,
                    SourcePersonId = root.SourcePersonId,
                    MentionedPersonDisplayNames = root.MentionedPersonDisplayNames
                };
                if (replies.Count > 0)
                    threadedComment = threadedComment with { Replies = replies };

                comments.Add((row, col, threadedComment));
                if (root.Id is not null)
                    matchedParentIds.Add(root.Id);
            }

            // R110: a reply group whose parentId never matched any root's Id in this part is an
            // orphaned thread -- the root <threadedComment> element is missing (a non-Excel writer,
            // a hand-edit, or an Excel co-authoring merge conflict can produce this; genuine
            // desktop-Excel delete flows always remove a thread's root and replies together).
            // Previously the loop above was the ONLY enumeration path over parsedComments, so any
            // such group sat unconsumed in repliesByParentId and its text/author/timestamps/mentions
            // were silently dropped -- and because this runs on LOAD, the very next Save made that
            // loss permanent (WriteThreadedCommentsPart only re-serializes what reached
            // Sheet.ThreadedComments). Recover what we can: promote the group's own earliest member
            // that actually carries a cell address (real Excel replies never carry <ref>; only a
            // non-standard producer would put one on a "reply") to stand in as a synthetic root, and
            // keep the remaining members as its replies -- matching how other Excel-ecosystem tools
            // recover orphaned threads. When no member of the group carries an address there is no
            // cell to attach the content to at all, so it cannot be represented in the
            // per-cell-keyed Sheet.ThreadedComments model; surface that loss via a diagnostic instead
            // of dropping it in total silence.
            foreach (var (parentId, group) in repliesByParentId)
            {
                if (matchedParentIds.Contains(parentId))
                    continue;

                var anchorIndex = group.FindIndex(candidate => candidate.Row is not null && candidate.Col is not null);
                if (anchorIndex < 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[XlsxWorksheetThreadedCommentMapper] Dropping {group.Count} orphaned threaded comment " +
                        $"repl{(group.Count == 1 ? "y" : "ies")} with parentId '{parentId}': the root threadedComment " +
                        "is missing from this part and no member of the reply group carries a cell address to " +
                        "recover it under.");
                    continue;
                }

                var anchor = group[anchorIndex];
                var remainingReplies = group
                    .Where((_, index) => index != anchorIndex)
                    .Select(ToCommentReply)
                    .ToList();

                var syntheticRoot = new ThreadedComment(anchor.Text, anchor.Author)
                {
                    CreatedAtUtc = anchor.TimestampUtc,
                    ModifiedAtUtc = GetThreadModifiedAt(anchor.TimestampUtc, remainingReplies),
                    IsResolved = anchor.IsResolved,
                    Id = anchor.Id,
                    MentionsXml = anchor.MentionsXml,
                    SourcePersonId = anchor.SourcePersonId,
                    MentionedPersonDisplayNames = anchor.MentionedPersonDisplayNames
                };
                if (remainingReplies.Count > 0)
                    syntheticRoot = syntheticRoot with { Replies = remainingReplies };

                comments.Add((anchor.Row!.Value, anchor.Col!.Value, syntheticRoot));
            }
        }
        catch
        {
            // Keep workbook load resilient if a threaded-comment part is malformed.
        }
    }

    /// <summary>
    /// Captures the CT_ThreadedComment @mention metadata that follows &lt;text&gt;: the real
    /// &lt;mentions&gt; child element (per the 2018 threadedcomments schema) followed by any
    /// &lt;extLst&gt; child, in schema order. Both are optional and FreeX does not model @mention
    /// linkage, so the raw fragment(s) are concatenated verbatim for round-tripping on save.
    /// </summary>
    private static string? ReadMentionsXml(XElement comment)
    {
        var mentions = comment.Element(ThreadedCommentNs + "mentions")?.ToString(SaveOptions.DisableFormatting);
        var extLst = comment.Element(ThreadedCommentNs + "extLst")?.ToString(SaveOptions.DisableFormatting);
        return (mentions, extLst) switch
        {
            (null, null) => null,
            (not null, null) => mentions,
            (null, not null) => extLst,
            (not null, not null) => mentions + extLst
        };
    }

    /// <summary>
    /// Resolves a display name (via the source persons part already loaded into
    /// <paramref name="authorsByPersonId"/>) for every distinct <c>mentionpersonId</c> referenced
    /// under this comment's real &lt;mentions&gt; child and/or &lt;extLst&gt;-nested
    /// <c>mtc:mentions</c> block, regardless of which shape carries it. A mentioned person who
    /// never authors any comment/reply would otherwise have no <c>&lt;person&gt;</c> record
    /// written back on save (person.xml is rewritten solely from comment/reply authors), leaving
    /// the mention dangling after the round trip.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ReadMentionedPersonDisplayNames(
        XElement comment,
        IReadOnlyDictionary<string, string> authorsByPersonId)
    {
        Dictionary<string, string>? result = null;
        foreach (var mentionElement in comment.Descendants().Where(element => element.Name.LocalName == "mention"))
        {
            var mentionPersonId = NormalizeId(mentionElement.Attribute("mentionpersonId")?.Value);
            if (mentionPersonId is null || !authorsByPersonId.TryGetValue(mentionPersonId, out var displayName))
                continue;

            result ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            result[mentionPersonId] = displayName;
        }

        return result;
    }

    private static void WritePersonsPart(
        ZipArchive archive,
        IReadOnlyDictionary<string, string> personIdentitiesById,
        IReadOnlyDictionary<string, string> nonAuthoringMentionedPersonsById,
        IReadOnlyDictionary<string, PersonRecord>? sourceRecordsById = null)
    {
        var personsXml = new XDocument(
            new XElement(
                ThreadedCommentNs + "personList",
                personIdentitiesById
                    .Select(pair => BuildPersonElement(pair.Key, pair.Value, sourceRecordsById))
                    .Concat(nonAuthoringMentionedPersonsById.Select(pair =>
                        BuildPersonElement(pair.Key, pair.Value, sourceRecordsById)))));

        XlsxPackageXmlEditor.ReplaceXml(archive, PersonsPath, personsXml);
        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, PersonsPath, PersonContentType);
    }

    /// <summary>
    /// R74-io-comments-threaded-4-2: builds one <c>&lt;person&gt;</c> element, preserving the
    /// ORIGINAL <c>userId</c>/<c>providerId</c>/<c>extLst</c> from <paramref name="sourceRecordsById"/>
    /// when the caller supplied a matching record for this <paramref name="id"/> and that record
    /// actually carries the attribute -- never emitting a spurious empty attribute otherwise.
    /// </summary>
    private static XElement BuildPersonElement(
        string id,
        string displayName,
        IReadOnlyDictionary<string, PersonRecord>? sourceRecordsById)
    {
        var element = new XElement(
            ThreadedCommentNs + "person",
            new XAttribute("displayName", displayName),
            new XAttribute("id", id));

        if (sourceRecordsById is null || !sourceRecordsById.TryGetValue(id, out var sourceRecord))
            return element;

        if (!string.IsNullOrEmpty(sourceRecord.UserId))
            element.SetAttributeValue("userId", sourceRecord.UserId);
        if (!string.IsNullOrEmpty(sourceRecord.ProviderId))
            element.SetAttributeValue("providerId", sourceRecord.ProviderId);
        if (!string.IsNullOrEmpty(sourceRecord.ExtLstXml))
        {
            try
            {
                element.Add(XElement.Parse(sourceRecord.ExtLstXml, LoadOptions.PreserveWhitespace));
            }
            catch (XmlException)
            {
                // Keep saves resilient if the preserved extLst fragment is somehow malformed.
            }
        }

        return element;
    }

    private static void EnsureWorkbookPersonRelationship(ZipArchive archive)
    {
        var workbookRelsEntry = archive.GetEntry(WorkbookRelsPath);
        var workbookRelsXml = workbookRelsEntry is null
            ? new XDocument(new XElement(PackageRelNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(workbookRelsEntry);

        RemoveRelationshipsForOtherPackageParts(
            workbookRelsXml,
            WorkbookPath,
            PersonsPath,
            PersonRelationshipType);
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
                    .SelectMany(pair => ToThreadedCommentElements(sheet, pair.Key, pair.Value, authorsByName))));

        XlsxPackageXmlEditor.ReplaceXml(archive, threadedCommentPath, commentsXml);
        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, threadedCommentPath, ThreadedCommentsContentType);
    }

    /// <summary>
    /// R93-threaded-comment-extLst: resolves the SAME stable thread id that
    /// <see cref="ToThreadedCommentElements"/> writes as the root <c>&lt;threadedComment&gt;</c>'s
    /// own <c>id</c> attribute, exposed so <see cref="XlsxFileAdapter"/>'s early ClosedXML cell
    /// population phase (which runs before this mapper's own post-processing pass touches the raw
    /// package) can embed the identical GUID into the legacy compatibility shim's
    /// <c>tc={GUID}</c> author -- matching real Excel, which always ties its legacy shim's author
    /// back to the thread's own id.
    /// </summary>
    internal static string ResolveThreadId(Sheet sheet, CellAddress address, ThreadedComment comment) =>
        comment.Id ?? CreateStableGuid("comment", $"{sheet.Name}!{address.ToA1()}:{comment.Text}");

    private static IEnumerable<XElement> ToThreadedCommentElements(
        Sheet sheet,
        CellAddress address,
        ThreadedComment comment,
        IReadOnlyDictionary<string, string> authorsByName)
    {
        // Preserve the id this comment was loaded with so it (and every reply's parentId, which
        // references it) stays stable across saves instead of cascade-changing whenever the
        // comment's text is edited. Only a comment that has never been saved before (no source
        // id) gets a freshly minted stable guid.
        var parentId = ResolveThreadId(sheet, address, comment);
        yield return ToThreadedCommentElement(address, comment, authorsByName, parentId);

        for (var replyIndex = 0; replyIndex < comment.Replies.Count; replyIndex++)
        {
            yield return ToThreadedCommentReplyElement(
                sheet,
                address,
                comment.Replies[replyIndex],
                replyIndex,
                authorsByName,
                parentId);
        }
    }

    private static XElement ToThreadedCommentElement(
        CellAddress address,
        ThreadedComment comment,
        IReadOnlyDictionary<string, string> authorsByName,
        string id)
    {
        var author = NormalizeAuthor(comment.Author);
        var element = new XElement(
            ThreadedCommentNs + "threadedComment",
            new XAttribute("ref", address.ToA1()),
            new XAttribute("personId", ResolvePersonId(author, comment.SourcePersonId, authorsByName)),
            new XAttribute("id", id),
            new XElement(ThreadedCommentNs + "text", comment.Text));

        SetDateTimeAttribute(element, ResolveRootThreadedCommentTimestamp(comment));
        if (comment.IsResolved)
            element.SetAttributeValue("done", "1");
        AppendMentionsXml(element, comment.MentionsXml);

        return element;
    }

    private static XElement ToThreadedCommentReplyElement(
        Sheet sheet,
        CellAddress address,
        CommentReply reply,
        int replyIndex,
        IReadOnlyDictionary<string, string> authorsByName,
        string parentId)
    {
        var author = NormalizeAuthor(reply.Author);
        // Preserve the reply's own source id the same way the root comment's id is preserved
        // above, so unrelated edits (e.g. to the root comment's text or to a sibling reply) do
        // not regenerate this reply's id.
        var id = reply.Id ?? CreateStableGuid(
            "comment-reply",
            $"{sheet.Name}!{address.ToA1()}:{parentId}:{replyIndex}:{author}:{reply.Text}");
        var element = new XElement(
            ThreadedCommentNs + "threadedComment",
            new XAttribute("ref", address.ToA1()),
            new XAttribute("personId", ResolvePersonId(author, reply.SourcePersonId, authorsByName)),
            new XAttribute("id", id),
            new XAttribute("parentId", parentId),
            new XElement(ThreadedCommentNs + "text", reply.Text));

        // Unlike the root comment (see ResolveRootThreadedCommentTimestamp), a reply's
        // CreatedAtUtc/ModifiedAtUtc pair is never polluted by sibling activity: ToCommentReply
        // sets both to the same source dT on load, and the only thing that ever moves
        // ModifiedAtUtc away from CreatedAtUtc afterwards is UpdateThreadedCommentReplyCommand
        // editing this specific reply's own text. So ModifiedAtUtc, when present, always reflects
        // this reply's own latest edit and must win over the (possibly stale) creation time.
        SetDateTimeAttribute(element, reply.ModifiedAtUtc ?? reply.CreatedAtUtc);
        AppendMentionsXml(element, reply.MentionsXml);
        return element;
    }

    private static void AppendMentionsXml(XElement element, string? mentionsXml)
    {
        if (string.IsNullOrWhiteSpace(mentionsXml))
            return;

        try
        {
            // The preserved payload can be up to two sibling elements (<mentions> followed by
            // <extLst>, per CT_ThreadedComment child order), and XElement.Parse requires a single
            // root, so wrap the fragment in a throwaway root and re-emit each child in the order
            // it was captured.
            var wrapped = XElement.Parse($"<w>{mentionsXml}</w>", LoadOptions.PreserveWhitespace);
            foreach (var child in wrapped.Elements())
                element.Add(new XElement(child));
        }
        catch (XmlException)
        {
            // Keep saves resilient if the preserved mentions/extLst fragment is somehow malformed.
        }
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

        RemoveRelationshipsForOtherPackageParts(
            worksheetRelsXml,
            worksheetPath,
            threadedCommentPath,
            ThreadedCommentsRelationshipType);
        XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            worksheetRelsXml,
            PackageRelNs,
            worksheetPath,
            threadedCommentPath,
            ThreadedCommentsRelationshipType);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static void RemoveRelationshipsForOtherPackageParts(
        XDocument relationshipsXml,
        string sourcePart,
        string targetPart,
        string relationshipType)
    {
        relationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship =>
                string.Equals(relationship.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    XlsxPackagePath.ResolveRelationshipTarget(sourcePart, relationship.Attribute("Target")?.Value ?? ""),
                    targetPart,
                    StringComparison.OrdinalIgnoreCase))
            .Remove();
    }

    private static string NormalizeAuthor(string? author) =>
        string.IsNullOrWhiteSpace(author) ? "FreeX" : author.Trim();

    private static bool IsThreadedCommentOrPersonPart(string path) =>
        path.StartsWith("xl/threadedComments/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("xl/persons/", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetReferencedThreadedCommentAndPersonPartPaths(ZipArchive archive)
    {
        foreach (var relationshipsEntry in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument relationshipsXml;
            try
            {
                relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            }
            catch
            {
                continue;
            }

            var sourcePartPath = RelationshipPartToSourcePart(relationshipsEntry.FullName);
            foreach (var relationship in relationshipsXml.Root?.Elements(PackageRelNs + "Relationship") ?? [])
            {
                var relationshipType = relationship.Attribute("Type")?.Value;
                if (!string.Equals(relationshipType, ThreadedCommentsRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(relationshipType, PersonRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                var targetPath = XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
                if (IsThreadedCommentOrPersonPart(targetPath))
                    yield return targetPath;
            }
        }
    }

    private static string RelationshipPartToSourcePart(string relationshipPartPath)
    {
        var normalized = XlsxPackagePath.NormalizePackagePath(relationshipPartPath);
        if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return "";

        const string relsSegment = "/_rels/";
        var relsIndex = normalized.IndexOf(relsSegment, StringComparison.OrdinalIgnoreCase);
        if (relsIndex < 0 || !normalized.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return normalized;

        var directory = normalized[..relsIndex];
        var fileName = normalized[(relsIndex + relsSegment.Length)..^".rels".Length];
        return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
    }

    private static string? NormalizeId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    private static string CreateStableGuid(string scope, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"FreeX:{scope}:{value}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, bytes.Length).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return $"{{{new Guid(bytes).ToString("D").ToUpperInvariant()}}}";
    }

    private static string FormatDateTimeOffset(DateTimeOffset value)
    {
        // R68-io-comment-note-6-3: a dT with sub-second precision (e.g. read from a source file
        // that carried fractional seconds) was silently truncated to whole seconds on every save
        // because the format string had no fractional-second token. Only emit the fractional
        // component when one is actually present, so an untouched whole-second dT still round-trips
        // to the exact same string as before (matches Excel's own dT format for that common case).
        var utc = value.ToUniversalTime();
        return utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static CommentReply ToCommentReply(ParsedThreadedComment comment) =>
        new(comment.Text, comment.Author)
        {
            CreatedAtUtc = comment.TimestampUtc,
            ModifiedAtUtc = comment.TimestampUtc,
            Id = comment.Id,
            MentionsXml = comment.MentionsXml,
            SourcePersonId = comment.SourcePersonId,
            MentionedPersonDisplayNames = comment.MentionedPersonDisplayNames
        };

    private static DateTimeOffset? GetThreadModifiedAt(
        DateTimeOffset? rootTimestampUtc,
        IReadOnlyList<CommentReply> replies)
    {
        var latest = rootTimestampUtc;
        foreach (var reply in replies)
        {
            var candidate = reply.ModifiedAtUtc ?? reply.CreatedAtUtc;
            if (candidate is not null && (latest is null || candidate > latest))
                latest = candidate;
        }

        return latest;
    }

    /// <summary>
    /// Determines the timestamp to persist as the root &lt;threadedComment&gt; element's own dT.
    ///
    /// When <see cref="ThreadedComment.RootTextEditedAtUtc"/> is set, the command layer has
    /// already unambiguously recorded the last genuine edit to the root's own text (see
    /// ThreadedCommentTimestamps.TouchRootTextEdit), so that value wins outright -- it can never
    /// be polluted by later reply activity, unlike ModifiedAtUtc (below).
    ///
    /// Otherwise (e.g. a comment freshly loaded from a source XLSX, where RootTextEditedAtUtc is
    /// intentionally left unset), fall back to inferring it from ModifiedAtUtc: unlike a reply
    /// (see ToThreadedCommentReplyElement), the root's ModifiedAtUtc is a thread-wide "last
    /// activity" value that also gets bumped by actions that never touch the root's own XML
    /// element or text -- e.g. AddThreadedCommentReplyCommand/DeleteThreadedCommentReplyCommand
    /// Touch the root purely because a *reply* (a separate &lt;threadedComment&gt; element with
    /// its own independent dT) was added/removed/edited; on load, GetThreadModifiedAt similarly
    /// folds reply activity into the root's ModifiedAtUtc so round-tripping preserves that same
    /// "last activity" reading. None of that reply-driven activity should ever move the ROOT
    /// element's own dT, matching real Excel (adding a reply never rewrites the root comment's
    /// dT). Only when ModifiedAtUtc has advanced past what mere reply activity already explains
    /// -- i.e. past what GetThreadModifiedAt would compute from just CreatedAtUtc plus the
    /// current replies -- has the root's own text or resolved state actually been edited, so only
    /// then should that newer ModifiedAtUtc win over the original CreatedAtUtc.
    /// </summary>
    private static DateTimeOffset? ResolveRootThreadedCommentTimestamp(ThreadedComment comment)
    {
        if (comment.RootTextEditedAtUtc is { } rootTextEditedAtUtc)
            return rootTextEditedAtUtc;

        if (comment.ModifiedAtUtc is not { } modifiedAtUtc)
            return comment.CreatedAtUtc;

        var replyExplainedCeiling = GetThreadModifiedAt(comment.CreatedAtUtc, comment.Replies);
        return replyExplainedCeiling is { } ceiling && modifiedAtUtc <= ceiling
            ? comment.CreatedAtUtc ?? modifiedAtUtc
            : modifiedAtUtc;
    }

    private static void SetDateTimeAttribute(XElement element, DateTimeOffset? timestampUtc)
    {
        if (timestampUtc is { } value)
            element.SetAttributeValue("dT", FormatDateTimeOffset(value));
    }

    private sealed record ParsedThreadedComment(
        uint? Row,
        uint? Col,
        string? Id,
        string? ParentId,
        string Text,
        string Author,
        DateTimeOffset? TimestampUtc,
        bool IsResolved,
        string? MentionsXml,
        string? SourcePersonId,
        IReadOnlyDictionary<string, string>? MentionedPersonDisplayNames);
}
