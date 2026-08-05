using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaWorksheetStructureOwnershipSourceTests
{
    [Fact]
    public void InsertDeleteAdapters_DoNotConstructPortableCommands()
    {
        var sources = new[]
        {
            ReadAppSource("MainWindow.InsertDeleteCells.cs"),
            ReadAppSource("MainWindow.ContextMenuGridActions.cs"),
            ReadAppSource("MainWindow.RibbonMenuWires.cs"),
        };

        foreach (var source in sources)
        {
            source.Should().NotContain("new InsertRowsCommand");
            source.Should().NotContain("new InsertColumnsCommand");
            source.Should().NotContain("new InsertCellsCommand");
            source.Should().NotContain("new DeleteRowsCommand");
            source.Should().NotContain("new DeleteColumnsCommand");
            source.Should().NotContain("new DeleteCellsCommand");
        }

        sources.Should().Contain(source => source.Contains("ApplyWorksheetStructureResult("));
    }

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", fileName));

    private static string RepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FreeX.slnx")))
            current = current.Parent;

        current.Should().NotBeNull();
        return Path.Combine([current!.FullName, .. parts]);
    }
}
