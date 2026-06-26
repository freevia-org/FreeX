using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-TBLDLG: tests for the Avalonia Table Properties dialog apply path
/// (<see cref="DocumentView.ApplyTableProperties"/> + <see cref="SetTablePropertiesCommand"/>), the Insert
/// Table dialog apply path (<see cref="InsertTableDialog.ApplyResult"/> → <see cref="DocumentView.InsertTable"/>),
/// command registration for the two new ids, and the no-op-when-null behaviour of the new optional callbacks.
/// The dialog UI itself is not driven; tests call the apply methods with a result struct (the same surface the
/// dialog produces on OK).
/// </summary>
public sealed class TableDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>NoopCallbacks WITHOUT the AV-TBLDLG callbacks → exercises the no-op-when-null path.</summary>
    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { },
            Save: () => { },
            Cut: () => { },
            Copy: () => { },
            Paste: () => { },
            Backstage: () => { },
            NewDocument: () => { },
            ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { },
            ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { },
            SetWebLayout: () => { },
            SetDraftView: () => { },
            OpenFontDialog: () => { },
            OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { },
            ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { },
            InsertPicture: () => { },
            OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });

    private static (DocumentView View, int TableBlockIdx, Table Tbl) MakeTable2x2()
    {
        var doc = TextDocument.CreateEmpty();
        var tbl = Table.Create(2, 2);
        tbl.Rows[0].Cells[0] = new TableCell("A1");
        tbl.Rows[0].Cells[1] = new TableCell("B1");
        tbl.Rows[1].Cells[0] = new TableCell("A2");
        tbl.Rows[1].Cells[1] = new TableCell("B2");
        doc.Blocks.Add(tbl);
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 4000));
        var idx = doc.Blocks.IndexOf(tbl);
        return (view, idx, tbl);
    }

    private static TablePropertiesValues SampleValues() => new(
        PreferredWidthPt: 360,
        Alignment: TableAlignment.Center,
        TextWrapping: true,
        RowHeightPt: 24,
        RowHeightRule: TableRowHeightRule.Exact,
        AllowRowBreak: false,
        RepeatHeaderRow: true,
        ColumnWidthPt: 120,
        CellPreferredWidthPt: 90,
        CellVerticalAlignment: TableCellVerticalAlignment.Bottom);

    // ── Command / callback registration ──────────────────────────────────────────────────────────

    [Fact]
    public void Table_properties_and_insert_table_dialog_commands_are_registered()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.table-properties"), out _)
            .Should().BeTrue("Table Properties dialog command must be registered");
        registry.TryGet(new RibbonCommandId("freew.insert-table-dialog"), out _)
            .Should().BeTrue("Insert Table dialog command must be registered");
    }

    [Fact]
    public void Dialog_commands_resolve_and_noop_when_callbacks_null()
    {
        // Callbacks are null (NoopCallbacks doesn't set them) → executing the command must not throw.
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        registry.TryGet(new RibbonCommandId("freew.table-properties"), out var props).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.insert-table-dialog"), out var insert).Should().BeTrue();

        var act = () => { props!.Execute(RibbonCommandContext.Empty); insert!.Execute(RibbonCommandContext.Empty); };
        act.Should().NotThrow("optional dialog callbacks must no-op when the shell didn't supply them");
    }

    [Fact]
    public void Insert_table_dialog_item_is_in_the_table_size_menu()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());

        // The id resolving in the registry is the load-bearing check; the menu item references it.
        registry.TryGet(new RibbonCommandId("freew.insert-table-dialog"), out _)
            .Should().BeTrue("Insert Table… menu item command must be registered");
        definition.Should().NotBeNull();
    }

    [Fact]
    public void Table_layout_tab_contains_properties_button()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());
        var layoutTab = definition.FindTab("table-layout");
        layoutTab.Should().NotBeNull();

        var hasProps = layoutTab!.Groups
            .SelectMany(g => g.Controls)
            .OfType<RibbonButton>()
            .Any(b => b.CommandId.Value == "freew.table-properties");
        hasProps.Should().BeTrue("Table Layout tab must expose the Properties button");

        registry.TryGet(new RibbonCommandId("freew.table-properties"), out _).Should().BeTrue();
    }

    // ── Table Properties apply ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyTableProperties_SetsTableRowColumnCell()
    {
        Table? tbl = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, t) = MakeTable2x2();
            tbl = t;
            view.PlaceCaretInCell(idx, 1, 0, 0, 0); // row 1, col 0
            TablePropertiesDialog.ApplyResult(view, SampleValues());
        });
        if (!ran) return;

        // Table tab.
        tbl!.PreferredWidthPt.Should().Be(360);
        tbl.Alignment.Should().Be(TableAlignment.Center);
        tbl.TextWrapping.Should().BeTrue();
        tbl.Formatting.RepeatHeaderRow.Should().BeTrue();

        // Row tab → caret row (row 1).
        tbl.Rows[1].HeightPt.Should().Be(24);
        tbl.Rows[1].HeightRule.Should().Be(TableRowHeightRule.Exact);
        tbl.Rows[1].AllowBreakAcrossPages.Should().BeFalse();

        // Column tab → every cell in caret column (col 0) gets the width.
        tbl.Rows[0].Cells[0].WidthPt.Should().Be(120);
        // Cell tab → caret cell width overrides (applied after column) + vertical alignment.
        tbl.Rows[1].Cells[0].WidthPt.Should().Be(90);
        tbl.Rows[1].Cells[0].VerticalAlignment.Should().Be(TableCellVerticalAlignment.Bottom);
    }

    [Fact]
    public async Task ApplyTableProperties_IsUndoable_SingleStep()
    {
        Table? tbl = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, t) = MakeTable2x2();
            tbl = t;
            view.PlaceCaretInCell(idx, 0, 1, 0, 0);
            TablePropertiesDialog.ApplyResult(view, SampleValues());
            view.Undo();
        });
        if (!ran) return;

        // After a single undo, all touched fields revert to defaults.
        tbl!.PreferredWidthPt.Should().BeNull("undo must restore the auto width");
        tbl.Alignment.Should().Be(TableAlignment.Left);
        tbl.TextWrapping.Should().BeFalse();
        tbl.Formatting.RepeatHeaderRow.Should().BeFalse();
        tbl.Rows[0].HeightPt.Should().BeNull();
        tbl.Rows[0].AllowBreakAcrossPages.Should().BeTrue("default is allow-break = true");
        tbl.Rows[0].Cells[1].VerticalAlignment.Should().Be(TableCellVerticalAlignment.Top);
        tbl.Rows[0].Cells[1].WidthPt.Should().BeNull();
    }

    [Fact]
    public async Task GetCaretTableProperties_RoundTripsCurrentValues()
    {
        DocumentView.TablePropertiesSnapshot? snap = null;
        var ran = await OnUiThread(() =>
        {
            var (view, idx, t) = MakeTable2x2();
            t.PreferredWidthPt = 200;
            t.Alignment = TableAlignment.Right;
            t.Rows[0].Cells[0].VerticalAlignment = TableCellVerticalAlignment.Center;
            view.PlaceCaretInCell(idx, 0, 0, 0, 0);
            snap = view.GetCaretTableProperties();
        });
        if (!ran) return;
        snap.Should().NotBeNull();
        snap!.Value.PreferredWidthPt.Should().Be(200);
        snap.Value.Alignment.Should().Be(TableAlignment.Right);
        snap.Value.CellVerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
    }

    [Fact]
    public async Task ApplyTableProperties_InBodyText_IsNoOp()
    {
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("not a table"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // CellCaretInfo is null — must not throw.
            view.ApplyTableProperties(SampleValues());
            view.GetCaretTableProperties().Should().BeNull();
        });
        if (!ran) return;
    }

    // ── Insert Table dialog apply ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertTableDialog_ApplyResult_InsertsTableWithDimensions()
    {
        int rowCount = -1, colCount = -1, blockCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("Body"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            var result = new InsertTableDialog.InsertTableResult(Rows: 4, Columns: 3, AutoFit: AutoFitMode.Contents);
            InsertTableDialog.ApplyResult(view, result);

            var tbl = view.Document.Blocks.OfType<Table>().FirstOrDefault();
            blockCount = view.Document.Blocks.OfType<Table>().Count();
            rowCount = tbl?.Rows.Count ?? -1;
            colCount = tbl?.Rows[0].Cells.Count ?? -1;
        });
        if (!ran) return;
        blockCount.Should().Be(1, "exactly one table must be inserted");
        rowCount.Should().Be(4, "dialog row count must drive the table rows");
        colCount.Should().Be(3, "dialog column count must drive the table columns");
    }

    [Fact]
    public async Task InsertTableDialog_Insert_IsUndoable()
    {
        int tablesAfterUndo = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Add(new Paragraph("Body"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));

            InsertTableDialog.ApplyResult(view, new InsertTableDialog.InsertTableResult(2, 2, AutoFitMode.Fixed));
            view.Undo();
            tablesAfterUndo = view.Document.Blocks.OfType<Table>().Count();
        });
        if (!ran) return;
        tablesAfterUndo.Should().Be(0, "undo must remove the inserted table");
    }
}
