using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Extends native list-marker sequencing (<see cref="DocumentListMarkerSequencePlanner"/>) to paragraphs
/// inside table cells. A Number/Bullet/MultiLevel paragraph in a table cell is mapped by
/// <c>DocxReader.ReadParagraph</c> exactly like a body paragraph (same <c>numbering</c> map — see
/// <c>DocxReader.cs</c> around <c>cell.Paragraphs.Add(ReadParagraph(...))</c>), but the body renderers only
/// ever walk <c>document.Blocks</c> at the top level, so a cell paragraph's marker was never computed and
/// the glyph silently disappeared even though the paragraph's indentation (driven by ordinary paragraph
/// formatting, not by this planner) still rendered correctly.
/// <para>
/// <see cref="Build"/> replays the WHOLE document — body paragraphs and table-cell paragraphs alike — in
/// document order through one running <see cref="DocumentListMarkerSequencePlanner"/>, exactly mirroring
/// how the body renderer's own live sequence advances. That is what keeps numbering continuity intact
/// across a body → table → body run: a list that starts in the body, continues inside a table cell, and
/// resumes afterward in the body must not restart at the table boundary, while two genuinely independent
/// list instances (different numIds — surfaced by the reader as an explicit
/// <see cref="ParagraphFormatting.ListStartOverride"/>, see <c>NumberingRestartState</c> in
/// <c>DocxReader.cs</c>) still restart exactly as they would if both were body paragraphs. Only the
/// table-cell paragraphs are returned: body paragraphs already compute their own marker live in the
/// renderers' main loop, and are advanced here (without being stored) purely to keep the running counters
/// positioned correctly for any table-cell paragraph that follows them.
/// </para>
/// <para>
/// This is a pure, stateless function of <paramref name="document"/> — nothing here retains counter state
/// across calls — so callers may recompute it freely (e.g. once per pagination page-segment when a table
/// repeats its header row across pages) without double-counting: the same document always replays to the
/// same result. Renderers that build a table more than once per <c>Render()</c> pass (paginated tables,
/// repeated header rows) rely on this — see <c>PreservedNumberingMarkerPlanner.BuildByParagraph</c>, which
/// the same call sites already recompute the same way.
/// </para>
/// <para>
/// Shared by the WPF and Avalonia document renderers so both shells produce identical markers and
/// numbering continuity; a caller driving its own live body-loop sequence (as both hosts' <c>Render()</c>
/// methods do) must additionally replay a table's cell paragraphs through that SAME live sequence when the
/// table block is encountered, so the body counter used for anything after the table starts from the
/// right value — see each host's <c>Render()</c> for that synchronization step.
/// </para>
/// </summary>
public static class TableCellListMarkerPlanner
{
    public static IReadOnlyDictionary<Paragraph, DocumentListMarkerPlan> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sequence = new DocumentListMarkerSequencePlanner(
            document.MultiLevelList.NumberFormats,
            document.MultiLevelList.LevelTexts);
        var result = new Dictionary<Paragraph, DocumentListMarkerPlan>();
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    if (paragraph.Formatting.ListKind != ListKind.None)
                        sequence.Advance(paragraph);
                    break;

                case Table table:
                    foreach (var cell in table.Rows.SelectMany(row => row.Cells))
                    foreach (var cellParagraph in cell.Paragraphs)
                    {
                        if (cellParagraph.Formatting.ListKind == ListKind.None)
                            continue;

                        result[cellParagraph] = sequence.Advance(cellParagraph);
                    }
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Advances <paramref name="sequence"/> (the body renderer's own live counter) through every
    /// Number/Bullet/MultiLevel paragraph in <paramref name="table"/>'s cells, in the same row/cell/
    /// paragraph order <see cref="Build"/> uses, WITHOUT recording markers. Call this when a body render
    /// loop encounters a table block so its live sequence — used for the body paragraphs that follow —
    /// stays positioned exactly where the table's own (freshly-recomputed) markers left it.
    /// </summary>
    public static void AdvanceThroughTable(Table table, DocumentListMarkerSequencePlanner sequence)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(sequence);

        foreach (var cell in table.Rows.SelectMany(row => row.Cells))
        foreach (var cellParagraph in cell.Paragraphs)
        {
            if (cellParagraph.Formatting.ListKind != ListKind.None)
                sequence.Advance(cellParagraph);
        }
    }
}
