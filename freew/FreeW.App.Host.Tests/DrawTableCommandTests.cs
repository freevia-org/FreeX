using System.Linq;
using FluentAssertions;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class DrawTableCommandTests
{
    [StaFact]
    public void DrawTable_RibbonCommandIsBacked()
    {
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.draw-table", out _).Should().BeTrue("draw-table must be backed");
    }

    [StaFact]
    public void Eraser_RibbonCommandIsBacked()
    {
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.eraser", out _).Should().BeTrue("eraser must be backed");
    }

    [Fact]
    public void TableDesignTab_DrawBordersGroup_ContainsDrawTableAndEraser()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var tableDesign = definition.FindTab("table-design");
        tableDesign.Should().NotBeNull();

        var drawBorders = tableDesign!.FindGroup("draw-borders");
        drawBorders.Should().NotBeNull("Table Design should have a Draw Borders group");

        var ids = drawBorders!.Controls
            .Select(c => c.CommandId.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        ids.Should().Contain("freew.draw-table");
        ids.Should().Contain("freew.eraser");
    }
}
