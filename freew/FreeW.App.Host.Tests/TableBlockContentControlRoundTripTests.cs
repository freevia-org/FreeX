using System.Linq;
using System.Reflection;
using System.Windows.Documents;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 162 remediation (freew-content-controls F1): a body <c>w:sdt</c> wrapping a whole
/// <see cref="System.Windows.Documents.Table"/> -- a Group / RepeatingSection / BuildingBlockGallery /
/// DocumentPart / Bibliography region around a <c>w:tbl</c>, carried on <see cref="Table.BlockContentControl"/>
/// (declared on the abstract <see cref="Block"/> base so both <see cref="Paragraph"/> and <see cref="Table"/>
/// carry it, and populated by the reader -- see <c>DocxReader</c>'s
/// <c>table.BlockContentControl = inheritedBlockContentControl</c>) -- had no Tag-borne round-trip through
/// the WPF host's view surface at all:
/// <list type="bullet">
/// <item><c>DocumentView.BuildTable</c> never stashed it on the <c>WpfTableTag</c> it puts on the rendered
/// <see cref="System.Windows.Documents.Table"/>'s Tag, and <c>DocumentView.ReadTable</c> never read it back,
/// so opening such a docx and pressing Ctrl+S with zero edits silently dropped the wrapper on save --
/// <c>FileCommands.PrepareDocumentAsync</c> calls <c>DocumentView.CommitToModel</c> (which drives
/// <c>ReadTable</c>) before every save;</item>
/// <item><c>DocumentView.SelectionRemovesDeleteLockedContentControl</c> (the choke point
/// <c>TryPrepareNativeFallback</c> consults before every Backspace/Delete) only ever branched on
/// <c>block is WpfParagraph</c> before checking a block-level control, so a delete-locked
/// (<see cref="ContentControlLockMode.ControlLocked"/>/<see cref="ContentControlLockMode.ControlAndContentLocked"/>)
/// wrapper around a table provided zero delete protection: selecting across the table and pressing Delete
/// removed it outright.</item>
/// </list>
/// Both are proven end-to-end here: the first via the real public <c>LoadModel</c>/<c>CommitToModel</c>
/// pair (same style as <see cref="DocumentViewRoundTripTests"/>), the second via the real
/// <c>TryPrepareNativeFallback</c> choke point (reflection-invoked, matching
/// <see cref="ContentControlKeyboardLockTests"/> and <see cref="BlockContentControlKeyboardLockTests"/> --
/// this headless host cannot make real OS keyboard dispatch deterministic).
///
/// <para>
/// The delete-lock tests select the WHOLE DOCUMENT (a neighboring paragraph on each side of the table,
/// selected end-to-end -- the natural shape of a Ctrl+A / "select all" gesture), not
/// <c>table.ContentStart</c>/<c>ContentEnd</c> directly: WPF's <c>TextSelection.Select</c> normalizes an
/// anchor placed EXACTLY at an element's own content boundary to a position slightly inside it, so a
/// selection with no slack on either side of the target block never satisfies
/// <c>SelectionRemovesDeleteLockedContentControl</c>'s <c>CoveredWhole</c> containment check -- a
/// pre-existing characteristic of the (unmodified) <c>WpfParagraph</c> branch just as much as the new
/// <c>WpfTable</c> branch (verified directly: the same self-bounded selection fails to trip the existing
/// paragraph branch too). A selection that extends into neighboring content, as any real drag-select or
/// select-all does, carries exactly the slack <c>CoveredWhole</c> needs.
/// </para>
/// </summary>
public sealed class TableBlockContentControlRoundTripTests
{
    private static readonly MethodInfo TryPrepareNativeFallbackMethod =
        typeof(DocumentView).GetMethod("TryPrepareNativeFallback", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            "DocumentView.TryPrepareNativeFallback not found -- the choke point this test targets was renamed or removed.");

    private static bool TryPrepareNativeFallbackAllowed(DocumentView view)
    {
        var args = new object?[] { null };
        return (bool)TryPrepareNativeFallbackMethod.Invoke(view, args)!;
    }

