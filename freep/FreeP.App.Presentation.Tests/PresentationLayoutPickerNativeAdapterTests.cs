using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationLayoutPickerNativeAdapterTests
{
    [Fact]
    public void Populate_PreservesGroupAndChoiceOrderAndBindsLayoutIds()
    {
        var title = Choice("title", "Title");
        var content = Choice("content", "Title and Content");
        var blank = Choice("blank", "Blank");
        var plan = new PresentationLayoutPickerPlan(
            PresentationDesignCommandPlanner.LayoutCommandId,
            CurrentLayoutId: "content",
            HasCurrentSlide: true,
            Choices: [title, content, blank],
            Groups:
            [
                new PresentationLayoutGroup("master-a", "Master A", [title, content]),
                new PresentationLayoutGroup("master-b", "Master B", [blank]),
            ]);
        var root = new FakeRoot();
        root.Entries.Add("stale");
        var applied = new List<string>();

        PresentationLayoutPickerNativeAdapter.Populate(
            plan,
            root,
            new PresentationLayoutPickerNativeBindings<FakeRoot, FakeHeading, FakeGroup, FakeChoice>(
                Clear: target => target.Entries.Clear(),
                CreateHeading: group => new FakeHeading(group.Heading),
                CreateGroup: group => new FakeGroup(group.GroupKey),
                CreateChoice: choice => new FakeChoice(choice),
                BindChoice: (choice, execute) => choice.Execute = execute,
                AddChoice: (group, choice) => group.Choices.Add(choice),
                AddHeading: (target, heading) => target.Entries.Add(heading),
                AddGroup: (target, group) => target.Entries.Add(group)),
            applied.Add);

        root.Entries.Should().HaveCount(4);
        root.Entries[0].Should().BeEquivalentTo(new FakeHeading("Master A"));
        var firstGroup = root.Entries[1].Should().BeOfType<FakeGroup>().Subject;
        firstGroup.Key.Should().Be("master-a");
        firstGroup.Choices.Select(choice => choice.Plan.LayoutId).Should().Equal("title", "content");
        root.Entries[2].Should().BeEquivalentTo(new FakeHeading("Master B"));
        var secondGroup = root.Entries[3].Should().BeOfType<FakeGroup>().Subject;
        secondGroup.Choices.Select(choice => choice.Plan.LayoutId).Should().Equal("blank");

        firstGroup.Choices[1].Execute.Should().NotBeNull();
        firstGroup.Choices[1].Execute!();
        secondGroup.Choices[0].Execute!();
        applied.Should().Equal("content", "blank");
    }

    [Fact]
    public void RendererSourcesDelegateTraversalAndActionBindingToSharedAdapter()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        foreach (var relativePath in new[]
                 {
                     Path.Combine("freep", "FreeP.App.Host", "MainWindow.cs"),
                     Path.Combine("freep", "FreeP.App.Avalonia", "MainWindow.cs"),
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            source.Should().Contain("PresentationLayoutPickerNativeAdapter.Populate(")
                .And.NotContain("foreach (var group in plan.Groups)")
                .And.NotContain("Tag = choice.LayoutId");
        }
    }

    private static PresentationLayoutChoice Choice(string id, string name) =>
        new(id, name, SlideLayoutType.Custom, false, "master", "Master", 0, 0);

    private sealed class FakeRoot
    {
        public List<object> Entries { get; } = [];
    }

    private sealed record FakeHeading(string Text);

    private sealed class FakeGroup(string key)
    {
        public string Key { get; } = key;
        public List<FakeChoice> Choices { get; } = [];
    }

    private sealed class FakeChoice(PresentationLayoutChoice plan)
    {
        public PresentationLayoutChoice Plan { get; } = plan;
        public Action? Execute { get; set; }
    }
}
