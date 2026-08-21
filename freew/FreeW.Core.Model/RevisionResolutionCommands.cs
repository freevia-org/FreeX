namespace FreeW.Core.Model;

/// <summary>
/// Base command for undoable tracked-change resolution. The snapshot covers every structure
/// <see cref="TrackChanges"/> can mutate: top-level blocks, table rows, table-cell paragraphs,
/// paragraph marks and formatting, and run membership/marks/formatting. This includes nested tables.
/// </summary>
internal abstract class RevisionResolutionCommand : IDocumentCommand
{
    private IRevisionSnapshot? _snapshot;
    private int _estimatedBytes = 256;

    public abstract string Label { get; }
    public int EstimatedBytes => _estimatedBytes;
    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.Mixed;

    /// <summary>True immediately after this command resolved at least one pending revision.</summary>
    public bool WasResolved { get; private set; }

    protected abstract bool Resolve(TextDocument document);

    /// <summary>
    /// Single-entry commands only mutate one paragraph, so they can retain a small focused undo snapshot.
    /// Bulk commands return null and capture the complete body structures that <see cref="TrackChanges"/>
    /// may merge or remove.
    /// </summary>
    protected virtual Paragraph? TargetParagraph => null;

    public void Apply(IDocumentCommandContext context)
    {
        IRevisionSnapshot snapshot = TargetParagraph is { } paragraph
            ? ParagraphCommandSnapshot.Capture(paragraph)
            : DocumentSnapshot.Capture(context.Document);
        WasResolved = Resolve(context.Document);
        _snapshot = WasResolved ? snapshot : null;
        _estimatedBytes = WasResolved ? snapshot.EstimatedBytes : 256;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!WasResolved || _snapshot is null)
            return;

