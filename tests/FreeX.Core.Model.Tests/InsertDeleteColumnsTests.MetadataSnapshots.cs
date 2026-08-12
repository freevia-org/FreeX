using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    [Fact]
    public void ColumnCommands_UseCompactMetadataSnapshotsForUndo()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("InsertDeleteColumnsCommand.cs");
        var snapshotSource = ModelSourceTestSupport.ReadCommandsSource("RowColumnMutationSnapshot.cs");

        source.Should().Contain("private List<KeyValuePair<uint, double>>? _columnWidthSnapshot;");
        source.Should().Contain("CaptureDictionary(sheet.ColumnWidths)");
        snapshotSource.Should().Contain("CaptureDictionary(sheet.Comments)");
        source.Should().Contain("CaptureSet(sheet.HiddenCols)");
        source.Should().Contain("CaptureSortedSet(sheet.ColumnPageBreaks)");
        source.Should().NotContain("new Dictionary<uint, double>(sheet.ColumnWidths)");
        source.Should().NotContain("new Dictionary<CellAddress, string>(sheet.Comments)");
        source.Should().NotContain("[.. sheet.HiddenCols]");
        source.Should().NotContain("sheet.ColumnPageBreaks.ToList()");
    }
}
