using System.Linq;
using System.Xml.Linq;
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
        // Multi-area verbatim series formulas, "value from cells" data-label formulas, and cached
        // numCache/strCache series data must survive Duplicate Sheet — not be silently dropped from
        // the cloned chart. Same-sheet ("Sheet1!") references additionally travel with the duplicate
        // and are remapped onto the copy ("Sheet1 (2)"), matching Excel and the GridRange DataRange
        // remap asserted in DuplicateSheet_SameSheetChartDataRange_IsRemappedOntoCopy. Cached
        // embedded series data carries no sheet reference, so it is copied verbatim.
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
        var expectedVerbatim = new List<ChartSeriesVerbatimFormulas>
        {
            new(SeriesIndex: 0,
                ValFormula: "'Sheet1 (2)'!$A$1:$A$5,'Sheet1 (2)'!$C$1:$C$5",
                CatFormula: null,
                TxFormula: "'Sheet1 (2)'!$A$1")
        };
        var expectedRangeLabels = new List<ChartSeriesRangeDataLabels>
        {
            new(SeriesIndex: 0, Formula: "'Sheet1 (2)'!$D$1:$D$5", PointCount: 5, Points: [])
        };
        copiedChart.VerbatimSeriesFormulas.Should().NotBeNull();
        copiedChart.VerbatimSeriesFormulas.Should().BeEquivalentTo(expectedVerbatim);
        copiedChart.EmbeddedSeriesData.Should().NotBeNull();
        copiedChart.EmbeddedSeriesData.Should().BeEquivalentTo(embedded);
        copiedChart.SeriesRangeDataLabels.Should().BeEquivalentTo(expectedRangeLabels);
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

    // ══════════════════════════════════════════════════════════════════════════
    // R96 — RemoveSheet must preserve a WorksheetRange pivot cache's records into
    // RawRecordsXml when its source sheet is deleted, not just null SourceSheetName. Without this,
    // XlsxPivotTableWriter.Cache.cs's ToPivotCacheRecordsXml can neither re-resolve a live range
    // (source sheet gone) nor find preserved records (RawRecordsXml was never populated for
    // WorksheetRange sources), so the pivot table's cache silently truncates to
    // <pivotCacheRecords count="0"/> on the very next save — real Excel keeps the last-refreshed
    // cache intact after the source sheet disappears.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void R96_RemoveSheet_PreservesPivotCacheRecordsInRawRecordsXml()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        wb.AddSheet("Report");
        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("Region"));
        data.SetCell(new CellAddress(data.Id, 1, 2), new TextValue("Amount"));
        data.SetCell(new CellAddress(data.Id, 2, 1), new TextValue("East"));
        data.SetCell(new CellAddress(data.Id, 2, 2), new NumberValue(10));
        data.SetCell(new CellAddress(data.Id, 3, 1), new TextValue("West"));
        data.SetCell(new CellAddress(data.Id, 3, 2), new NumberValue(20));

        var ctx = new TestCommandContext(wb);
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        wb.PivotCaches.Add(cache);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        cache.SourceSheetName.Should().BeNull();
        cache.RawRecordsXml.Should().NotBeNullOrWhiteSpace(
            because: "the deleted sheet's last-known records must be captured as a fallback the " +
                     "writer can serve once it can no longer re-derive them from a live range");

        var recordsDoc = System.Xml.Linq.XDocument.Parse(cache.RawRecordsXml!);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        recordsDoc.Root!.Name.Should().Be(ns + "pivotCacheRecords");
        var records = recordsDoc.Root.Elements(ns + "r").ToList();
        records.Should().HaveCount(2, because: "two data rows (East/10, West/20) existed at delete time");
        records[0].Elements().Select(e => e.Name.LocalName).Should().Equal("s", "n");
        records[0].Elements().ElementAt(0).Attribute("v")!.Value.Should().Be("East");
        records[0].Elements().ElementAt(1).Attribute("v")!.Value.Should().Be("10");
        records[1].Elements().ElementAt(0).Attribute("v")!.Value.Should().Be("West");
        records[1].Elements().ElementAt(1).Attribute("v")!.Value.Should().Be("20");
    }

    [Fact]
    public void R96_RemoveSheetRevert_RestoresRawRecordsXmlToNull()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        wb.AddSheet("Report");
        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("Region"));
        data.SetCell(new CellAddress(data.Id, 1, 2), new TextValue("Amount"));
        data.SetCell(new CellAddress(data.Id, 2, 1), new TextValue("East"));
        data.SetCell(new CellAddress(data.Id, 2, 2), new NumberValue(10));
        data.SetCell(new CellAddress(data.Id, 3, 1), new TextValue("West"));
        data.SetCell(new CellAddress(data.Id, 3, 2), new NumberValue(20));

        var ctx = new TestCommandContext(wb);
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        wb.PivotCaches.Add(cache);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx);
        cache.RawRecordsXml.Should().NotBeNullOrWhiteSpace(); // sanity: capture happened

        command.Revert(ctx);

        cache.SourceSheetName.Should().Be("Data");
        cache.RawRecordsXml.Should().BeNull(
            because: "undoing the delete must not leave a cache carrying preserved records it never " +
                     "had before the delete happened");
    }

    [Fact]
    public void R96_RemoveSheet_NoSourceReference_DoesNotSynthesizeRawRecordsXml()
    {
        // Sibling/no-regression case: a cache with no SourceReference (e.g. table-sourced, or never
        // fully resolved) has nothing to capture a live range from -- must not throw, and must leave
        // RawRecordsXml untouched (still null), matching pre-fix behavior for this shape of cache.
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

        cache.SourceSheetName.Should().BeNull();
        cache.RawRecordsXml.Should().BeNull();
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

    // ══════════════════════════════════════════════════════════════════════════
    // R108 — RemoveSheet must remove the SlicerModel/TimelineModel instance itself from
    // ctx.Workbook.Slicers/Timelines when the sheet hosting its drawing anchor is deleted, not
    // merely null SourceSheetName. Nulling alone left the instance behind, homeless but alive,
    // and every downstream consumer that falls back to "wherever the connected pivot table
    // lives" (SlicerTimelinePanePlanner) or "sheet1" (XlsxSlicerTimelineWriter) would silently
    // reattach it to an unrelated surviving sheet on the next render/save -- exactly the
    // "dashboard" pattern (pivot on 'Data', slicer placed on 'Dashboard') this codebase already
    // explicitly supports elsewhere.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void R108_RemoveSheet_RemovesSlicerAnchoredOnDeletedSheet_EvenWhenPivotSurvivesElsewhere()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Data");
        var dashboard = wb.AddSheet("Dashboard");
        var ctx = new TestCommandContext(wb);
        // The dashboard pattern: the slicer is anchored on 'Dashboard' (its drawing lives there)
        // but filters a pivot table that lives on the surviving 'Data' sheet. Deleting
        // 'Dashboard' must delete the slicer itself, not leave it homeless and pointing only at
        // the still-alive pivot's name.
        var slicer = new SlicerModel { SourceSheetName = "Dashboard", SourcePivotTableName = "PivotOnData" };
        wb.Slicers.Add(slicer);

        var command = new RemoveSheetCommand(dashboard.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        wb.Slicers.Should().NotContain(slicer,
            "the slicer's drawing anchor lived on the deleted sheet, so real Excel deletes the " +
            "slicer along with it instead of letting it reattach to the surviving pivot's sheet");
    }

    [Fact]
    public void R108_RemoveSheet_RemovesTimelineAnchoredOnDeletedSheet()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Data");
        var dashboard = wb.AddSheet("Dashboard");
        var ctx = new TestCommandContext(wb);
        var timeline = new TimelineModel { SourceSheetName = "Dashboard", SourcePivotTableName = "PivotOnData" };
        wb.Timelines.Add(timeline);

        var command = new RemoveSheetCommand(dashboard.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        wb.Timelines.Should().NotContain(timeline);
    }

    [Fact]
    public void R108_RemoveSheetRevert_ReinsertsSlicerAtOriginalIndexInWorkbookSlicers()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Data");
        var dashboard = wb.AddSheet("Dashboard");
        var ctx = new TestCommandContext(wb);
        var survivorBefore = new SlicerModel { SourceSheetName = "Data" };
        var deleted = new SlicerModel { SourceSheetName = "Dashboard" };
        var survivorAfter = new SlicerModel { SourceSheetName = "Data" };
        wb.Slicers.Add(survivorBefore);
        wb.Slicers.Add(deleted);
        wb.Slicers.Add(survivorAfter);

        var command = new RemoveSheetCommand(dashboard.Id);
        command.Apply(ctx).Success.Should().BeTrue();
        wb.Slicers.Should().NotContain(deleted);

        command.Revert(ctx);

        wb.Slicers.Should().ContainInOrder(survivorBefore, deleted, survivorAfter);
        deleted.SourceSheetName.Should().Be("Dashboard");
    }

    [Fact]
    public void R108_RemoveSheet_DoesNotRemoveSlicerAnchoredOnSurvivingSheet()
    {
        // Sibling/no-regression: a slicer anchored on a DIFFERENT surviving sheet must be left
        // completely untouched by deleting an unrelated sheet.
        var wb = new Workbook("test");
        var oldSheet = wb.AddSheet("Old");
        wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);
        var slicer = new SlicerModel { SourceSheetName = "Data" };
        wb.Slicers.Add(slicer);

        var command = new RemoveSheetCommand(oldSheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        wb.Slicers.Should().ContainSingle().Which.Should().BeSameAs(slicer);
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

    // ══════════════════════════════════════════════════════════════════════════
    // R100 — RemoveSheet must clear ChartModel.PivotSourceSheetName when the sheet it names is
    // deleted, mirroring the PivotCache/Slicer/Picture/Timeline clears above (same field
    // RenameSheetCommand's T6 block rewrites on rename instead of clearing).
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void R100_RemoveSheet_ClearsChartPivotSourceSheetName()
    {
        // A pivot chart lives on 'Dashboard' but its source PivotTable lives on 'Data' — a common
        // layout (chart sheet separate from the pivot data sheet). Deleting 'Data' must not leave
        // the surviving chart naming a worksheet absent from the workbook.
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var dashboard = wb.AddSheet("Dashboard");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            Name = "PivotChart1",
            Type = ChartType.Column,
            PivotSourceSheetName = "Data",
            PivotTableName = "PivotTable1"
        };
        dashboard.Charts.Add(chart);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        chart.PivotSourceSheetName.Should().BeNull(
            because: "a pivot chart must not keep naming a deleted sheet as its PivotTable's source, or " +
                     "XlsxChartXmlWriter would emit a <c:pivotSource><c:name> referencing a nonexistent " +
                     "worksheet, and the stale name could silently reattach to an unrelated later sheet " +
                     "with the same name");
    }

    [Fact]
    public void R100_RemoveSheetRevert_RestoresChartPivotSourceSheetName()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var dashboard = wb.AddSheet("Dashboard");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            Name = "PivotChart1",
            Type = ChartType.Column,
            PivotSourceSheetName = "Data",
            PivotTableName = "PivotTable1"
        };
        dashboard.Charts.Add(chart);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx);
        command.Revert(ctx);

        chart.PivotSourceSheetName.Should().Be("Data");
    }

    [Fact]
    public void R100_RemoveSheet_DoesNotTouchChartPivotSourceSheetNameOfUnrelatedSheet()
    {
        // Sibling/no-regression case: deleting 'Old' must not clear a chart's PivotSourceSheetName
        // pointing at a differently-named surviving sheet.
        var wb = new Workbook("test");
        var oldSheet = wb.AddSheet("Old");
        var dashboard = wb.AddSheet("Dashboard");
        wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            Name = "PivotChart1",
            Type = ChartType.Column,
            PivotSourceSheetName = "Data",
            PivotTableName = "PivotTable1"
        };
        dashboard.Charts.Add(chart);

        var command = new RemoveSheetCommand(oldSheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        chart.PivotSourceSheetName.Should().Be("Data");
    }
}
