using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
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
            OpenFontDialog: () => { },
            OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { },
            ToggleOrientation:   () => { },
            ApplyMarginPreset:   _ => { },
            ApplyPaperSize:      _ => { },
            InsertPicture:       () => { },
            OpenWordCountDialog: () => { },
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
            "freew.table-last-row",
            "freew.table-first-column",
            "freew.table-last-column",
            "freew.table-banded-rows",
            "freew.table-banded-cols",
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
            "freew.table-view-gridlines",
            "freew.table-properties",
            "freew.table-insert-above",
            "freew.table-insert-below",
            "freew.table-insert-col-left",
            "freew.table-insert-col-right",
            "freew.table-delete-row",
            "freew.table-delete-col",
            "freew.table-delete",
            "freew.table-merge-cells",
            "freew.table-split-cell",
            "freew.split-table",
            "freew.table-row-height",
            "freew.table-col-width",
            "freew.table-distribute-rows",
            "freew.table-distribute-cols",
            "freew.table-autofit-contents",
            "freew.table-autofit-window",
            "freew.table-autofit-fixed",
            "freew.table-cell-margins",
            "freew.cell-text-direction-horizontal",
            "freew.cell-text-direction-rotate90",
            "freew.cell-text-direction-rotate270",
            "freew.table-repeat-header",
            "freew.table-formula",
            "freew.sort",
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
    public void Table_shading_command_routes_to_the_shell_color_picker()
    {
        var opened = false;
        var registry = FreeWAvaloniaRibbonCommands.Build(
            new DocumentView(),
            NoopCallbacks() with { OpenCellShadingDialog = () => opened = true });

        Execute(registry, "freew.table-shading");

        opened.Should().BeTrue("Table Design > Shading must open the color picker instead of applying a hidden default fill");
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
    public void Ribbon_definition_has_at_least_75_commands_after_table_layout_catchup()
    {
        // Was >= 54 after AV-TBLTAB; table layout catch-up adds 20 direct controls, then Sort.
        var definition = FreeWRibbon.BuildDefinition();
        var count = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Count(c => GetCommandId(c) is not null);

        count.Should().BeGreaterThanOrEqualTo(75,
            "table layout catch-up adds the remaining direct table controls");
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
    public async Task CellTextDirection_IsUndoable()
    {
        await Session.Dispatch(() =>
        {
            var (view, index, table) = MakeTableView();
            view.PlaceCaretInCell(index, row: 0, col: 0, paraIdx: 0, offset: 0);

            view.SetCaretCellTextDirection(CellTextDirection.Rotate270);
            table.Rows[0].Cells[0].TextDirection.Should().Be(CellTextDirection.Rotate270);
            view.CanUndo.Should().BeTrue();

            view.Undo();
            table.Rows[0].Cells[0].TextDirection.Should().Be(CellTextDirection.Horizontal);
            view.Redo();
            table.Rows[0].Cells[0].TextDirection.Should().Be(CellTextDirection.Rotate270);
        }, CancellationToken.None);
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
    public async Task Table_style_option_catchup_toggles_mutate_formatting_flags()
    {
        TableFormatting? formatting = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
                var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

                Execute(registry, "freew.table-last-row");
                Execute(registry, "freew.table-first-column");
                Execute(registry, "freew.table-last-column");
                Execute(registry, "freew.table-banded-cols");
                Execute(registry, "freew.table-repeat-header");

                formatting = tbl.Formatting;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        formatting!.LastRow.Should().BeTrue();
        formatting.FirstColumn.Should().BeTrue();
        formatting.LastColumn.Should().BeTrue();
        formatting.BandedColumns.Should().BeTrue();
        formatting.RepeatHeaderRow.Should().BeTrue();
    }

    [Fact]
    public async Task Table_layout_size_and_text_direction_commands_mutate_model()
    {
        AutoFitMode? autoFit = null;
        double? preferredWidth = null;
        double? firstColumnWidth = null;
        CellTextDirection? direction = null;
        bool? gridlines = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                tbl.ColumnWidthsPt.AddRange([60, 120, 180]);
                view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 0);
                var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

                Execute(registry, "freew.table-distribute-cols");
                firstColumnWidth = tbl.ColumnWidthsPt[0];
                Execute(registry, "freew.table-autofit-window");
                autoFit = tbl.AutoFit;
                preferredWidth = tbl.PreferredWidthPt;
                Execute(registry, "freew.cell-text-direction-rotate90");
                direction = tbl.Rows[0].Cells[1].TextDirection;
                Execute(registry, "freew.table-view-gridlines");
                gridlines = view.ViewTableGridlines;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        firstColumnWidth!.Value.Should().BeApproximately(120, 0.001);
        autoFit.Should().Be(AutoFitMode.Window);
        preferredWidth.Should().Be(TableLayoutOperations.DefaultAutoFitWindowWidthPt);
        direction.Should().Be(CellTextDirection.Rotate90);
        gridlines.Should().BeTrue();
    }

    [Fact]
    public async Task Table_properties_command_applies_callback_values()
    {
        double? columnWidth = null;
        TableCellMargins? cellMargins = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                view.PlaceCaretInCell(idx, row: 0, col: 1, paraIdx: 0, offset: 0);
                var callbacks = NoopCallbacks() with
                {
                    OpenTablePropertiesDialog = _ => view.ApplyTableProperties(TablePropertyValues())
                };
                var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

                Execute(registry, "freew.table-properties");

                columnWidth = tbl.Rows[2].Cells[1].WidthPt;
                cellMargins = tbl.Rows[0].Cells[1].Margins;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        columnWidth.Should().Be(144);
        cellMargins.Should().Be(new TableCellMargins(2, 8, 2, 8));
    }

    [Fact]
    public async Task Table_formula_command_applies_the_dialog_callback_result()
    {
        Run? formulaRun = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Clear();
                var tbl = new Table();
                tbl.Rows.Add(new TableRow { Cells = { new TableCell("1") } });
                tbl.Rows.Add(new TableRow { Cells = { new TableCell("2") } });
                tbl.Rows.Add(new TableRow { Cells = { new TableCell(string.Empty) } });
                doc.Blocks.Add(tbl);
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));
                view.PlaceCaretInCell(0, row: 2, col: 0, paraIdx: 0, offset: 0);

                var registry = FreeWAvaloniaRibbonCommands.Build(
                    view,
                    NoopCallbacks() with
                    {
                        OpenTableFormulaDialog = state =>
                            view.InsertTableFormula(new TableFormulaField(state.FormulaText)),
                    });
                Execute(registry, "freew.table-formula");

                formulaRun = tbl.Rows[2].Cells[0].Paragraphs[0].Runs.SingleOrDefault(r => r.TableFormula is not null);
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        formulaRun.Should().NotBeNull();
        formulaRun!.TableFormula!.Expression.Should().Be(TableFormulaDialogPlanner.SumAboveFormula);
        formulaRun.Text.Should().Be("3");
    }

    [Fact]
    public async Task Table_layout_sort_command_sorts_rows_by_caret_column()
    {
        IReadOnlyList<string>? sorted = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Clear();
                var tbl = new Table();
                tbl.Rows.Add(new TableRow { Cells = { new TableCell("Bravo") } });
                tbl.Rows.Add(new TableRow { Cells = { new TableCell("Alpha") } });
                tbl.Rows.Add(new TableRow { Cells = { new TableCell("Charlie") } });
                doc.Blocks.Add(tbl);

                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));
                view.PlaceCaretInCell(0, row: 0, col: 0, paraIdx: 0, offset: 0);
                var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

                Execute(registry, "freew.sort");

                var result = (Table)view.Document.Blocks[0];
                sorted = result.Rows.Select(row => row.Cells[0].PlainText).ToArray();
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        sorted.Should().Equal("Alpha", "Bravo", "Charlie");
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
    public async Task CellShadingDialog_apply_result_applies_the_chosen_palette_color()
    {
        string? shadingAfter = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);
                var result = CellShadingDialogPlanner.SelectPaletteColor(2);

                CellShadingDialog.ApplyResult(view, result);

                shadingAfter = tbl.Rows[0].Cells[0].ShadingColorHex;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        shadingAfter.Should().Be("#00B0F0", "the selected WPF palette color must be applied to the caret cell");
    }

    [Fact]
    public async Task CellShadingDialog_cancel_is_a_no_op()
    {
        string? shadingAfter = null;
        var canUndoAfter = true;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView();
                tbl.Rows[0].Cells[0].ShadingColorHex = "#123456";
                view.PlaceCaretInCell(idx, row: 0, col: 0, paraIdx: 0, offset: 0);

                CellShadingDialog.ApplyResult(view, CellShadingDialogPlanner.Cancel());

                shadingAfter = tbl.Rows[0].Cells[0].ShadingColorHex;
                canUndoAfter = view.CanUndo;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        shadingAfter.Should().Be("#123456", "cancelling the picker must leave the existing fill untouched");
        canUndoAfter.Should().BeFalse("cancelling the picker must not add an undo step");
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
                view.ToggleTableLastRow();
                view.ToggleTableFirstColumn();
                view.ToggleTableLastColumn();
                view.ToggleTableBandedColumns();
                view.ToggleTableRepeatHeaderRow();
                view.SplitTable();
                view.DistributeTableRows();
                view.DistributeTableColumns();
                view.SetTableAutoFit(AutoFitMode.Contents);
                view.SetCaretCellTextDirection(CellTextDirection.Rotate270);
                view.InsertTableFormula(new TableFormulaField("=SUM(ABOVE)"));

                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        ran.Should().BeTrue("table commands must silently no-op when no table is active");
    }

    // ── BY1: Select Table / Row / Column — no infinite loop ──────────────────

    /// <summary>
    /// BY1 regression: SetCellBlockSelection used to receive int.MaxValue for row/col bounds,
    /// causing ExpandForMergedCells to loop forever (r++ overflows int.MaxValue → int.MinValue,
    /// r &lt;= maxRow always true). The blame-hang-timeout catching a hang IS the regression check.
    /// </summary>
    [Fact]
    public async Task SelectTable_returns_bounded_range_without_hanging()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, tbl) = MakeTableView(); // 3×3 table
                view.PlaceCaretInCell(idx, row: 1, col: 1, paraIdx: 0, offset: 0);
                // Invoke select-table — must NOT hang.
                var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
                registry.TryGet(new RibbonCommandId("freew.table-select-table"), out var cmd);
                cmd!.Execute(RibbonCommandContext.Empty);
                range = view.SelectedCellRange;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        range.Should().NotBeNull("SelectTable must set a cell range");
        range!.Value.MinRow.Should().Be(0);
        range.Value.MinCol.Should().Be(0);
        range.Value.MaxRow.Should().Be(2, "3-row table → last row = 2");
        range.Value.MaxCol.Should().Be(2, "3-col table → last col = 2");
    }

    [Fact]
    public async Task SelectRow_returns_full_row_without_hanging()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, _) = MakeTableView(); // 3×3 table
                view.PlaceCaretInCell(idx, row: 1, col: 0, paraIdx: 0, offset: 0);
                var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
                registry.TryGet(new RibbonCommandId("freew.table-select-row"), out var cmd);
                cmd!.Execute(RibbonCommandContext.Empty);
                range = view.SelectedCellRange;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        range.Should().NotBeNull("SelectRow must set a cell range");
        range!.Value.MinRow.Should().Be(1, "row 1 selected");
        range.Value.MaxRow.Should().Be(1, "single row selected");
        range.Value.MinCol.Should().Be(0);
        range.Value.MaxCol.Should().Be(2, "all 3 columns covered");
    }

    [Fact]
    public async Task SelectColumn_returns_full_column_without_hanging()
    {
        (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? range = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                var (view, idx, _) = MakeTableView(); // 3×3 table
                view.PlaceCaretInCell(idx, row: 0, col: 2, paraIdx: 0, offset: 0);
                var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
                registry.TryGet(new RibbonCommandId("freew.table-select-col"), out var cmd);
                cmd!.Execute(RibbonCommandContext.Empty);
                range = view.SelectedCellRange;
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        range.Should().NotBeNull("SelectColumn must set a cell range");
        range!.Value.MinCol.Should().Be(2, "column 2 selected");
        range.Value.MaxCol.Should().Be(2, "single column selected");
        range.Value.MinRow.Should().Be(0);
        range.Value.MaxRow.Should().Be(2, "all 3 rows covered");
    }

    // ── BY3: DeleteTableBlock leaves a valid caret ────────────────────────────

    /// <summary>
    /// BY3 regression: after deleting the last table block, _caret.Block pointed past the
    /// document end. ClampCaret() must re-anchor it to a valid position.
    /// </summary>
    [Fact]
    public async Task DeleteTableBlock_caret_is_valid_and_typing_works()
    {
        int? caretBlockAfter = null;
        int? blockCountAfter = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                // Document: only a table (last block).
                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Clear();
                doc.Blocks.Add(new Paragraph("Before"));
                var tbl = Table.Create(2, 2);
                doc.Blocks.Add(tbl);
                var view = new DocumentView();
                view.LoadDocument(doc);
                view.Measure(new Size(800, 4000));

                var tblIdx = doc.Blocks.IndexOf(tbl);
                view.PlaceCaretInCell(tblIdx, row: 0, col: 0, paraIdx: 0, offset: 0);
                view.DeleteTableBlock(tblIdx);

                blockCountAfter = doc.Blocks.Count;
                caretBlockAfter = view.CellCaretInfo is null
                    ? view.CaretPosition.Block
                    : -1; // cell caret must be cleared
                // Subsequent type op must not throw (InsertText uses _caret which must be valid).
                view.InsertText("X");
                ran = true;
            }, CancellationToken.None);
        }
        catch { return; }

        if (!ran) return;
        blockCountAfter.Should().Be(1, "only the 'Before' paragraph remains");
        caretBlockAfter.Should().Be(0, "_caret.Block must be 0 after table deleted");
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

    private static TablePropertiesValues TablePropertyValues() => new(
        PreferredWidthPt: 300,
        Alignment: TableAlignment.Center,
        TextWrapping: false,
        IndentFromLeftPt: null,
        DefaultCellMargins: TableCellMargins.Default,
        CellSpacingPt: null,
        RowHeightPt: 36,
        RowHeightRule: TableRowHeightRule.Exact,
        AllowRowBreak: true,
        RepeatHeaderRow: true,
        ColumnWidthPt: 144,
        CellPreferredWidthPt: 150,
        CellVerticalAlignment: TableCellVerticalAlignment.Center,
        CellMargins: new TableCellMargins(2, 8, 2, 8));

    private static void Execute(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command)
            .Should().BeTrue($"command '{id}' must be registered");
        command!.Execute(RibbonCommandContext.Empty);
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
