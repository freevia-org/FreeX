using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    [Fact]
    public void ColumnCommands_UseCompactMetadataSnapshotsForUndo()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src",
            "FreeX.Core.Commands",
            "InsertDeleteColumnsCommand.cs"));

        source.Should().Contain("private List<KeyValuePair<uint, double>>? _columnWidthSnapshot;");
        source.Should().Contain("CaptureDictionary(sheet.ColumnWidths)");
        source.Should().Contain("CaptureDictionary(sheet.Comments)");
        source.Should().Contain("CaptureSet(sheet.HiddenCols)");
        source.Should().Contain("CaptureSortedSet(sheet.ColumnPageBreaks)");
        source.Should().NotContain("new Dictionary<uint, double>(sheet.ColumnWidths)");
        source.Should().NotContain("new Dictionary<CellAddress, string>(sheet.Comments)");
        source.Should().NotContain("[.. sheet.HiddenCols]");
        source.Should().NotContain("sheet.ColumnPageBreaks.ToList()");
    }
}