        _snapshot.Restore(context.Document);
        WasResolved = false;
    }

    /// <summary>
    /// Returns whether <paramref name="entry"/> still identifies a pending revision inside
    /// <paramref name="document"/>. Reviewing-pane entries are snapshots and may become stale.
    /// </summary>
    public static bool CanResolve(TextDocument document, RevisionEntry? entry)
    {
        // A null Run means entry describes a paragraph-mark revision (Paragraph.MarkRevision) rather than
        // a run. Resolving one can merge this paragraph's runs into the next paragraph in its container
        // (RevisionList.ResolveMarkRevision), which this command's TargetParagraph snapshot (one
        // paragraph's own runs/formatting/marks) is too narrow to undo correctly -- so this legacy
        // undoable-command path deliberately does not support it yet, exactly as it could not before
        // RevisionEntry.Run became nullable (there was no way to construct such an entry at all).
        if (entry is null || entry.Run is not { } run || !ContainsParagraph(document, entry.Paragraph) || !entry.Paragraph.Runs.Contains(run))
            return false;

        return entry.Kind switch
        {
            RevisionEntryKind.Insertion => run.Revision == RevisionKind.Inserted,
            RevisionEntryKind.Deletion => run.Revision == RevisionKind.Deleted,
            RevisionEntryKind.Formatting => run.FormatRevision is not null,
            _ => false
        };
    }

    private static bool ContainsParagraph(TextDocument document, Paragraph target)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph && ReferenceEquals(paragraph, target))
                return true;
            if (block is Table table && TableContainsParagraph(table, target))
                return true;
        }

        return false;
    }

    private static bool TableContainsParagraph(Table table, Paragraph target)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (cell.Paragraphs.Any(paragraph => ReferenceEquals(paragraph, target)))
                    return true;
                if (cell.NestedTables.Any(nested => TableContainsParagraph(nested, target)))
                    return true;
            }
        }

        return false;
    }

    private interface IRevisionSnapshot
    {
        int EstimatedBytes { get; }
        void Restore(TextDocument document);
    }

    private sealed class ParagraphCommandSnapshot : IRevisionSnapshot
    {
        private readonly ParagraphSnapshot _paragraph;

        private ParagraphCommandSnapshot(ParagraphSnapshot paragraph)
        {
            _paragraph = paragraph;
        }

        public int EstimatedBytes => _paragraph.EstimatedBytes;

        public static ParagraphCommandSnapshot Capture(Paragraph paragraph) =>
            new(ParagraphSnapshot.Capture(paragraph));

        public void Restore(TextDocument document) => _paragraph.Restore();
    }

    private sealed class DocumentSnapshot : IRevisionSnapshot
    {
        private readonly List<Block> _blocks;
        private readonly List<ParagraphSnapshot> _paragraphs;
        private readonly List<TableSnapshot> _tables;

        private DocumentSnapshot(
            List<Block> blocks,
            List<ParagraphSnapshot> paragraphs,
            List<TableSnapshot> tables,
            int estimatedBytes)
        {
            _blocks = blocks;
            _paragraphs = paragraphs;
            _tables = tables;
            EstimatedBytes = estimatedBytes;
        }

        public int EstimatedBytes { get; }

        public static DocumentSnapshot Capture(TextDocument document)
        {
            var blocks = document.Blocks.ToList();
            var paragraphs = new List<ParagraphSnapshot>();
            var tables = new List<TableSnapshot>();
            var estimatedBytes = 128 + blocks.Count * 16;

            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        var paragraphSnapshot = ParagraphSnapshot.Capture(paragraph);
                        paragraphs.Add(paragraphSnapshot);
                        estimatedBytes += paragraphSnapshot.EstimatedBytes;
                        break;
                    case Table table:
                        var tableSnapshot = TableSnapshot.Capture(table);
                        tables.Add(tableSnapshot);
                        estimatedBytes += tableSnapshot.EstimatedBytes;
                        break;
                }
            }

            return new DocumentSnapshot(blocks, paragraphs, tables, estimatedBytes);
        }

        public void Restore(TextDocument document)
        {
            foreach (var paragraph in _paragraphs)
                paragraph.Restore();
            foreach (var table in _tables)
                table.Restore();

            document.Blocks.Clear();
            document.Blocks.AddRange(_blocks);
        }
    }

    private sealed class TableSnapshot
    {
        private readonly Table _table;
        private readonly List<TableRow> _rows;
        private readonly List<RowSnapshot> _rowSnapshots;

        private TableSnapshot(Table table, List<TableRow> rows, List<RowSnapshot> rowSnapshots, int estimatedBytes)
        {
            _table = table;
            _rows = rows;
            _rowSnapshots = rowSnapshots;
            EstimatedBytes = estimatedBytes;
        }

        public int EstimatedBytes { get; }

        public static TableSnapshot Capture(Table table)
        {
            var rows = table.Rows.ToList();
            var rowSnapshots = rows.Select(RowSnapshot.Capture).ToList();
            return new TableSnapshot(table, rows, rowSnapshots, 128 + rowSnapshots.Sum(row => row.EstimatedBytes));
        }

        public void Restore()
        {
            foreach (var row in _rowSnapshots)
                row.Restore();

            _table.Rows.Clear();
            _table.Rows.AddRange(_rows);
        }
    }

    private sealed class RowSnapshot
    {
        private readonly TableRow _row;
        private readonly RevisionKind _revision;
        private readonly string? _author;
        private readonly string? _dateXml;
        private readonly List<CellSnapshot> _cells;

        private RowSnapshot(
            TableRow row,
            RevisionKind revision,
            string? author,
            string? dateXml,
            List<CellSnapshot> cells,
            int estimatedBytes)
        {
            _row = row;
            _revision = revision;
            _author = author;
            _dateXml = dateXml;
            _cells = cells;
            EstimatedBytes = estimatedBytes;
        }

        public int EstimatedBytes { get; }

        public static RowSnapshot Capture(TableRow row)
        {
            var cells = row.Cells.Select(CellSnapshot.Capture).ToList();
            return new RowSnapshot(
                row,
                row.RowRevision,
                row.RowRevisionAuthor,
                row.RowRevisionDateXml,
                cells,
                128 + cells.Sum(cell => cell.EstimatedBytes));
        }

        public void Restore()
        {
            _row.RowRevision = _revision;
            _row.RowRevisionAuthor = _author;
            _row.RowRevisionDateXml = _dateXml;
            foreach (var cell in _cells)
                cell.Restore();
        }
    }

    private sealed class CellSnapshot
    {
        private readonly TableCell _cell;
        private readonly List<Paragraph> _paragraphs;
        private readonly List<ParagraphSnapshot> _paragraphSnapshots;
        private readonly List<TableSnapshot> _nestedTables;

        private CellSnapshot(
            TableCell cell,
            List<Paragraph> paragraphs,
            List<ParagraphSnapshot> paragraphSnapshots,
            List<TableSnapshot> nestedTables,
            int estimatedBytes)
        {
            _cell = cell;
            _paragraphs = paragraphs;
            _paragraphSnapshots = paragraphSnapshots;
            _nestedTables = nestedTables;
            EstimatedBytes = estimatedBytes;
        }

        public int EstimatedBytes { get; }

        public static CellSnapshot Capture(TableCell cell)
        {
            var paragraphs = cell.Paragraphs.ToList();
            var paragraphSnapshots = paragraphs.Select(ParagraphSnapshot.Capture).ToList();
            var nestedTables = cell.NestedTables.Select(TableSnapshot.Capture).ToList();
            return new CellSnapshot(
                cell,
                paragraphs,
                paragraphSnapshots,
                nestedTables,
                128 + paragraphSnapshots.Sum(paragraph => paragraph.EstimatedBytes)
                    + nestedTables.Sum(table => table.EstimatedBytes));
        }

        public void Restore()
        {
            foreach (var paragraph in _paragraphSnapshots)
                paragraph.Restore();
            foreach (var table in _nestedTables)
                table.Restore();

            _cell.Paragraphs.Clear();
            _cell.Paragraphs.AddRange(_paragraphs);
        }
    }

    private sealed class ParagraphSnapshot
    {
        private readonly Paragraph _paragraph;
        private readonly List<Run> _runs;
        private readonly List<RunSnapshot> _runSnapshots;
        private readonly ParagraphFormatting _formatting;
        private readonly ParagraphFormatRevision? _formatRevision;
        private readonly RevisionKind _markRevision;
        private readonly string? _markAuthor;
        private readonly string? _markDateXml;

        private ParagraphSnapshot(
            Paragraph paragraph,
            List<Run> runs,
            List<RunSnapshot> runSnapshots,
            ParagraphFormatting formatting,
            ParagraphFormatRevision? formatRevision,
            RevisionKind markRevision,
            string? markAuthor,
            string? markDateXml,
            int estimatedBytes)
        {
            _paragraph = paragraph;
            _runs = runs;
            _runSnapshots = runSnapshots;
            _formatting = formatting;
            _formatRevision = formatRevision;
            _markRevision = markRevision;
            _markAuthor = markAuthor;
            _markDateXml = markDateXml;
            EstimatedBytes = estimatedBytes;
        }

        public int EstimatedBytes { get; }

        public static ParagraphSnapshot Capture(Paragraph paragraph)
        {
            var runs = paragraph.Runs.ToList();
            var runSnapshots = runs.Select(RunSnapshot.Capture).ToList();
            return new ParagraphSnapshot(
                paragraph,
                runs,
                runSnapshots,
                paragraph.Formatting,
                paragraph.ParagraphFormatRevision,
                paragraph.MarkRevision,
                paragraph.MarkRevisionAuthor,
                paragraph.MarkRevisionDateXml,
                128 + runSnapshots.Sum(run => run.EstimatedBytes));
        }

        public void Restore()
        {
            foreach (var run in _runSnapshots)
                run.Restore();

            _paragraph.Runs.Clear();
            _paragraph.Runs.AddRange(_runs);
            _paragraph.Formatting = _formatting;
            _paragraph.ParagraphFormatRevision = _formatRevision;
            _paragraph.MarkRevision = _markRevision;
            _paragraph.MarkRevisionAuthor = _markAuthor;
            _paragraph.MarkRevisionDateXml = _markDateXml;
        }
    }

    private sealed record RunSnapshot(
        Run Run,
        RevisionKind Revision,
        string? Author,
        string? DateXml,
        FormatRevision? FormatRevision,
        RunFormatting Formatting,
        int EstimatedBytes)
    {
        public static RunSnapshot Capture(Run run) => new(
            run,
            run.Revision,
            run.RevisionAuthor,
            run.RevisionDateXml,
            run.FormatRevision,
            run.Formatting,
            128 + run.Text.Length * sizeof(char));

        public void Restore()
        {
            Run.Revision = Revision;
            Run.RevisionAuthor = Author;
            Run.RevisionDateXml = DateXml;
            Run.FormatRevision = FormatRevision;
            Run.Formatting = Formatting;
        }
    }
}

