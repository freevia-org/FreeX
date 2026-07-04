using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for group D-sheet-objects findings:
/// G8 (DuplicateSheet must only remap a chart's cross-references onto the copy when they point
/// at the sheet being duplicated; cross-sheet DataRange/series formulas must stay pointing at the
/// original sheet and must not be dropped), and
/// G9 (RemoveSheet must clear PivotCacheModel.SourceSheetName / SlicerModel.SourceSheetName /
/// PictureModel.LinkedSourceSheetName when the sheet they name is deleted, mirroring the existing
/// chart.DataRange / RenameSheet T6 fix pattern).
/// </summary>
public sealed class DSheetObjectsRegressionTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // G8 — DuplicateSheet chart DataRange / series-formula remap
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DuplicateSheet_CrossSheetChartDataRange_StaysOnOriginalSheet()
    {
        // 'Data' holds the chart's source values; 'Dashboard' hosts the chart. Duplicating
        // Dashboard must NOT remap the chart's DataRange onto the new copy — Excel keeps
        // cross-sheet chart source references pointing at the original sheet.
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var dashboard = wb.AddSheet("Dashboard");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(data.Id, 1, 1),
            new CellAddress(data.Id, 10, 2));
        dashboard.Charts.Add(new ChartModel
        {
            Name = "Sales",
            Type = ChartType.Column,
            DataRange = dataRange
        });

        var command = new DuplicateSheetCommand(dashboard.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[2];
        copy.Name.Should().Be("Dashboard (2)");
        var copiedChart = copy.Charts.Should().ContainSingle().Subject;
        copiedChart.DataRange.Should().Be(dataRange,
            because: "a chart DataRange pointing at another sheet must not be remapped onto the duplicate");
    }

    [Fact]
    public void DuplicateSheet_SameSheetChartDataRange_IsRemappedOntoCopy()
    {
        // When the chart's DataRange points at the sheet being duplicated itself, the copy's
        // chart must point at the copy's own data (matching Excel: same-sheet refs travel with it).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));
        sheet.Charts.Add(new ChartModel
        {
            Name = "Sales",
            Type = ChartType.Column,
            DataRange = dataRange
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedChart = copy.Charts.Should().ContainSingle().Subject;
        copiedChart.DataRange.Start.Sheet.Should().Be(copy.Id);
        copiedChart.DataRange.End.Sheet.Should().Be(copy.Id);
        copiedChart.DataRange.Start.Row.Should().Be(dataRange.Start.Row);
        copiedChart.DataRange.End.Row.Should().Be(dataRange.End.Row);
    }

    [Fact]
    public void DuplicateSheet_PreservesVerbatimSeriesFormulasAndEmbeddedSeriesData()
    {
        // Multi-area verbatim series formulas and cached numCache/strCache series data must
        // survive Duplicate Sheet — not be silently dropped from the cloned chart.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var verbatim = new List<ChartSeriesVerbatimFormulas>
        {
            new(SeriesIndex: 0,
                ValFormula: "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5",
                CatFormula: null,
                TxFormula: "Sheet1!$A$1")
        };
        var embedded = new List<ChartEmbeddedSeriesData>
        {
            new(SeriesIndex: 0, SeriesName: "Series 1",
                Categories: ["a", "b"], Values: [1.0, 2.0])
        };
        var rangeLabels = new List<ChartSeriesRangeDataLabels>
        {
            new(SeriesIndex: 0, Formula: "Sheet1!$D$1:$D$5", PointCount: 5, Points: [])
        };

        sheet.Charts.Add(new ChartModel
        {
            Name = "Combo",
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            VerbatimSeriesFormulas = verbatim,
            EmbeddedSeriesData = embedded,
            SeriesRangeDataLabels = rangeLabels
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copiedChart = wb.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.VerbatimSeriesFormulas.Should().NotBeNull();
        copiedChart.VerbatimSeriesFormulas.Should().BeEquivalentTo(verbatim);
        copiedChart.EmbeddedSeriesData.Should().NotBeNull();
        copiedChart.EmbeddedSeriesData.Should().BeEquivalentTo(embedded);
        copiedChart.SeriesRangeDataLabels.Should().BeEquivalentTo(rangeLabels);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // G9 — RemoveSheet clears PivotCache/Slicer/Picture sheet-name refs
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RemoveSheet_ClearsPivotCacheSourceSheetName()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data"
        };
        wb.PivotCaches.Add(cache);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        cache.SourceSheetName.Should().BeNull(
            because: "a pivot cache must not keep pointing at a deleted sheet's name, or it could " +
                     "silently reattach to an unrelated later sheet with the same name");
    }

    [Fact]
    public void RemoveSheetRevert_RestoresPivotCacheSourceSheetName()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data"
        };
        wb.PivotCaches.Add(cache);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx);
        command.Revert(ctx);

        cache.SourceSheetName.Should().Be("Data");
    }

    [Fact]
    public void RemoveSheet_ClearsSlicerSourceSheetName()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);
        var slicer = new SlicerModel { SourceSheetName = "Data" };
        wb.Slicers.Add(slicer);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        slicer.SourceSheetName.Should().BeNull();
    }

    [Fact]
    public void RemoveSheetRevert_RestoresSlicerSourceSheetName()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);
        var slicer = new SlicerModel { SourceSheetName = "Data" };
        wb.Slicers.Add(slicer);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx);
        command.Revert(ctx);

        slicer.SourceSheetName.Should().Be("Data");
    }

    [Fact]
    public void RemoveSheet_ClearsPictureLinkedSourceSheetName()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Name = "Snapshot",
            Anchor = new CellAddress(report.Id, 1, 1),
            IsLinkedToSourceRange = true,
            LinkedSourceSheetName = "Data"
        };
        report.Pictures.Add(picture);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        picture.LinkedSourceSheetName.Should().BeNull();
    }

    [Fact]
    public void RemoveSheetRevert_RestoresPictureLinkedSourceSheetName()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Name = "Snapshot",
            Anchor = new CellAddress(report.Id, 1, 1),
            IsLinkedToSourceRange = true,
            LinkedSourceSheetName = "Data"
        };
        report.Pictures.Add(picture);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx);
        command.Revert(ctx);

        picture.LinkedSourceSheetName.Should().Be("Data");
    }

    [Fact]
    public void RemoveSheet_DoesNotTouchSourceSheetNameOfUnrelatedSheet()
    {
        // Deleting 'Old' must not clear string refs pointing at a differently-named surviving sheet.
        var wb = new Workbook("test");
        var oldSheet = wb.AddSheet("Old");
        wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data"
        };
        wb.PivotCaches.Add(cache);

        var command = new RemoveSheetCommand(oldSheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        cache.SourceSheetName.Should().Be("Data");
    }
}
