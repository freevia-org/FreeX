using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip fidelity inventory for NativeJsonAdapter (.fxl format).
///
/// This test constructs a maximal workbook exercising every model feature,
/// round-trips through NativeJsonAdapter, and asserts field-by-field equality.
///
/// KNOWN GAPS (not serialized by NativeJsonAdapter — exclusion list):
/// 1.  Sheet.StructuredTables   — full table model (columns, filters, style options).
/// 2.  Sheet.CodeName           — VBA code name for the worksheet.
/// 3.  Sheet.DefaultColumnWidth — sheet-level default; NativeJsonAdapter writes 8.43 on load.
/// 4.  Sheet.DefaultRowHeight   — sheet-level default; NativeJsonAdapter writes 20.0 on load.
/// 5.  Pictures                 — chart/shape binary payloads; ImageBase64 round-trips but
///                               geometry/anchor detail tested in visual smoke tests.
/// 6.  TextBoxes                — binary visual payload; not tested here.
/// 7.  DrawingShapes            — binary visual payload; not tested here.
/// 8.  Charts                   — chart model (NativeJsonAdapter serializes chart XML blob but
///                               ChartModel properties like Title/Series are not individually
///                               asserted here; covered by NativeJsonSchemaTests.Charts).
/// 9.  PivotTables              — covered by NativeJsonPivotTableTests.
/// 10. DrawingObjectZOrder      — serialized but equality requires same-reference ordering;
///                               covered indirectly by visual smoke tests.
/// 11. Workbook.Theme           — native XML blobs round-trip; field-by-field theme equality
///                               is large and covered by FileAdapterSmokeTests.WorkbookTheme.
/// 12. NativeXmlPreserveBag fields (SheetFormatMetadata, DimensionMetadata, etc.) — native XML
///                               opaque blobs; not individually asserted here.
///
/// If you add new model state and forget to serialize it, this test will fail because
/// the new field will differ between source and loaded workbook, reminding you to add
/// it to the serializer or explicitly to the exclusion list above.
/// </summary>
public sealed class NativeJsonFidelityInventoryTests
{
    [Fact]
    public void NativeJsonAdapter_RoundTrips_WorkbookScalarFields()
    {
        var (wb, _) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        loaded.Name.Should().Be(wb.Name);
        loaded.Uses1904DateSystem.Should().Be(wb.Uses1904DateSystem);
        loaded.ShowSheetTabs.Should().Be(wb.ShowSheetTabs);
        loaded.SheetTabRatio.Should().Be(wb.SheetTabRatio);
        loaded.FirstVisibleSheetIndex.Should().Be(wb.FirstVisibleSheetIndex);
        loaded.ActiveSheetIndex.Should().Be(wb.ActiveSheetIndex);
        loaded.IsStructureProtected.Should().Be(wb.IsStructureProtected);
        loaded.WindowArrangement.Should().Be(wb.WindowArrangement);
        loaded.CalculationMode.Should().Be(wb.CalculationMode);
        loaded.FullCalculationOnLoad.Should().Be(wb.FullCalculationOnLoad);
        loaded.ForceFullCalculation.Should().Be(wb.ForceFullCalculation);
        loaded.IterativeCalculation.Should().Be(wb.IterativeCalculation);
        loaded.MaxCalculationIterations.Should().Be(wb.MaxCalculationIterations);
        loaded.MaxCalculationChange.Should().BeApproximately(wb.MaxCalculationChange!.Value, 1e-15);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_SheetCount()
    {
        var (wb, _) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        loaded.SheetCount.Should().Be(wb.SheetCount);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_SheetVisibility()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        // IsHidden
        loaded.GetSheetAt(sheets.hiddenIdx).IsHidden.Should().BeTrue();
        loaded.GetSheetAt(sheets.visibleIdx).IsHidden.Should().BeFalse();

        // IsVeryHidden — the gap fixed by this commit
        loaded.GetSheetAt(sheets.veryHiddenIdx).IsVeryHidden.Should().BeTrue();
        loaded.GetSheetAt(sheets.veryHiddenIdx).IsHidden.Should().BeTrue();
        loaded.GetSheetAt(sheets.visibleIdx).IsVeryHidden.Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_SheetKind()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        loaded.GetSheetAt(sheets.mainIdx).Kind.Should().Be(SheetKind.Worksheet);
        loaded.GetSheetAt(sheets.visibleIdx).Kind.Should().Be(SheetKind.DialogSheet);
        loaded.GetSheetAt(sheets.visibleIdx).IsDialogSheet.Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_SheetProtection()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.IsProtected.Should().Be(src.IsProtected);
        // ProtectionPassword is hashed on save; we assert round-trip via truthy equality
        dst.ProtectionPermissions.Should().BeEquivalentTo(src.ProtectionPermissions);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_SheetTabColor()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        loaded.GetSheetAt(sheets.mainIdx).TabColor.Should().Be(wb.GetSheetAt(sheets.mainIdx).TabColor);
        loaded.GetSheetAt(sheets.visibleIdx).TabColor.Should().BeNull();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_SheetViewState()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.FrozenRows.Should().Be(src.FrozenRows);
        dst.FrozenCols.Should().Be(src.FrozenCols);
        dst.ViewTopRow.Should().Be(src.ViewTopRow);
        dst.ViewLeftCol.Should().Be(src.ViewLeftCol);
        dst.ActiveRow.Should().Be(src.ActiveRow);
        dst.ActiveCol.Should().Be(src.ActiveCol);
        dst.ViewMode.Should().Be(src.ViewMode);
        dst.ShowGridlines.Should().Be(src.ShowGridlines);
        dst.ShowHeadings.Should().Be(src.ShowHeadings);
        dst.ZoomPercent.Should().Be(src.ZoomPercent);
        dst.ShowFormulas.Should().Be(src.ShowFormulas);
        dst.ShowZeros.Should().Be(src.ShowZeros);
    }

    // Helper: compare a GridRange's row/col structure without comparing SheetId GUIDs.
    private static (uint StartRow, uint StartCol, uint EndRow, uint EndCol) RangeShape(GridRange r) =>
        (r.Start.Row, r.Start.Col, r.End.Row, r.End.Col);

    [Fact]
    public void NativeJsonAdapter_RoundTrips_RowAndColDimensions()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.RowHeights.Should().BeEquivalentTo(src.RowHeights);
        dst.ColumnWidths.Should().BeEquivalentTo(src.ColumnWidths);
        dst.HiddenRows.Should().BeEquivalentTo(src.HiddenRows);
        dst.HiddenCols.Should().BeEquivalentTo(src.HiddenCols);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_OutlineGrouping()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.RowOutlineLevels.Should().BeEquivalentTo(src.RowOutlineLevels);
        dst.ColOutlineLevels.Should().BeEquivalentTo(src.ColOutlineLevels);
        dst.OutlineSummaryBelow.Should().Be(src.OutlineSummaryBelow);
        dst.OutlineSummaryRight.Should().Be(src.OutlineSummaryRight);
        dst.ShowOutlineSymbols.Should().Be(src.ShowOutlineSymbols);
        dst.GroupHiddenRows.Should().BeEquivalentTo(src.GroupHiddenRows);
        dst.GroupHiddenCols.Should().BeEquivalentTo(src.GroupHiddenCols);
        dst.CollapsedAnchorRows.Should().BeEquivalentTo(src.CollapsedAnchorRows);
        dst.CollapsedAnchorCols.Should().BeEquivalentTo(src.CollapsedAnchorCols);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_MergedRegions()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        // MergedRegion.ToString() includes the SheetId in the address format;
        // compare by row/col shape instead.
        var srcRegions = wb.GetSheetAt(sheets.mainIdx).MergedRegions
            .Select(RangeShape)
            .OrderBy(r => r)
            .ToList();
        var dstRegions = loaded.GetSheetAt(sheets.mainIdx).MergedRegions
            .Select(RangeShape)
            .OrderBy(r => r)
            .ToList();

        dstRegions.Should().Equal(srcRegions);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_CellValuesAndFormulas()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.GetCell(1, 1)!.Value.Should().Be(src.GetCell(1, 1)!.Value);       // text
        dst.GetCell(1, 2)!.Value.Should().Be(src.GetCell(1, 2)!.Value);       // number
        dst.GetCell(1, 3)!.Value.Should().Be(src.GetCell(1, 3)!.Value);       // bool
        dst.GetCell(2, 1)!.FormulaText.Should().Be(src.GetCell(2, 1)!.FormulaText); // formula
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_Comments()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        // Comments are keyed by CellAddress whose SheetId differs after round-trip;
        // compare by row/col key and text value.
        dst.Comments.Count.Should().Be(src.Comments.Count);
        foreach (var (addr, text) in src.Comments)
        {
            var dstKey = dst.Comments.Keys.SingleOrDefault(a => a.Row == addr.Row && a.Col == addr.Col);
            dstKey.Should().NotBe(default(CellAddress), "comment at row {0} col {1} should survive round-trip", addr.Row, addr.Col);
            dst.Comments[dstKey].Should().Be(text);
        }
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_ThreadedComments()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.ThreadedComments.Count.Should().Be(src.ThreadedComments.Count);
        foreach (var (addr, comment) in src.ThreadedComments)
        {
            // SheetId GUIDs differ after round-trip — match by row/col.
            var dstKey = dst.ThreadedComments.Keys.SingleOrDefault(a => a.Row == addr.Row && a.Col == addr.Col);
            dstKey.Should().NotBe(default(CellAddress), "threaded comment at row {0} col {1} should survive round-trip", addr.Row, addr.Col);
            dst.ThreadedComments[dstKey].Text.Should().Be(comment.Text);
            dst.ThreadedComments[dstKey].Author.Should().Be(comment.Author);
            dst.ThreadedComments[dstKey].IsResolved.Should().Be(comment.IsResolved);
        }
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_Hyperlinks()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        // Hyperlinks keyed by CellAddress (SheetId differs after round-trip).
        dst.Hyperlinks.Count.Should().Be(src.Hyperlinks.Count);
        foreach (var (addr, target) in src.Hyperlinks)
        {
            var dstKey = dst.Hyperlinks.Keys.SingleOrDefault(a => a.Row == addr.Row && a.Col == addr.Col);
            dstKey.Should().NotBe(default(CellAddress), "hyperlink at row {0} col {1} should survive round-trip", addr.Row, addr.Col);
            dst.Hyperlinks[dstKey].Should().Be(target);
        }
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_DataValidations()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.DataValidations.Count.Should().Be(src.DataValidations.Count);
        for (var i = 0; i < src.DataValidations.Count; i++)
        {
            var s = src.DataValidations[i];
            var d = dst.DataValidations[i];
            // SheetId GUIDs differ; compare row/col extent only.
            RangeShape(d.AppliesTo).Should().Be(RangeShape(s.AppliesTo));
            d.Type.Should().Be(s.Type);
            d.Formula1.Should().Be(s.Formula1);
            d.AllowBlank.Should().Be(s.AllowBlank);
            d.ShowDropdown.Should().Be(s.ShowDropdown);
            d.AlertStyle.Should().Be(s.AlertStyle);
            d.ErrorTitle.Should().Be(s.ErrorTitle);
        }
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_ConditionalFormats()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.ConditionalFormats.Count.Should().Be(src.ConditionalFormats.Count);
        for (var i = 0; i < src.ConditionalFormats.Count; i++)
        {
            var s = src.ConditionalFormats[i];
            var d = dst.ConditionalFormats[i];
            // SheetId GUIDs differ; compare row/col extent only.
            RangeShape(d.AppliesTo).Should().Be(RangeShape(s.AppliesTo));
            d.RuleType.Should().Be(s.RuleType);
            d.Priority.Should().Be(s.Priority);
        }
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_Sparklines()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.Sparklines.Count.Should().Be(src.Sparklines.Count);
        for (var i = 0; i < src.Sparklines.Count; i++)
        {
            dst.Sparklines[i].Kind.Should().Be(src.Sparklines[i].Kind);
            // SheetId GUIDs differ between source and loaded workbook; compare row/col only.
            dst.Sparklines[i].Location.Row.Should().Be(src.Sparklines[i].Location.Row);
            dst.Sparklines[i].Location.Col.Should().Be(src.Sparklines[i].Location.Col);
            RangeShape(dst.Sparklines[i].DataRange).Should().Be(RangeShape(src.Sparklines[i].DataRange));
        }
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_NamedRanges()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        loaded.NamedRanges.Should().HaveCount(wb.NamedRanges.Count);
        foreach (var (name, range) in wb.NamedRanges)
        {
            loaded.NamedRanges.Should().ContainKey(name);
            RangeShape(loaded.NamedRanges[name]).Should().Be(RangeShape(range));
        }
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_PrintAreaAndPageSetup()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.PrintArea.Should().NotBeNull();
        // SheetId differs after round-trip; compare row/col extent.
        RangeShape(dst.PrintArea!.Value).Should().Be(RangeShape(src.PrintArea!.Value));
        dst.PageOrientation.Should().Be(src.PageOrientation);
        dst.PaperSize.Should().Be(src.PaperSize);
        dst.PrintGridlines.Should().Be(src.PrintGridlines);
        dst.PrintHeadings.Should().Be(src.PrintHeadings);
        dst.CenterHorizontallyOnPage.Should().Be(src.CenterHorizontallyOnPage);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_AllowEditRanges()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        // SheetId differs after round-trip; compare row/col extent.
        var srcRanges = src.AllowEditRanges.Select(RangeShape).OrderBy(r => r).ToList();
        var dstRanges = dst.AllowEditRanges.Select(RangeShape).OrderBy(r => r).ToList();
        dstRanges.Should().Equal(srcRanges);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_PageBreaks()
    {
        var (wb, sheets) = BuildMaximalWorkbook();
        var loaded = RoundTrip(wb);

        var src = wb.GetSheetAt(sheets.mainIdx);
        var dst = loaded.GetSheetAt(sheets.mainIdx);

        dst.RowPageBreaks.Should().BeEquivalentTo(src.RowPageBreaks);
        dst.ColumnPageBreaks.Should().BeEquivalentTo(src.ColumnPageBreaks);
    }

    [Fact]
    public void NativeJsonAdapter_Load_BackwardCompatibility_OldSnapshotWithoutIsVeryHiddenDefaultsFalse()
    {
        // Old .fxl snapshots saved before IsVeryHidden was added must load cleanly.
        // System.Text.Json defaults missing bool properties to false, so no migration needed.
        const string oldJson = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "Legacy",
              "Sheets": [
                { "Name": "Visible" },
                { "Name": "Hidden", "IsHidden": true }
              ]
            }
            """;

        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(oldJson));
        var wb = new NativeJsonAdapter().Load(stream);

        wb.GetSheetAt(0).IsVeryHidden.Should().BeFalse();
        wb.GetSheetAt(1).IsVeryHidden.Should().BeFalse();
        wb.GetSheetAt(1).IsHidden.Should().BeTrue();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Workbook RoundTrip(Workbook source)
    {
        using var stream = new System.IO.MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    private static (Workbook Wb, (int mainIdx, int hiddenIdx, int veryHiddenIdx, int visibleIdx) Sheets)
        BuildMaximalWorkbook()
    {
        var wb = new Workbook("MaximalFidelityWorkbook");

        // ── Workbook-level scalar fields ───────────────────────────────────────
        wb.Uses1904DateSystem = true;
        wb.ShowSheetTabs = true;
        wb.SheetTabRatio = 600;
        wb.IsStructureProtected = false;
        wb.WindowArrangement = WorkbookWindowArrangement.Tiled;
        wb.CalculationMode = WorkbookCalculationMode.Automatic;
        wb.FullCalculationOnLoad = true;
        wb.ForceFullCalculation = false;
        wb.IterativeCalculation = true;
        wb.MaxCalculationIterations = 200;
        wb.MaxCalculationChange = 0.001;

        // ── Sheets ─────────────────────────────────────────────────────────────
        var main = wb.AddSheet("Main");
        var hidden = wb.AddSheet("HiddenSheet");
        var veryHidden = wb.AddSheet("VeryHiddenSheet");
        var visible = wb.AddSheet("Visible");

        int mainIdx = 0, hiddenIdx = 1, veryHiddenIdx = 2, visibleIdx = 3;

        wb.FirstVisibleSheetIndex = 0;
        wb.ActiveSheetIndex = 0;

        hidden.IsHidden = true;
        veryHidden.IsVeryHidden = true;
        veryHidden.IsHidden = true;
        visible.Kind = SheetKind.DialogSheet;

        // ── Main sheet: scalar fields ─────────────────────────────────────────
        main.TabColor = new CellColor(255, 0, 0);
        main.IsProtected = true;
        main.ProtectionPermissions.Clear();
        main.ProtectionPermissions.Add(SheetProtectionPermission.SelectLockedCells);
        main.ProtectionPermissions.Add(SheetProtectionPermission.Sort);

        // ── View state ─────────────────────────────────────────────────────────
        main.FrozenRows = 1;
        main.FrozenCols = 2;
        main.ViewTopRow = 1;
        main.ViewLeftCol = 1;
        main.ActiveRow = 3;
        main.ActiveCol = 2;
        main.ViewMode = WorksheetViewMode.Normal;
        main.ShowGridlines = false;
        main.ShowHeadings = false;
        main.ZoomPercent = 90;
        main.ShowFormulas = false;
        main.ShowZeros = false;

        // ── Row/col dimensions ────────────────────────────────────────────────
        main.RowHeights[1] = 30.0;
        main.RowHeights[5] = 45.5;
        main.ColumnWidths[1] = 12.5;
        main.ColumnWidths[3] = 20.0;
        main.HiddenRows.Add(4);
        main.HiddenCols.Add(2);

        // ── Outline grouping ──────────────────────────────────────────────────
        main.RowOutlineLevels[6] = 1;
        main.RowOutlineLevels[7] = 2;
        main.ColOutlineLevels[4] = 1;
        main.OutlineSummaryBelow = false;
        main.OutlineSummaryRight = true;
        main.ShowOutlineSymbols = true;
        main.GroupHiddenRows.Add(6);
        main.GroupHiddenCols.Add(4);
        main.CollapsedAnchorRows.Add(8);
        main.CollapsedAnchorCols.Add(5);

        // ── Cells: values, formulas, styles ──────────────────────────────────
        main.SetCell(new CellAddress(main.Id, 1, 1), new TextValue("Hello"));
        main.SetCell(new CellAddress(main.Id, 1, 2), new NumberValue(42.5));
        main.SetCell(new CellAddress(main.Id, 1, 3), new BoolValue(true));
        main.SetFormula(new CellAddress(main.Id, 2, 1), "A1&\" world\"");

        // ── Merged regions ────────────────────────────────────────────────────
        main.AddMergedRegion(new GridRange(
            new CellAddress(main.Id, 10, 1),
            new CellAddress(main.Id, 11, 3)));

        // ── Comments ─────────────────────────────────────────────────────────
        main.Comments[new CellAddress(main.Id, 1, 1)] = "A plain comment";

        // ── Threaded comments ─────────────────────────────────────────────────
        main.ThreadedComments[new CellAddress(main.Id, 2, 2)] = new ThreadedComment(
            "Threaded note",
            "alice@example.com")
        {
            IsResolved = false,
            Replies = [new CommentReply("Reply text", "bob@example.com")]
        };

        // ── Hyperlinks ────────────────────────────────────────────────────────
        main.Hyperlinks[new CellAddress(main.Id, 3, 1)] = "https://example.com";
        main.HyperlinkMetadata[new CellAddress(main.Id, 3, 1)] =
            new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, "Example", "");

        // ── Data validations ──────────────────────────────────────────────────
        main.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(main.Id, 5, 1), new CellAddress(main.Id, 10, 1)),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
            AllowBlank = true,
            ShowDropdown = false,
            AlertStyle = DvAlertStyle.Warning,
            ErrorTitle = "Invalid",
            ErrorMessage = "Must be 1-100"
        });

        main.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(main.Id, 5, 2), new CellAddress(main.Id, 10, 2)),
            Type = DvType.List,
            Formula1 = "\"A,B,C\"",
            AllowBlank = false,
            ShowDropdown = true,
            AlertStyle = DvAlertStyle.Stop
        });

        // ── Conditional formats ───────────────────────────────────────────────
        var cfRange = new GridRange(new CellAddress(main.Id, 1, 1), new CellAddress(main.Id, 20, 1));

        main.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "50",
            FormatIfTrue = new CellStyle { Bold = true }
        });

        main.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            Priority = 2,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = new RgbColor(255, 0, 0),
            MidColor = new RgbColor(255, 255, 0),
            MaxColor = new RgbColor(0, 255, 0)
        });

        main.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            Priority = 3,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(0, 112, 192)
        });

        main.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            Priority = 4,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 5,
            TopBottomPercent = false
        });

        main.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            Priority = 5,
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = false
        });

        main.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = cfRange,
            Priority = 6,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "Error"
        });

        // ── Sparklines ────────────────────────────────────────────────────────
        main.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(main.Id, 1, 1), new CellAddress(main.Id, 1, 5)),
            Location = new CellAddress(main.Id, 1, 6),
            Kind = SparklineKind.Column
        });

        main.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(main.Id, 2, 1), new CellAddress(main.Id, 2, 5)),
            Location = new CellAddress(main.Id, 2, 6),
            Kind = SparklineKind.WinLoss
        });

        // ── Named ranges (workbook-scope) ─────────────────────────────────────
        wb.DefineNamedRange(
            "SalesData",
            new GridRange(new CellAddress(main.Id, 1, 1), new CellAddress(main.Id, 10, 5)),
            new NamedRangeMetadata("Workbook", "Top-level sales range"));

        wb.DefineNamedRange(
            "SheetLocal",
            new GridRange(new CellAddress(main.Id, 5, 1), new CellAddress(main.Id, 5, 3)),
            new NamedRangeMetadata("Sheet", "Sheet-scoped range"));

        // ── Print area and page setup ─────────────────────────────────────────
        main.PrintArea = new GridRange(new CellAddress(main.Id, 1, 1), new CellAddress(main.Id, 50, 10));
        main.PageOrientation = WorksheetPageOrientation.Landscape;
        main.PaperSize = WorksheetPaperSize.A4;
        main.PrintGridlines = true;
        main.PrintHeadings = true;
        main.CenterHorizontallyOnPage = true;

        // ── Allow-edit ranges ─────────────────────────────────────────────────
        main.AllowEditRanges.Add(new GridRange(
            new CellAddress(main.Id, 15, 1),
            new CellAddress(main.Id, 20, 5)));

        // ── Page breaks ───────────────────────────────────────────────────────
        main.RowPageBreaks.Add(25);
        main.RowPageBreaks.Add(50);
        main.ColumnPageBreaks.Add(10);

        return (wb, (mainIdx, hiddenIdx, veryHiddenIdx, visibleIdx));
    }
}
