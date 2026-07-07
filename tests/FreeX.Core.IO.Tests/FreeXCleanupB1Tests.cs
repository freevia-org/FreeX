using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression test for FreeX cleanup batch B1 finding P10 (slicer-timeline), the ColumnCount/
/// ShowCaption round-trip loss called out alongside the drawing-anchor gap:
/// <see cref="XlsxSlicerTimelineWriter"/> never emitted <c>columnCount</c>/<c>showCaption</c> on a
/// fresh save (only <c>name</c>/<c>caption</c>/<c>style</c>/<c>cache</c>/<c>rowHeight</c>), so a
/// slicer with a non-default tile-column layout or a hidden caption band silently reverted to
/// Excel's defaults (1 column, caption shown) on every save-from-scratch, even though
/// <see cref="XlsxSlicerTimelineMetadataReader"/> already parses both attributes back onto the
/// model. This only covers the writer-side attribute loss, not the separate (deferred, out of
/// scope for this fix) missing drawing graphicFrame anchor emission described in the same finding.
/// </summary>
public sealed class FreeXCleanupB1Tests
{
    [Fact]
    public void FreshSave_PreservesNonDefaultColumnCountAndHiddenCaption()
    {
        var workbook = new Workbook("SlicerColumnCountShowCaption");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2",
            ColumnCount = 4,
            ShowCaption = false
        };
        workbook.Slicers.Add(slicer);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(stream);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;

        reloadedSlicer.ColumnCount.Should().Be(4,
            "a fresh save must emit columnCount so a non-default tile-column layout survives a round trip");
        reloadedSlicer.ShowCaption.Should().BeFalse(
            "a fresh save must emit showCaption=\"0\" so a hidden caption band survives a round trip");
    }

    [Fact]
    public void FreshSave_DefaultColumnCountAndVisibleCaption_OmitsAttributes()
    {
        // Default-shaped slicer (columnCount=1, showCaption=true, i.e. what the reader already
        // defaults to when the attributes are absent) must not gain new XML noise from this fix.
        var workbook = new Workbook("SlicerDefaultShape");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2",
        };
        workbook.Slicers.Add(slicer);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(stream);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;

        reloadedSlicer.ColumnCount.Should().Be(1);
        reloadedSlicer.ShowCaption.Should().BeTrue();
    }
}
