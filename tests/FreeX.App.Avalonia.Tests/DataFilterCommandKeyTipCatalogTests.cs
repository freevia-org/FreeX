using FreeX.App.Avalonia.Tests.Parity;

namespace FreeX.App.Avalonia.Tests;

public sealed class DataFilterCommandKeyTipCatalogTests
{
    [Fact]
    public void SharedDataFilterCommand_StaysOnDataTabAndAvaloniaBound()
    {
        var command = SurfaceCatalog.RibbonCommands
            .Single(entry => entry.CommandId == "Filter#FilterButton_Click" && !entry.IsMenuItem);

        command.TabHeader.Should().Be("Data");
        command.GroupHeader.Should().Be("Sort Filter");
        command.Display.Should().Be("Filter");
        command.KeyTip.Should().Be("T");
        SurfaceCatalog.AvaloniaBoundCanonicalIds.Should().Contain("Filter#FilterButton_Click");
    }
}
