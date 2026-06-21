using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class FreeWRibbonParityTests
{
    [Fact]
    public void Build_OrdersImplementedTopLevelTabsLikeWord()
    {
        FreeWRibbon.Build().VisibleTabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert", "design", "layout", "references", "mailings", "review", "view", "developer");
    }

    [Fact]
    public void Build_ExposesReferencesAsAWordStyleTopLevelTab()
    {
        var definition = FreeWRibbon.Build();

        definition.VisibleTabs.Select(tab => tab.Id)
            .Should()
            .ContainInOrder("layout", "references", "mailings");

        definition.FindTab("insert")!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("references", "Word exposes References as a dedicated top-level tab, not as an Insert group");
    }

    [Fact]
    public void ReferencesTab_GroupsImplementedReferenceCommandsLikeWord()
    {
        var references = FreeWRibbon.Build().FindTab("references");

        references.Should().NotBeNull();
        references!.Groups.Select(group => group.Id)
            .Should()
            .Equal("table-of-contents", "footnotes", "citations", "captions", "index", "authorities");

        CommandIds(references)
            .Should()
            .Contain(new[]
            {
                "freew.toc",
                "freew.toc-refresh",
                "freew.footnote",
                "freew.endnote",
                "freew.footnote-endnote-options",
                "freew.citation",
                "freew.citation-style",
                "freew.bibliography",
                "freew.caption",
                "freew.tof",
                "freew.tof-refresh",
                "freew.cross-reference",
                "freew.index-mark",
                "freew.index-insert",
                "freew.mark-citation",
                "freew.table-of-authorities",
                "freew.table-of-authorities-refresh"
            });
    }

    [StaFact]
    public void HomeFont_ExposesAndRegistersStrikethrough()
    {
        var definition = FreeWRibbon.Build();
        var font = definition.FindTab("home")!.FindGroup("font");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(font!)
            .Should()
            .Contain("freew.strikethrough");
        registry.TryGet("freew.strikethrough", out _)
            .Should()
            .BeTrue("Word exposes Strikethrough alongside Bold, Italic, and Underline");
    }

    [StaFact]
    public void HomeEditing_ExposesFindReplaceAndSelect()
    {
        var definition = FreeWRibbon.Build();
        var editing = definition.FindTab("home")!.FindGroup("editing");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onFindReplace: () => { });

        CommandIds(editing!)
            .Should()
            .Equal("freew.find", "freew.replace", "freew.select");
        registry.TryGet("freew.find", out _).Should().BeTrue();
        registry.TryGet("freew.replace", out _).Should().BeTrue();
        registry.TryGet("freew.select", out _).Should().BeTrue();
    }

    [StaFact]
    public void ReviewComments_ExposesAndRegistersWordStyleThreadActions()
    {
        var definition = FreeWRibbon.Build();
        var comments = definition.FindTab("review")!.FindGroup("comments");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(comments!)
            .Should()
            .Equal(
                "freew.new-comment",
                "freew.delete-comment",
                "freew.previous-comment",
                "freew.next-comment",
                "freew.reply-comment",
                "freew.resolve-comment");

        foreach (var commandId in CommandIds(comments!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Review comments group");
    }

    [StaFact]
    public void DeveloperControls_ExposesAndRegistersImplementedContentControlCommands()
    {
        var definition = FreeWRibbon.Build();
        var developer = definition.FindTab("developer");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        definition.VisibleTabs.Select(tab => tab.Id)
            .Should()
            .ContainInOrder("view", "developer");

        developer.Should().NotBeNull();
        developer!.Groups.Select(group => group.Id)
            .Should()
            .Equal("controls");

        CommandIds(developer)
            .Should()
            .Equal(
                "freew.cc-text",
                "freew.cc-richtext",
                "freew.cc-checkbox",
                "freew.cc-date",
                "freew.cc-dropdown",
                "freew.cc-combo");

        foreach (var commandId in CommandIds(developer))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Developer controls group");
    }

    [Fact]
    public void InsertTab_DoesNotExposeContentControlsOutsideDeveloper()
    {
        var insert = FreeWRibbon.Build().FindTab("insert");

        insert.Should().NotBeNull();
        insert!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("controls", "Word exposes content controls from the Developer tab");

        CommandIds(insert).Should().NotContain(new[]
        {
            "freew.cc-text",
            "freew.cc-richtext",
            "freew.cc-checkbox",
            "freew.cc-date",
            "freew.cc-dropdown",
            "freew.cc-combo"
        });
    }

    [Fact]
    public void Build_ExposesWordStyleTableDesignAndTableLayoutContextualTabs()
    {
        var definition = FreeWRibbon.Build();

        definition.ContextualTabs.Select(tab => tab.Id)
            .Should()
            .ContainInOrder("picture-format", "table-design", "table-layout");

        foreach (var tabId in new[] { "table-design", "table-layout" })
        {
            var tab = definition.FindTab(tabId);

            tab.Should().NotBeNull();
            tab!.Context.Should().NotBeNull();
            tab.Context!.ActivationKey.Should().Be("table");
            tab.Context.Label.Should().Be("Table Tools");
            tab.Context.Color.Should().Be(RibbonContextColor.Teal);
        }
    }

    [StaFact]
    public void TableDesign_ContextualTabContainsOnlyImplementedStyleCommands()
    {
        var definition = FreeWRibbon.Build();
        var tableDesign = definition.FindTab("table-design");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        tableDesign.Should().NotBeNull();
        tableDesign!.Groups.Select(group => group.Id)
            .Should()
            .Equal("table-style");

        CommandIds(tableDesign)
            .Should()
            .Equal(
                "freew.cell-shading",
                "freew.table-header-row",
                "freew.table-banded-rows");

        foreach (var commandId in CommandIds(tableDesign))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Table Design tab");
    }

    [StaFact]
    public void TableLayout_ContextualTabContainsImplementedTableLayoutCommands()
    {
        var definition = FreeWRibbon.Build();
        var tableLayout = definition.FindTab("table-layout");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        tableLayout.Should().NotBeNull();
        tableLayout!.Groups.Select(group => group.Id)
            .Should()
            .Equal("table-properties", "table-rows-cols", "table-merge", "table-data");

        CommandIds(tableLayout)
            .Should()
            .Equal(
                "freew.table-properties",
                "freew.table-insert-row",
                "freew.table-delete-row",
                "freew.table-insert-col",
                "freew.table-delete-col",
                "freew.merge-cells",
                "freew.split-cell",
                "freew.table-repeat-header",
                "freew.table-formula");

        foreach (var commandId in CommandIds(tableLayout))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Table Layout tab");
    }

    [Fact]
    public void InsertTab_DoesNotExposeTableMutationToolsOutsideTableContext()
    {
        var insert = FreeWRibbon.Build().FindTab("insert");

        insert.Should().NotBeNull();
        insert!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("table-tools");

        CommandIds(insert).Should().Contain("freew.table");
        CommandIds(insert).Should().NotContain(new[]
        {
            "freew.table-insert-row",
            "freew.table-delete-row",
            "freew.table-insert-col",
            "freew.table-delete-col",
            "freew.cell-shading",
            "freew.merge-cells",
            "freew.split-cell",
            "freew.table-header-row",
            "freew.table-banded-rows",
            "freew.table-repeat-header",
            "freew.table-formula",
            "freew.table-properties"
        });
    }

    private static IEnumerable<string> CommandIds(RibbonTab tab)
    {
        foreach (var control in tab.Groups.SelectMany(group => group.Controls))
        {
            if (!string.IsNullOrWhiteSpace(control.CommandId.Value))
                yield return control.CommandId.Value;
        }
    }

    private static IEnumerable<string> CommandIds(RibbonGroup group)
    {
        foreach (var control in group.Controls)
        {
            if (!string.IsNullOrWhiteSpace(control.CommandId.Value))
                yield return control.CommandId.Value;
        }
    }
}
