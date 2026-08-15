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
}
