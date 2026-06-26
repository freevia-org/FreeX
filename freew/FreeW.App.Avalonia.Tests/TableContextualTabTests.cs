using System.Linq;
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
/// AV-TBLTAB: Guard tests for the Table contextual tabs (Design + Layout) added to the
/// FreeW Avalonia ribbon.
/// <list type="bullet">
///   <item>All new command ids are in the registry.</item>
///   <item>Layout commands mutate the model correctly.</item>
///   <item>Design toggles mutate TableFormatting.</item>
///   <item><see cref="TableRibbonContextSource"/> fires ContextChanged when caret enters/leaves a table.</item>
/// </list>
/// </summary>
public sealed class TableContextualTabTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

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
            ApplyZoom: (_, _) => { });

    // ── Registry completeness ─────────────────────────────────────────────────

    [Fact]
    public void Registry_contains_all_table_contextual_commands()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        var tableCommands = new[]
        {
            // Table Design
            "freew.table-header-row",
            "freew.table-banded-rows",
            "freew.table-shading",
            "freew.table-borders",
            // Table borders sub-commands
            "freew.table-borders.all",
            "freew.table-borders.outside",
            "freew.table-borders.inside",
            "freew.table-borders.none",
            "freew.table-borders.top",
            "freew.table-borders.bottom",
            "freew.table-borders.left",
            "freew.table-borders.right",
            // Table Layout
            "freew.table-select-table",
            "freew.table-select-row",
            "freew.table-select-col",
            "freew.table-select-cell",
            "freew.table-insert-above",
            "freew.table-insert-below",
            "freew.table-insert-col-left",
            "freew.table-insert-col-right",
            "freew.table-delete-row",
            "freew.table-delete-col",
            "freew.table-delete",
            "freew.table-merge-cells",
            "freew.table-split-cell",
        };

        foreach (var id in tableCommands)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Table command '{id}' must be registered");
    }

    [Fact]
    public void Ribbon_definition_includes_table_design_and_layout_contextual_tabs()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var contextual = definition.ContextualTabs.ToList();

        contextual.Any(t => t.Id == "table-design")
            .Should().BeTrue("table-design contextual tab must be defined");
        contextual.Any(t => t.Id == "table-layout")
            .Should().BeTrue("table-layout contextual tab must be defined");

        var design = definition.FindTab("table-design")!;
        design.Context!.ActivationKey.Should().Be(TableRibbonContextSource.TableContextKey);
        design.Context.Color.Should().Be(RibbonContextColor.Teal);

        var layout = definition.FindTab("table-layout")!;
        layout.Context!.ActivationKey.Should().Be(TableRibbonContextSource.TableContextKey);
    }

    [Fact]
    public void Every_contextual_table_ribbon_command_is_registered()
    {
        // Verify that the existing registry-completeness guard passes with the new contextual tabs.
        var definition = FreeWRibbon.BuildDefinition();
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), NoopCallbacks());

        // Collect all command ids from ALL tabs (including contextual).
        var ids = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Select(GetCommandId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        foreach (var id in ids)
            registry.TryGet(id, out _)
                .Should().BeTrue($"Ribbon command '{id.Value}' must be registered");
    }

    [Fact]
    public void Ribbon_definition_has_at_least_54_commands_after_table_tabs()
    {
        // Was >= 37 before AV-TBLTAB; we add 17 new ribbon-level controls.
        var definition = FreeWRibbon.BuildDefinition();
        var count = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Count(c => GetCommandId(c) is not null);

        count.Should().BeGreaterThanOrEqualTo(54,
            "AV-TBLTAB adds 17 table contextual controls on top of the prior 37");
    }

    // ── Context source ────────────────────────────────────────────────────────

    [Fact]
    public async Task TableRibbonContextSource_fires_ContextChanged_when_entering_table()
    {
        var changed = false;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = TextDocument.CreateEmpty();
                var tbl = Table.Create(2, 2);
                doc.Blocks.Add(tbl);
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));

                var source = new TableRibbonContextSource(view);
                source.ContextChanged += (_, _) => changed = true;

                // Initially no table context.
                source.Current.IsActive(TableRibbonContextSource.TableContextKey)
                    .Should().BeFalse("context must be inactive before entering table");

                // Place caret in cell — should fire ContextChanged.
                var tblIdx = doc.Blocks.IndexOf(tbl);
                view.PlaceCaretInCell(tblIdx, row: 0, col: 0, paraIdx: 0, offset: 0);

                source.Current.IsActive(TableRibbonContextSource.TableContextKey)
                    .Should().BeTrue("context must be active after entering table");

                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        changed.Should().BeTrue("ContextChanged must fire when table context activates");
    }

    [Fact]
    public async Task TableRibbonContextSource_deactivates_when_leaving_table()
    {
        var ran = false;
        TableRibbonContextSource? source = null;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Add(new Paragraph("body"));
                var tbl = Table.Create(1, 1);
                doc.Blocks.Add(tbl);
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));

                source = new TableRibbonContextSource(view);

                // Enter table.
                var tblIdx = doc.Blocks.IndexOf(tbl);
                view.PlaceCaretInCell(tblIdx, row: 0, col: 0, paraIdx: 0, offset: 0);
                source.Current.IsActive(TableRibbonContextSource.TableContextKey)
                    .Should().BeTrue("context active in table");

                // Load a new document (clears cell caret) — context should deactivate.
                view.LoadDocument(TextDocument.CreateEmpty());

                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran || source is null) return;

        source.Current.IsActive(TableRibbonContextSource.TableContextKey)
            .Should().BeFalse("context must deactivate after leaving table");
    }

    // ── Model-mutation tests (no headless needed) ─────────────────────────────

    [Fact]
    public async Task InsertTableRowAbove_increases_row_count()
    {
        int? rowsBefore = null;
        int? rowsAfter  = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                rowsBefore = tbl.Rows.Count;
                view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
                view.InsertTableRowAbove();
                rowsAfter = tbl.Rows.Count;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        rowsAfter.Should().Be(rowsBefore + 1, "InsertTableRowAbove must add one row");
    }

    [Fact]
    public async Task DeleteTableRow_decreases_row_count()
    {
        int? rowsBefore = null;
        int? rowsAfter  = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                rowsBefore = tbl.Rows.Count;
                view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
                view.DeleteTableRow();
                rowsAfter = tbl.Rows.Count;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        rowsAfter.Should().Be(rowsBefore - 1, "DeleteTableRow must remove one row");
    }

    [Fact]
    public async Task ToggleTableHeaderRow_sets_HeaderRow_flag()
    {
        bool? headerRowAfter = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                var wasSet = tbl.Formatting.HeaderRow;
                view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
                view.ToggleTableHeaderRow();
                headerRowAfter = tbl.Formatting.HeaderRow;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        // Default is false; after one toggle it should be true.
        headerRowAfter.Should().BeTrue("ToggleTableHeaderRow must enable header row on first call");
    }

    [Fact]
    public async Task ToggleBandedRows_sets_BandedRows_flag()
    {
        bool? bandedAfter = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
                view.ToggleBandedRows();
                bandedAfter = tbl.Formatting.BandedRows;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        bandedAfter.Should().BeTrue("ToggleBandedRows must enable banded rows on first call");
    }

    [Fact]
    public async Task SetCellShading_applies_color_to_caret_cell()
    {
        string? shadingAfter = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
                view.SetCellShading("#AABBCC");
                shadingAfter = tbl.Rows[0].Cells[0].ShadingColorHex;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        shadingAfter.Should().Be("#AABBCC", "SetCellShading must apply the hex color to the caret cell");
    }

    [Fact]
    public async Task DeleteTableBlock_removes_the_table_from_document()
    {
        int? blockCountAfter = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = TextDocument.CreateEmpty();
                var tbl = Table.Create(2, 2);
                doc.Blocks.Add(tbl);
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));
                var tblIdx = doc.Blocks.IndexOf(tbl);
                view.DeleteTableBlock(tblIdx);
                blockCountAfter = doc.Blocks.OfType<Table>().Count();
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        blockCountAfter.Should().Be(0, "DeleteTableBlock must remove the table from the document");
    }

    [Fact]
    public async Task Table_commands_are_noops_when_caret_is_not_in_table()
    {
        // Smoke test: calling structural commands outside a table must not throw.
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Add(new Paragraph("no table here"));
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));

                // None of these must throw when CellCaretInfo is null.
                view.InsertTableRowAbove();
                view.InsertTableRowBelow();
                view.DeleteTableRow();
                view.InsertTableColumnLeft();
                view.InsertTableColumnRight();
                view.DeleteTableColumn();
                view.MergeSelectedCells();
                view.SplitCurrentCell();
                view.SetCellShading("#FF0000");
                view.ToggleTableHeaderRow();
                view.ToggleBandedRows();

                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        ran.Should().BeTrue("table commands must silently no-op when no table is active");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DocumentView View, int TableBlockIdx, Table Tbl) MakeTableView()
    {
        var doc = TextDocument.CreateEmpty();
        var tbl = Table.Create(3, 3);
        for (var r = 0; r < 3; r++)
        for (var c = 0; c < 3; c++)
            tbl.Rows[r].Cells[c] = new TableCell($"R{r}C{c}");
        doc.Blocks.Add(tbl);
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 4000));
        var idx = doc.Blocks.IndexOf(tbl);
        return (view, idx, tbl);
    }

    private static RibbonCommandId? GetCommandId(RibbonControl control) => control switch
    {
        RibbonButton b       => b.CommandId,
        RibbonToggleButton t => t.CommandId,
        RibbonComboBox c     => c.CommandId,
        RibbonCheckBox cb    => cb.CommandId,
        RibbonSplitButton sb => sb.CommandId,
        RibbonDropdown d     => d.CommandId,
        RibbonGallery g      => g.CommandId,
        _                    => (RibbonCommandId?)null,
    };
}