internal sealed class AcceptAllRevisionsCommand : RevisionResolutionCommand
{
    public override string Label => "Accept All Changes";

    protected override bool Resolve(TextDocument document)
    {
        if (!TrackChanges.HasRevisions(document))
            return false;
        TrackChanges.AcceptAll(document);
        return true;
    }
}

internal sealed class RejectAllRevisionsCommand : RevisionResolutionCommand
{
    public override string Label => "Reject All Changes";

    protected override bool Resolve(TextDocument document)
    {
        if (!TrackChanges.HasRevisions(document))
            return false;
        TrackChanges.RejectAll(document);
        return true;
    }
}

internal sealed class AcceptRevisionCommand(RevisionEntry entry) : RevisionResolutionCommand
{
    public override string Label => "Accept Change";

    protected override Paragraph? TargetParagraph => entry.Paragraph;

    protected override bool Resolve(TextDocument document) =>
        CanResolve(document, entry) && RevisionList.Accept(document, entry);
}

internal sealed class RejectRevisionCommand(RevisionEntry entry) : RevisionResolutionCommand
{
    public override string Label => "Reject Change";

    protected override Paragraph? TargetParagraph => entry.Paragraph;

    protected override bool Resolve(TextDocument document) =>
        CanResolve(document, entry) && RevisionList.Reject(document, entry);
}

