using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class InsertEditingRibbonWorkflowTests
{
    [Fact]
    public void RegistersCanonicalLinkBookmarkAliasesAndSharedCommands()
    {
        var registry = new RibbonCommandRegistry();
        var events = new List<string>();
        var ports = CreatePorts(events);

        InsertEditingRibbonWorkflow.Register(registry, ports);

        Command(registry, "freew.hyperlink").Should().BeSameAs(ports.Hyperlink);
        Command(registry, "freew.insert-hyperlink").Should().BeSameAs(ports.Hyperlink);
        Command(registry, "freew.bookmark").Should().BeSameAs(ports.Bookmark);
        Command(registry, "freew.insert-bookmark").Should().BeSameAs(ports.Bookmark);

        foreach (var id in new[]
                 {
                     "freew.edit-hyperlink",
                     "freew.remove-hyperlink",
                     "freew.hyperlink-tooltip",
                     "freew.link-bookmark",
                     "freew.bookmark-manager",
                     "freew.cc-text",
                     "freew.cc-richtext",
                     "freew.cc-checkbox",
                     "freew.cc-date",
                     "freew.cc-dropdown",
                     "freew.cc-combo",
                     "freew.update-fields",
                     "freew.toggle-field-codes",
                 })
        {
            Command(registry, id);
        }
    }

    [Fact]
    public void ContentControlsPrepareBeforeMutationWhileFieldActionsPreserveDirectExecution()
    {
        var registry = new RibbonCommandRegistry();
        var events = new List<string>();
        InsertEditingRibbonWorkflow.Register(registry, CreatePorts(events));

        Command(registry, "freew.cc-text").Execute(RibbonCommandContext.Empty);
        Command(registry, "freew.cc-combo").Execute(RibbonCommandContext.Empty);
        Command(registry, "freew.update-fields").Execute(RibbonCommandContext.Empty);
        Command(registry, "freew.toggle-field-codes").Execute(RibbonCommandContext.Empty);

        events.Should().Equal(
            "prepare", "cc-text",
            "prepare", "cc-combo",
            "update-fields",
            "toggle-field-codes");
    }

    [Fact]
    public void BothRenderersDelegateInsertEditingIdentityToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("InsertEditingRibbonWorkflow.Register(");
            source.Should().Contain("new InsertEditingRibbonPorts(");
            source.Should().NotContain("RegisterDeveloperControls(");
            source.Should().NotContain("Register(\"freew.insert-hyperlink\"");
            source.Should().NotContain("Register(\"freew.insert-bookmark\"");
        }

        wpf.Should().NotContain("registry.Bind(FreeWRibbonCommandAction.CcText")
            .And.NotContain("registry.Bind(FreeWRibbonCommandAction.UpdateFields");
        avalonia.Should().NotContain("r.Bind(FreeWRibbonCommandAction.CcText")
            .And.NotContain("r.Bind(FreeWRibbonCommandAction.UpdateFields");
    }

    private static InsertEditingRibbonPorts CreatePorts(ICollection<string> events)
    {
        IRibbonCommand RecordCommand(string name) => new RecordingCommand(events, name);
        Action RecordAction(string name) => () => events.Add(name);

        return new InsertEditingRibbonPorts(
            Hyperlink: RecordCommand("hyperlink"),
            EditHyperlink: RecordCommand("edit-hyperlink"),
            RemoveHyperlink: RecordCommand("remove-hyperlink"),
            HyperlinkTooltip: RecordCommand("hyperlink-tooltip"),
            Bookmark: RecordCommand("bookmark"),
            LinkBookmark: RecordCommand("link-bookmark"),
            BookmarkManager: RecordCommand("bookmark-manager"),
            PrepareContentControlInsertion: RecordAction("prepare"),
            InsertPlainTextControl: RecordAction("cc-text"),
            InsertRichTextControl: RecordAction("cc-richtext"),
            InsertCheckBoxControl: RecordAction("cc-checkbox"),
            InsertDatePickerControl: RecordAction("cc-date"),
            InsertDropDownListControl: RecordAction("cc-dropdown"),
            InsertComboBoxControl: RecordAction("cc-combo"),
            UpdateFields: RecordAction("update-fields"),
            ToggleFieldCodes: RecordAction("toggle-field-codes"));
    }

    private static IRibbonCommand Command(IRibbonCommandRegistry registry, string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue($"{id} should be registered");
        return command!;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private sealed class RecordingCommand(ICollection<string> events, string name) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => events.Add(name);
    }
}
