using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    [Fact]
    public void DeleteRowsCommand_UsesCompactMetadataSnapshotsForUndo()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("DeleteRowsCommand.cs");

        source.Should().Contain("CaptureDictionary(sheet.RowHeights)");
        source.Should().Contain("CaptureSet(sheet.HiddenRows)");
        source.Should().Contain("CaptureSortedSet(sheet.RowPageBreaks)");
        source.Should().NotContain("new Dictionary<uint, double>(sheet.RowHeights)");
        source.Should().NotContain("[.. sheet.HiddenRows]");
        source.Should().NotContain("sheet.RowPageBreaks.ToList()");
    }

    [Fact]
    public void InsertRowsCommand_UsesCompactMetadataSnapshotsForUndo()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("InsertDeleteRowsCommand.cs");
        var snapshotSource = ModelSourceTestSupport.ReadCommandsSource("RowColumnMutationSnapshot.cs");

        source.Should().Contain("private List<KeyValuePair<uint, double>>? _rowHeightSnapshot;");
        source.Should().Contain("CaptureDictionary(sheet.RowHeights)");
        snapshotSource.Should().Contain("CaptureDictionary(sheet.Comments)");
        source.Should().Contain("CaptureSortedSet(sheet.RowPageBreaks)");
        source.Should().NotContain("new Dictionary<uint, double>(sheet.RowHeights)");
        source.Should().NotContain("new Dictionary<CellAddress, string>(sheet.Comments)");
        source.Should().NotContain("sheet.RowPageBreaks.ToList()");
    }

    [Fact]
    public void RowCommands_PrecountTailCellSnapshotsBeforeAllocatingLists()
    {
        var insertSource = ModelSourceTestSupport.ReadCommandsSource("InsertDeleteRowsCommand.cs");
        var deleteSource = ModelSourceTestSupport.ReadCommandsSource("DeleteRowsCommand.cs");

        insertSource.Should().Contain("FullSnapshotCapacityThreshold");
        insertSource.Should().Contain("CaptureMovedCellsWithFullCapacity(sheet)");
        insertSource.Should().Contain("CountMovedCells(sheet, out var maxOccupied)");
        insertSource.Should().Contain("new List<CellStateSnapshot>(movedCount)");
        deleteSource.Should().Contain("FullSnapshotCapacityThreshold");
        deleteSource.Should().Contain("CaptureDeletedAndShiftedCellsWithFullCapacity(sheet, endRow)");
        deleteSource.Should().Contain("CountDeletedAndShiftedCells(sheet, endRow)");
        deleteSource.Should().Contain("new List<CellStateSnapshot>(shiftedCount)");
    }

    [Fact]
    public void CellStateSnapshot_StoresCoordinatesWithoutPerSnapshotSheetId()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("CellStateSnapshot.cs");

        source.Should().Contain("uint Row");
        source.Should().Contain("uint Col");
        source.Should().Contain("ToAddress(SheetId sheetId)");
        source.Should().NotContain("CellAddress Address");
    }

}
