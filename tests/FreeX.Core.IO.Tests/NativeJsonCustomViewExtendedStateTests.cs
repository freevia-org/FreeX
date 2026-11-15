using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class NativeJsonCustomViewExtendedStateTests
{
    [Fact]
    public void RoundTrip_PreservesHiddenFilterAndPrintSnapshots()
    {
        var workbook = new Workbook("CustomViewExtendedState");
        var sheet = workbook.AddSheet("Data");
        var autoFilter = new WorksheetAutoFilterModel("A1:B4", null);
        autoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["North"], IncludeBlank: true));
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [new WorksheetCustomViewState(
                sheet.Name,
                WorksheetViewMode.PageLayout,
                FrozenRows: 0,
                FrozenCols: 0,
                SplitRow: null,
                SplitColumn: null,
                HiddenRows: [3, 8],
                HiddenCols: [2],
                FilterHiddenRows: [4, 6],
                AutoFilter: autoFilter,
                PrintAreas:
                [
                    GridRange.Parse("A1:C20", sheet.Id),
                    GridRange.Parse("E2:F9", sheet.Id)
                ],
                PageOrientation: WorksheetPageOrientation.Landscape,
                PaperSize: WorksheetPaperSize.Legal,
                PaperSizeCode: 5,
                PageMargins: new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1),
                HeaderMargin: 0.25,
                FooterMargin: 0.35,
                PrintGridlines: true,
                PrintHeadings: false,
                ScaleToFit: new WorksheetScaleToFit(null, 2, 3),
                FitToPage: true)],
            IncludePrintSettings: true,
            IncludeHiddenRowsColumnsAndFilterSettings: true));

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        var state = loaded.CustomViews.Should().ContainSingle().Subject.Sheets.Should().ContainSingle().Subject;
        state.HiddenRows.Should().Equal(3, 8);
        state.HiddenCols.Should().Equal(2);
        state.FilterHiddenRows.Should().Equal(4, 6);
        state.AutoFilter.Should().NotBeNull();
        state.AutoFilter!.Reference.Should().Be("A1:B4");
        var filterColumn = state.AutoFilter.FilterColumns.Should().ContainSingle().Subject;
        filterColumn.ColumnId.Should().Be(0);
        filterColumn.Values.Should().Equal("North");
        filterColumn.IncludeBlank.Should().BeTrue();
        state.PrintAreas.Should().NotBeNull();
        state.PrintAreas!.Select(range => range.ToString()).Should().Equal("A1:C20", "E2:F9");
        state.PrintAreas.Should().OnlyContain(range => range.Start.Sheet == loaded.GetSheetAt(0).Id);
        state.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        state.PaperSize.Should().Be(WorksheetPaperSize.Legal);
        state.PaperSizeCode.Should().Be(5);
        state.PageMargins.Should().Be(new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1));
        state.HeaderMargin.Should().Be(0.25);
        state.FooterMargin.Should().Be(0.35);
        state.PrintGridlines.Should().BeTrue();
        state.PrintHeadings.Should().BeFalse();
        state.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 2, 3));
        state.FitToPage.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PreservesNotCapturedVersusCapturedEmptyFacets()
    {
        var workbook = new Workbook("CustomViewEmptyState");
        workbook.AddSheet("Data");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Not captured",
            [new WorksheetCustomViewState("Data", WorksheetViewMode.Normal, 0, 0, null, null)],
            IncludePrintSettings: false,
            IncludeHiddenRowsColumnsAndFilterSettings: false));
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Captured empty",
            [new WorksheetCustomViewState(
                "Data",
                WorksheetViewMode.Normal,
                0,
                0,
                null,
                null,
                HiddenRows: [],
                HiddenCols: [],
                FilterHiddenRows: [],
                AutoFilter: null,
                PrintAreas: [])],
            IncludePrintSettings: true,
            IncludeHiddenRowsColumnsAndFilterSettings: true));

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        var omitted = loaded.CustomViews.Single(view => view.Name == "Not captured").Sheets.Single();
        omitted.HiddenRows.Should().BeNull();
        omitted.HiddenCols.Should().BeNull();
        omitted.FilterHiddenRows.Should().BeNull();
        omitted.PrintAreas.Should().BeNull();

        var captured = loaded.CustomViews.Single(view => view.Name == "Captured empty").Sheets.Single();
        captured.HiddenRows.Should().BeEmpty();
        captured.HiddenCols.Should().BeEmpty();
        captured.FilterHiddenRows.Should().BeEmpty();
        captured.AutoFilter.Should().BeNull();
        captured.PrintAreas.Should().BeEmpty();
    }
    [Fact]
    public void RoundTrip_PrintSnapshotPreservesHeadersPageSetupAndCapturedNulls()
    {
        var workbook = new Workbook("custom views");
        workbook.AddSheet("Data");
        static NativeXmlPreserveBag Bag(string key)
        {
            var bag = new NativeXmlPreserveBag();
            bag.Set(key, "<e custom=\"preserved\" />");
            return bag;
        }
        var print = new WorksheetCustomViewPrintSettings
        {
            PageHeader = new("left", "center", "right"),
            PageFooter = new("footer", "", ""),
            FirstPageHeader = new("", "first", ""),
            EvenPageFooter = new("", "", "even"),
            DifferentFirstPageHeaderFooter = true,
            DifferentOddEvenHeaderFooter = true,
            HeaderFooterScaleWithDocument = false,
            HeaderFooterAlignWithMargins = false,
            CenterHorizontallyOnPage = true,
            CenterVerticallyOnPage = true,
            PageOrder = WorksheetPageOrder.OverThenDown,
            FirstPageNumber = 4,
            UsePrinterDefaults = false,
            PrintCopies = 3,
            PrintBlackAndWhite = true,
            PrintDraftQuality = true,
            PrintQualityDpi = 600,
            PrintQualityVerticalDpi = 300,
            AutoPageBreaksCaptured = true,
            AutoPageBreaks = false,
            PrintTitleRows = new WorksheetRepeatRange(1, 2),
            PrintTitleColumns = new WorksheetRepeatRange(1, 3),
            RowPageBreaks = [2, 12],
            RowPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = new() { ["manualBreakCount"] = "2" },
                BreakNativeAttributes = new() { [2] = new() { ["max"] = "8" } }
            },
            ColumnPageBreaks = [3, 9],
            ColumnPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = new() { ["manualBreakCount"] = "2" },
                BreakNativeAttributes = new() { [3] = new() { ["max"] = "10" } }
            },
            FitToPage = null,
            LegacyPrintSizeCaptured = true,
            LegacyPrintSize = 12,
            PageSetupMetadataCaptured = true,
            PageSetupMetadata = Bag("pageSetup"),
            HeaderFooterMetadataCaptured = true,
            HeaderFooterMetadata = Bag("headerFooter"),
            PageMarginsMetadataCaptured = true,
            PageMarginsMetadata = Bag("pageMargins"),
            PrintOptionsMetadataCaptured = true,
            PrintOptionsMetadata = Bag("printOptions")
        };
        workbook.CustomViews.Add(new WorkbookCustomView("Captured",
            [new WorksheetCustomViewState(
                "Data",
                WorksheetViewMode.Normal,
                0,
                0,
                null,
                null)
            {
                PrintSettings = print
            }]));
        workbook.CustomViews.Add(new WorkbookCustomView("Omitted",
            [new WorksheetCustomViewState("Data", WorksheetViewMode.Normal, 0, 0, null, null)], IncludePrintSettings: false));

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var actual = loaded.CustomViews.Single(v => v.Name == "Captured").Sheets.Single().PrintSettings;
        actual.Should().NotBeNull();
        actual!.PageHeader.Should().Be(print.PageHeader);
        actual.PageFooter.Should().Be(print.PageFooter);
        actual.FirstPageHeader.Should().Be(print.FirstPageHeader);
        actual.EvenPageFooter.Should().Be(print.EvenPageFooter);
        actual.DifferentFirstPageHeaderFooter.Should().BeTrue();
        actual.DifferentOddEvenHeaderFooter.Should().BeTrue();
        actual.HeaderFooterScaleWithDocument.Should().BeFalse();
        actual.HeaderFooterAlignWithMargins.Should().BeFalse();
        actual.CenterHorizontallyOnPage.Should().BeTrue();
        actual.CenterVerticallyOnPage.Should().BeTrue();
        actual.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        actual.FirstPageNumber.Should().Be(4);
        actual.UsePrinterDefaults.Should().BeFalse();
        actual.PrintCopies.Should().Be(3);
        actual.PrintBlackAndWhite.Should().BeTrue();
        actual.PrintDraftQuality.Should().BeTrue();
        actual.PrintQualityDpi.Should().Be(600);
        actual.PrintQualityVerticalDpi.Should().Be(300);
        actual.AutoPageBreaksCaptured.Should().BeTrue();
        actual.AutoPageBreaks.Should().BeFalse();
        actual.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        actual.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 3));
        actual.RowPageBreaks.Should().Equal(2, 12);
        actual.ColumnPageBreaks.Should().Equal(3, 9);
        actual.RowPageBreaksMetadata!.NativeAttributes["manualBreakCount"].Should().Be("2");
        actual.RowPageBreaksMetadata.BreakNativeAttributes[2]["max"].Should().Be("8");
        actual.ColumnPageBreaksMetadata!.NativeAttributes["manualBreakCount"].Should().Be("2");
        actual.ColumnPageBreaksMetadata.BreakNativeAttributes[3]["max"].Should().Be("10");
        actual.FitToPage.Should().BeNull();
        actual.LegacyPrintSizeCaptured.Should().BeTrue();
        actual.LegacyPrintSize.Should().Be(12);
        actual.PageSetupMetadataCaptured.Should().BeTrue();
        actual.PageSetupMetadata!.Get("pageSetup").Should().Contain("custom=\"preserved\"");
        actual.HeaderFooterMetadataCaptured.Should().BeTrue();
        actual.HeaderFooterMetadata!.Get("headerFooter").Should().Contain("custom=\"preserved\"");
        actual.PageMarginsMetadataCaptured.Should().BeTrue();
        actual.PageMarginsMetadata!.Get("pageMargins").Should().Contain("custom=\"preserved\"");
        actual.PrintOptionsMetadataCaptured.Should().BeTrue();
        actual.PrintOptionsMetadata!.Get("printOptions").Should().Contain("custom=\"preserved\"");
        loaded.CustomViews.Single(v => v.Name == "Omitted").Sheets.Single().PrintSettings.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_PreservesCapturedNullFlagsAndPrintAreaForms()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var area = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, CellAddress.MaxRow, 2));
        var state = new WorksheetCustomViewState(
            sheet.Name,
            WorksheetViewMode.Normal,
            0,
            0,
            null,
            null,
            PrintAreas: [area])
        {
            PrintSettings = new WorksheetCustomViewPrintSettings
            {
                AutoPageBreaksCaptured = true, AutoPageBreaks = null,
                LegacyPrintSizeCaptured = true, LegacyPrintSize = null,
                PageSetupMetadataCaptured = true, PageSetupMetadata = null
            },
            PrintAreaReferenceForms = [new WorksheetCustomViewPrintAreaForm(area, WorksheetCustomViewPrintAreaKind.FullColumn)]
        };
        workbook.CustomViews.Add(new WorkbookCustomView("View", [state]));
        workbook.CustomViews.Add(new WorkbookCustomView("Opaque", [state with
        {
            PrintAreas = null,
            PrintAreaReferenceForms = null,
            OpaquePrintAreaExpression = "OFFSET(Data!$A$1,0,0,4,2)",
            OpaquePrintTitlesExpression = "CustomTitles"
        }]));
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var actual = loaded.CustomViews.Single(view => view.Name == "View").Sheets.Single();
        actual.PrintSettings!.AutoPageBreaksCaptured.Should().BeTrue();
        actual.PrintSettings.AutoPageBreaks.Should().BeNull();
        actual.PrintSettings.LegacyPrintSizeCaptured.Should().BeTrue();
        actual.PrintSettings.LegacyPrintSize.Should().BeNull();
        actual.PrintSettings.PageSetupMetadataCaptured.Should().BeTrue();
        actual.PrintSettings.PageSetupMetadata.Should().BeNull();
        actual.PrintAreaReferenceForms.Should().ContainSingle().Which.Kind.Should().Be(WorksheetCustomViewPrintAreaKind.FullColumn);
        actual.PrintAreaReferenceForms![0].Range.Should().Be(actual.PrintAreas![0]);
        var opaque = loaded.CustomViews.Single(view => view.Name == "Opaque").Sheets.Single();
        opaque.PrintAreas.Should().BeNull();
        opaque.OpaquePrintAreaExpression.Should().Be("OFFSET(Data!$A$1,0,0,4,2)");
        opaque.OpaquePrintTitlesExpression.Should().Be("CustomTitles");
    }

}
