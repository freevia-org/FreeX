using System.Collections.Generic;
using System.Linq;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace FreeW.App.Host.Tests;

public sealed class SisterQuickAccessToolbarBuilderTests
{
    [Fact]
    public void BuildDefaultItems_ReturnsSaveUndoRedoDescriptors()
    {
        var items = SisterQuickAccessToolbarBuilder.BuildDefaultItems();

        items.Select(item => item.CommandId).Should().Equal("Save", "Undo", "Redo");
        items.Select(item => item.Tooltip).Should().Equal("Save (Ctrl+S)", "Undo (Ctrl+Z)", "Redo (Ctrl+Y)");
        items.Select(item => item.IconKind).Should().Equal(
            RibbonCommandIconKind.Save,
            RibbonCommandIconKind.Undo,
            RibbonCommandIconKind.Redo);
    }

    [Fact]
    public void Execute_RoutesKnownCommandIdsToSuppliedActions()
    {
        var invoked = new List<string>();
        var actions = new SisterQuickAccessToolbarActions(
            Save: () => invoked.Add("Save"),
            Undo: () => invoked.Add("Undo"),
            Redo: () => invoked.Add("Redo"));

        SisterQuickAccessToolbarBuilder.Execute(actions, "Save").Should().BeTrue();
        SisterQuickAccessToolbarBuilder.Execute(actions, "Undo").Should().BeTrue();
        SisterQuickAccessToolbarBuilder.Execute(actions, "Redo").Should().BeTrue();

        invoked.Should().Equal("Save", "Undo", "Redo");
    }

    [Fact]
    public void Execute_IgnoresUnknownCommandIds()
    {
        var invoked = false;
        var actions = new SisterQuickAccessToolbarActions(
            Save: () => invoked = true,
            Undo: () => invoked = true,
            Redo: () => invoked = true);

        SisterQuickAccessToolbarBuilder.Execute(actions, "Zoom").Should().BeFalse();

        invoked.Should().BeFalse();
    }
}
