using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationDomainContextMenuNativeAdapterTests
{
    [Fact]
    public void Populate_PreservesHierarchySeparatorsStateAndLeafExecution()
    {
        var firstAction = new PresentationDomainContextAction(
            PresentationDomainContextActionKind.FormatChartArea,
            ShapeId: 7);
        var childAction = new PresentationDomainContextAction(
            PresentationDomainContextActionKind.DeleteTableRow,
            ShapeId: 9);
        var plan = new PresentationDomainContextMenuPlan(
            PresentationDomainContextMenuKind.Table,
        [
            Command("Format Area", firstAction, isEnabled: false),
            Separator(),
            new PresentationDomainContextMenuEntryPlan(
                PresentationDomainContextMenuEntryKind.Submenu,
                "Table",
                IsEnabled: true,
                Children:
                [
                    Command("Delete Row", childAction),
                    Separator(),
                    Command("Unavailable", action: null, isEnabled: false),
                ]),
        ]);
        var menu = new FakeMenu();
        var executed = new List<PresentationDomainContextAction>();

        PresentationDomainContextMenuNativeAdapter.Populate(
            plan,
            menu,
            new PresentationDomainContextMenuNativeBindings<FakeMenu, FakeItem>(
                CreateItem: entry => new FakeItem(entry.Text, entry.IsEnabled),
                AddRootSeparator: target => target.Entries.Add(FakeSeparator.Instance),
                AddRootItem: (target, item) => target.Entries.Add(item),
                AddChildSeparator: parent => parent.Children.Add(FakeSeparator.Instance),
                AddChildItem: (parent, item) => parent.Children.Add(item),
                BindExecute: (item, execute) => item.Execute = execute),
            executed.Add);

        menu.Entries.Should().HaveCount(3);
        var first = menu.Entries[0].Should().BeOfType<FakeItem>().Subject;
        first.Text.Should().Be("Format Area");
        first.IsEnabled.Should().BeFalse();
        menu.Entries[1].Should().BeSameAs(FakeSeparator.Instance);
        var submenu = menu.Entries[2].Should().BeOfType<FakeItem>().Subject;
        submenu.Children.Should().HaveCount(3);
        submenu.Execute.Should().BeNull("submenus must not execute as leaves");

        first.Execute.Should().NotBeNull();
        first.Execute!();
        var child = submenu.Children[0].Should().BeOfType<FakeItem>().Subject;
        child.Execute.Should().NotBeNull();
        child.Execute!();
        submenu.Children[1].Should().BeSameAs(FakeSeparator.Instance);
        submenu.Children[2].Should().BeOfType<FakeItem>().Which.Execute.Should().BeNull();
        executed.Should().Equal(firstAction, childAction);
    }

    [Fact]
    public void RendererSourcesDelegateRecursiveProjectionToSharedAdapter()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        foreach (var relativePath in new[]
                 {
                     Path.Combine("freep", "FreeP.App.Host", "MainWindow.cs"),
                     Path.Combine("freep", "FreeP.App.Avalonia", "MainWindow.cs"),
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            source.Should().Contain("PresentationDomainContextMenuNativeAdapter.Populate(")
                .And.NotContain("BuildDomainContextMenuItem(")
                .And.NotContain("foreach (var child in entry.Children)");
        }
    }

    private static PresentationDomainContextMenuEntryPlan Command(
        string text,
        PresentationDomainContextAction? action,
        bool isEnabled = true) =>
        new(PresentationDomainContextMenuEntryKind.Command, text, isEnabled, action);

    private static PresentationDomainContextMenuEntryPlan Separator() =>
        new(PresentationDomainContextMenuEntryKind.Separator, string.Empty, IsEnabled: false);

    private sealed class FakeMenu
    {
        public List<object> Entries { get; } = [];
    }

    private sealed class FakeItem(string text, bool isEnabled)
    {
        public string Text { get; } = text;
        public bool IsEnabled { get; } = isEnabled;
        public List<object> Children { get; } = [];
        public Action? Execute { get; set; }
    }

    private sealed class FakeSeparator
    {
        public static FakeSeparator Instance { get; } = new();
    }
}