    private static DocumentView LoadWithTable(BlockContentControl? blockContentControl)
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("Cell"));
        table.BlockContentControl = blockContentControl;

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    /// <summary>
    /// Loads Before-paragraph / Table(with the given control) / After-paragraph and selects the whole
    /// document end-to-end -- a natural Ctrl+A/select-all shape that carries real slack on both sides of
    /// the table, exactly as <see cref="DocumentView.SelectionRemovesDeleteLockedContentControl"/>'s
    /// <c>CoveredWhole</c> containment check requires (see the class doc comment).
    /// </summary>
    private static DocumentView LoadWithTableSelectedAcross(BlockContentControl? blockContentControl)
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs.Clear();
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("Cell"));
        table.BlockContentControl = blockContentControl;

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Before"));
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph("After"));

        var view = new DocumentView();
        view.LoadModel(document);
        view.Selection.Select(view.Document.ContentStart, view.Document.ContentEnd);
        return view;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Save round-trip: BuildTable -> ReadTable via LoadModel -> CommitToModel, exactly what
    // FileCommands.PrepareDocumentAsync drives on every save.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void CommitToModel_PreservesGroupContentControlWrappingWholeTable()
    {
        var group = BlockContentControl.GroupRegion(tag: "TeamRoster", alias: "Team Roster");
        var view = LoadWithTable(group);

        view.CommitToModel();

        var recovered = view.Model.Blocks.OfType<Table>().Single();
        recovered.BlockContentControl.Should().Be(group,
            "a body-level w:sdt wrapping the whole table must survive an edit-triggered (or Ctrl+S-with-" +
            "no-edits-triggered) view->model round-trip, exactly like Table.Borders/TableStyleId already do");
    }

    [StaFact]
    public void CommitToModel_PreservesRepeatingSectionAroundTable_WithNoEditsAtAll()
    {
        // The exact user gesture from the finding: open a docx whose table sits inside a repeating-section
        // content control, then press Ctrl+S with zero edits. FileCommands.PrepareDocumentAsync's save path
        // calls CommitToModel unconditionally, so this must round-trip even with no interaction at all.
        var repeating = BlockContentControl.RepeatingSection(title: "Roster Row");
        var view = LoadWithTable(repeating);

        view.CommitToModel();

        var recovered = view.Model.Blocks.OfType<Table>().Single();
        recovered.BlockContentControl.Should().Be(repeating);
    }

    [StaFact]
    public void CommitToModel_LeavesUnwrappedTableAlone()
    {
        // Sibling/regression guard: a table with no body-level content control at all must still commit
        // with a null BlockContentControl -- the new Tag plumbing must not manufacture one from nothing.
        var view = LoadWithTable(blockContentControl: null);

        view.CommitToModel();

        var recovered = view.Model.Blocks.OfType<Table>().Single();
        recovered.BlockContentControl.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Delete-lock enforcement: SelectionRemovesDeleteLockedContentControl, consulted by
    // TryPrepareNativeFallback before every Backspace/Delete fallback to native RichTextBox editing.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void TryPrepareNativeFallback_BlocksDeletingAControlLockedTable()
    {
        var locked = new BlockContentControl(BlockContentControlKind.Group, LockMode: ContentControlLockMode.ControlLocked);
        var view = LoadWithTableSelectedAcross(locked);

        TryPrepareNativeFallbackAllowed(view).Should().BeFalse(
            "Word's sdtLocked protects the control's existence, so a selection spanning the whole table " +
            "must be refused exactly as it already is for a delete-locked run or paragraph");
    }

    [StaFact]
    public void TryPrepareNativeFallback_AllowsDeletingAnUnlockedTable()
    {
        // Sibling/regression guard: an ordinary table (or one wrapped in a control with no lock) must stay
        // deletable -- the new guard branch must not over-block plain tables.
        var unlocked = new BlockContentControl(BlockContentControlKind.Group, LockMode: ContentControlLockMode.NotSpecified);
        var view = LoadWithTableSelectedAcross(unlocked);

        TryPrepareNativeFallbackAllowed(view).Should().BeTrue();
    }

    [StaFact]
    public void TryPrepareNativeFallback_AllowsDeletingATableWithNoContentControlAtAll()
    {
        // Sibling/regression guard: the overwhelming majority of tables carry no BlockContentControl at
        // all, and this must remain a complete no-op for them.
        var view = LoadWithTableSelectedAcross(blockContentControl: null);

        TryPrepareNativeFallbackAllowed(view).Should().BeTrue();
    }
}
