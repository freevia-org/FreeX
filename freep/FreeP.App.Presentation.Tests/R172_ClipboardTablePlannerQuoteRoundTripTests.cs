namespace FreeP.App.Compositor.Tests;

/// <summary>
/// R172 F1: FreeX's plain-text clipboard serializer (ClipboardSerializer.AppendTsvCell /
/// RequiresTsvQuoting in FreeX.Core.Commands) wraps a cell's text in double quotes -- doubling any
/// internal quote -- whenever the text contains a tab, a CR, an LF, or a quote. ClipboardTablePlanner
/// .SplitCells must reverse that exact encoding when it splits a pasted row into cells, the same way
/// ClipboardSerializer.Deserialize / IsProperlyQuotedField already reverses it for a FreeX-to-FreeX
/// paste. These tests build the literal text FreeX's serializer would place on the clipboard (via
/// <see cref="SerializeFreeXField"/>, a byte-for-byte copy of AppendTsvCell/RequiresTsvQuoting) and
/// push it through the real FreeP production path, then assert the recovered cell text matches the
/// original -- not merely that a table was produced.
/// </summary>
public sealed class R172_ClipboardTablePlannerQuoteRoundTripTests
{
    // Exact copy of FreeX.Core.Commands.ClipboardSerializer.RequiresTsvQuoting / AppendTsvCell
    // (src/FreeX.Core.Commands/ClipboardSerializer.cs), so the text handed to FreeP here is
    // byte-for-byte what a real FreeX range copy would put on the clipboard.
    private static bool RequiresTsvQuoting(string text) =>
        text.Any(ch => ch is '\t' or '\r' or '\n' or '"');

    private static string SerializeFreeXField(string text) =>
        RequiresTsvQuoting(text) ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

    private static string SerializeFreeXRow(params string[] cells) =>
        string.Join('\t', cells.Select(SerializeFreeXField));

    private static string SerializeFreeXGrid(params string[][] rows) =>
        string.Join("\r\n", rows.Select(SerializeFreeXRow));

