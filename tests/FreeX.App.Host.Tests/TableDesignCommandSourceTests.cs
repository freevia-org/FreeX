using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TableDesignCommandSourceTests
{
    [Fact]
    public void TableDesignTotalRow_UsesPhysicalTotalsRowCommandAndReappliesKnownGalleryStyle()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.TableDesignCommands.cs"));

        source.Should().Contain("new SetStructuredTableTotalsRowCommand(");
        source.Should().Contain("var totalsRowChanged = false;");
        source.Should().Contain("if (totalsRowShown is { } showTotals && showTotals != table.TotalsRowShown)");
        source.Should().Contain("if (styleOptionChanged || totalsRowChanged)");
        source.Should().Contain("new CompositeWorkbookCommand(\"Table Style Options\", commands)");
        source.Should().NotContain("totalsRowShown: totalsRowShown");
    }
}