/// <summary>
/// Shared entry point used by both renderers. It prevents stale/no-op actions from entering undo history
/// and guarantees the same command labels, mutation classification and undo semantics on both hosts.
/// </summary>
public static class RevisionResolutionCoordinator
{
    public static bool Accept(DocumentCommandBus commandBus, TextDocument document, RevisionEntry? entry) =>
        ExecuteOne(commandBus, document, entry, accept: true);

    public static bool Reject(DocumentCommandBus commandBus, TextDocument document, RevisionEntry? entry) =>
        ExecuteOne(commandBus, document, entry, accept: false);

    public static bool AcceptAll(DocumentCommandBus commandBus, TextDocument document) =>
        ExecuteAll(commandBus, document, accept: true);

    public static bool RejectAll(DocumentCommandBus commandBus, TextDocument document) =>
        ExecuteAll(commandBus, document, accept: false);

    private static bool ExecuteOne(
        DocumentCommandBus commandBus,
        TextDocument document,
        RevisionEntry? entry,
        bool accept)
    {
        if (!RevisionResolutionCommand.CanResolve(document, entry))
            return false;

        RevisionResolutionCommand command = accept
            ? new AcceptRevisionCommand(entry!)
            : new RejectRevisionCommand(entry!);
        commandBus.Execute(command);
        return command.WasResolved;
    }

    private static bool ExecuteAll(DocumentCommandBus commandBus, TextDocument document, bool accept)
    {
        if (!TrackChanges.HasRevisions(document))
            return false;

        RevisionResolutionCommand command = accept
            ? new AcceptAllRevisionsCommand()
            : new RejectAllRevisionsCommand();
        commandBus.Execute(command);
        return command.WasResolved;
    }
}