    // Mirrors PresentationClipboardWorkflow.BuildPlainTextBody (freep/FreeP.App.Presentation/
    // Core/PresentationClipboardWorkflow.cs), reusing the actual production row splitter
    // (PresentationClipboardContent.SplitTabularRows) rather than reimplementing it.
    private static TextBody BuildBodyFromFreeXText(string text)
    {
        var body = new TextBody();
        foreach (var line in PresentationClipboardContent.SplitTabularRows(text))
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = line });
            body.Paragraphs.Add(paragraph);
        }

        return body;
    }

    private static string CellText(TableShape table, int row, int col) =>
        string.Concat(table.Rows[row].Cells[col].TextBody!.Paragraphs
            .SelectMany(p => p.Runs)
            .Select(r => r.Text));

    private static (EditingSession Editor, Slide Slide) CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        return (new EditingSession(presentation, new PresentationCommandBus(presentation)), slide);
    }

    [Fact]
    public void SerializeFreeXField_MatchesDocumentedFreeXQuotingRule()
    {
        // Sanity-check the test's own copy of FreeX's algorithm against the documented behaviour
        // before relying on it below.
        SerializeFreeXField("plain").Should().Be("plain");
        SerializeFreeXField("He said \"hi\"").Should().Be("\"He said \"\"hi\"\"\"");
        SerializeFreeXField("line1\nline2").Should().Be("\"line1\nline2\"");
        SerializeFreeXField("a\tb").Should().Be("\"a\tb\"");
    }

    /// <summary>
    /// Full production round trip through PresentationClipboardWorkflow.ApplyPaste (the real
    /// FreeX-&gt;FreeP paste call site) for a cell containing a double quote. Before the fix this
    /// asserted "He said ""hi""" (the raw quoted CSV field); after the fix it must be the user's
    /// original text with the wrapping/doubling reversed.
    /// </summary>
    [Fact]
    public void ApplyPaste_CellWithQuotes_RecoversOriginalTextNotRawCsv()
    {
        var (editor, slide) = CreateEditor();
        var text = SerializeFreeXGrid(
            ["Region", "Notes"],
            ["North", "He said \"hi\""],
            ["South", "plain"]);
        var content = new PresentationClipboardContent(Text: text);

        PresentationClipboardWorkflow.ApplyPaste(
            PresentationClipboardWorkflow.PreparePaste(editor),
            content,
            ownCopyIsCurrent: false);

        var table = slide.Shapes[^1].Table;
        table.Should().NotBeNull();
        CellText(table!, 1, 1).Should().Be("He said \"hi\"");
    }

    /// <summary>
    /// Full production round trip for an Alt+Enter wrapped cell (the r169 regression test's exact
    /// payload), now asserting the recovered cell text rather than only the shape kind -- r169's own
    /// test (ApplyPaste_RangeCopyWithAWrappedCell_StillCreatesATable) never inspected the text and so
    /// kept passing while it was corrupted.
    /// </summary>
    [Fact]
    public void ApplyPaste_WrappedCell_RecoversEmbeddedNewlineWithoutQuoteCharacters()
    {
        var (editor, slide) = CreateEditor();
        var text = SerializeFreeXGrid(
            ["Region", "Notes"],
            ["North", "line1\nline2"],
            ["South", "plain"]);
        var content = new PresentationClipboardContent(Text: text);

        PresentationClipboardWorkflow.ApplyPaste(
            PresentationClipboardWorkflow.PreparePaste(editor),
            content,
            ownCopyIsCurrent: false);

        var table = slide.Shapes[^1].Table;
        table.Should().NotBeNull();
        CellText(table!, 1, 1).Should().Be("line1\nline2");
        CellText(table!, 2, 1).Should().Be("plain");
    }

    /// <summary>
    /// Cell-splitting round trip for a cell containing a literal tab. FreeX quotes such a field
    /// (RequiresTsvQuoting treats tab like quote/CR/LF), so the internal tab must not be treated as a
    /// column boundary. Exercises ClipboardTablePlanner.TryBuildStandaloneTable directly against a
    /// body built the same way BuildPlainTextBody builds one in production, since the separate
    /// row-shape heuristic in PresentationClipboardContent.HasTabularText (a different file, not part
    /// of this finding) rejects a raw embedded tab before the table is even attempted -- a pre-existing
    /// detection gap distinct from the construction bug this finding is about.
    /// </summary>
    [Fact]
    public void SplitCells_CellWithEmbeddedTab_KeepsTabAsCellContentNotColumnBoundary()
    {
        var text = SerializeFreeXGrid(
            ["Region", "Notes"],
            ["North", "B\tC"],
            ["South", "plain"]);

        var body = BuildBodyFromFreeXText(text);
        ClipboardTablePlanner.TryBuildStandaloneTable(body, null, null, out var table).Should().BeTrue();

        table.Rows.Should().HaveCount(3);
        table.Rows[0].Cells.Should().HaveCount(2);
        CellText(table, 1, 1).Should().Be("B\tC");
        CellText(table, 2, 1).Should().Be("plain");
    }

    /// <summary>
    /// Same as above, for a cell whose text combines a tab, a quote, and a newline -- the worst case
    /// named by the round-172 directive. FreeX quotes the whole field once and doubles only the
    /// internal quote; the recovered cell text must match the original exactly.
    /// </summary>
    [Fact]
    public void SplitCells_CellWithTabQuoteAndNewline_RecoversExactOriginalText()
    {
        const string original = "a\tb\"c\nd";
        var text = SerializeFreeXGrid(
            ["Region", "Notes"],
            ["North", original],
            ["South", "plain"]);

        var body = BuildBodyFromFreeXText(text);
        ClipboardTablePlanner.TryBuildStandaloneTable(body, null, null, out var table).Should().BeTrue();

        table.Rows.Should().HaveCount(3);
        CellText(table, 1, 1).Should().Be(original);
        CellText(table, 2, 1).Should().Be("plain");
    }

    /// <summary>
    /// Sibling no-regression: ClipboardTablePlanner.SplitCells also parses paragraphs built by the
    /// RTF/XAML rich-clipboard table projection (ExternalRichTextClipboardPlanner /
    /// ExternalXamlClipboardPlanner via EditingSession.InsertTableFromClipboard), whose cell text is
    /// literal user content that was never CSV-quoted -- a Word/Excel-table cell that happens to
    /// contain a literal quote mark must NOT have it stripped just because it sits at a field
    /// boundary. IsProperlyQuotedCell must reject this (no genuine closing-quote-then-tab/end
    /// pattern immediately follows), leaving the mark alone as data.
    /// </summary>
    [Fact]
    public void SplitCells_RichTextCellWithLiteralLeadingQuote_LeavesQuoteCharacterAlone()
    {
        var source = new Paragraph();
        source.Runs.Add(new Run { Text = "\"Quoted\" Header" });
        source.Runs.Add(new Run { Text = "\t" });
        source.Runs.Add(new Run { Text = "Value" });
        var body = new TextBody();
        body.Paragraphs.Add(source);
        // Second row so column-count detection (>= 2 columns, >= 1 row already, need >=1 more row is
        // not required by TryBuildStandaloneTable itself -- only columnCount < 2 is rejected) is moot;
        // add a plain second row anyway to mirror a realistic two-row rich-text table paste.
        var second = new Paragraph();
        second.Runs.Add(new Run { Text = "Plain\tRow" });
        body.Paragraphs.Add(second);

        ClipboardTablePlanner.TryBuildStandaloneTable(body, null, null, out var table).Should().BeTrue();

        CellText(table, 0, 0).Should().Be("\"Quoted\" Header",
            "a literal quote typed into a Word/rich-text table cell is not FreeX CSV syntax and must survive verbatim");
        CellText(table, 0, 1).Should().Be("Value");
    }
}
