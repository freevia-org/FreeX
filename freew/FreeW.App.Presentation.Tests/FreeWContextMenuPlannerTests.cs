using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWContextMenuPlannerTests
{
    [Fact]
    public void Inventory_CoversSevenExplicitWpfFamiliesAndPortableEditorCore()
    {
        FreeWContextMenuPlanner.Inventory.Count(entry => entry.IsExplicitWpfContextMenu).Should().Be(7);
        FreeWContextMenuPlanner.Inventory.Count(entry => entry.Coverage == FreeWContextMenuCoverage.Paired).Should().Be(9);
        FreeWContextMenuPlanner.Inventory.Count(entry => entry.Coverage == FreeWContextMenuCoverage.ExternalOnly).Should().Be(1);
        FreeWContextMenuPlanner.Inventory.Select(entry => entry.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void FixedCatalogs_HaveAuthoritativeCommandCounts()
    {
        CommandCount(FreeWContextMenuPlanner.BuildFindSpecial()).Should().Be(9);
        CommandCount(FreeWContextMenuPlanner.BuildParagraphSpacing()).Should().Be(6);
        CommandCount(FreeWContextMenuPlanner.BuildEffects()).Should().Be(4);
        CommandCount(FreeWContextMenuPlanner.BuildTableStyles()).Should().Be(21);
        CommandCount(FreeWContextMenuPlanner.BuildEditor(new(false, false, false, false, true))).Should().Be(7);
    }

    [Fact]
    public void ContentChoicePlan_IsDynamicAndMarksCurrentValueChecked()
    {
        var run = Run.DropDownListControl(
        [
            new ContentControlListItem("Red", "R"),
            new ContentControlListItem("Green", "G"),
            new ContentControlListItem("Blue", "B"),
        ]);
        run.Text = "Green";

        var items = FreeWContextMenuPlanner.BuildContentControl(run).Items;

        items.Should().HaveCount(3);
        items.Select(item => item.Header).Should().Equal("Red", "Green", "Blue");
        items.Select(item => item.IsChecked).Should().Equal(false, true, false);
    }

    [Fact]
    public void DatePlan_HasThreeStableRelativeChoicesAndCurrentCheck()
    {
        var run = Run.DatePickerControl("2026-07-20", dateFormat: "yyyy-MM-dd");

        var items = FreeWContextMenuPlanner.BuildContentControl(run, new DateTime(2026, 7, 20)).Items;

        items.Select(item => item.Header).Should().Equal(
            "Today (2026-07-20)",
            "Yesterday (2026-07-19)",
            "Tomorrow (2026-07-21)");
        items.Select(item => item.IsChecked).Should().Equal(true, false, false);
    }

    [Fact]
    public void ContentControlCommandAppliesChoiceAndStableRelativeDate()
    {
        var list = Run.DropDownListControl(
        [
            new ContentControlListItem("Red", "R"),
            new ContentControlListItem("Green", "G"),
        ]);
        var date = Run.DatePickerControl("old", dateFormat: "yyyy-MM-dd");
        var today = new DateTime(2026, 7, 20);

        FreeWContextMenuPlanner.ApplyContentControlCommand(
                list,
                new RibbonCommandId(FreeWContextMenuPlanner.ContentChoicePrefix + "1"))!
            .Text.Should().Be("Green");
        FreeWContextMenuPlanner.ApplyContentControlCommand(
                date,
                new RibbonCommandId(FreeWContextMenuPlanner.ContentDatePrefix + "2"),
                today)!
            .Text.Should().Be("2026-07-21");
        FreeWContextMenuPlanner.ApplyContentControlCommand(
                list,
                new RibbonCommandId("freew.context.unrelated"))
            .Should().BeNull();
    }

    [Fact]
    public void OutlinePlan_RecomputesBoundaryAndCollapseEnablement()
    {
        var blocks = new List<Block>
        {
            Heading("Title", "Document"),
            Heading("Heading1", "First"),
            Body("body"),
            Heading("Heading1", "Second"),
        };

        var open = FreeWContextMenuPlanner.BuildOutline(blocks, 1, isCollapsed: false);
        Enabled(open, FreeWContextMenuPlanner.OutlineMoveUp).Should().BeFalse();
        Enabled(open, FreeWContextMenuPlanner.OutlineMoveDown).Should().BeTrue();
        Enabled(open, FreeWContextMenuPlanner.OutlinePromote).Should().BeTrue();
        Enabled(open, FreeWContextMenuPlanner.OutlineDemote).Should().BeTrue();
        Enabled(open, FreeWContextMenuPlanner.OutlineCollapse).Should().BeTrue();
        Enabled(open, FreeWContextMenuPlanner.OutlineExpand).Should().BeFalse();

        var collapsed = FreeWContextMenuPlanner.BuildOutline(blocks, 1, isCollapsed: true);
        Enabled(collapsed, FreeWContextMenuPlanner.OutlineCollapse).Should().BeFalse();
        Enabled(collapsed, FreeWContextMenuPlanner.OutlineExpand).Should().BeTrue();
    }

    [Fact]
    public void EditorPlan_TracksSelectionProtectionClipboardAndHistoryState()
    {
        var plan = FreeWContextMenuPlanner.BuildEditor(new(
            CanUndo: true,
            CanRedo: false,
            HasSelection: true,
            CanPaste: true,
            CanEdit: false));

        Enabled(plan, FreeWContextMenuPlanner.EditorUndo).Should().BeTrue();
        Enabled(plan, FreeWContextMenuPlanner.EditorRedo).Should().BeFalse();
        Enabled(plan, FreeWContextMenuPlanner.EditorCut).Should().BeFalse();
        Enabled(plan, FreeWContextMenuPlanner.EditorCopy).Should().BeTrue();
        Enabled(plan, FreeWContextMenuPlanner.EditorPaste).Should().BeFalse();
        Enabled(plan, FreeWContextMenuPlanner.EditorDelete).Should().BeFalse();
        Enabled(plan, FreeWContextMenuPlanner.EditorSelectAll).Should().BeTrue();
    }

    [Fact]
    public void SpellingPlan_MatchesWpfOrderAndStateForPortableDiagnostic()
    {
        var diagnostic = new ProofingDiagnostic(
            BlockIndex: 0,
            RunIndex: 0,
            RunOffset: 0,
            ParagraphOffset: 0,
            Length: 3,
            Word: "teh",
            NormalizedWord: "teh",
            LanguageTag: "en-US");

        var spelling = FreeWContextMenuPlanner.BuildSpelling(new(
            diagnostic,
            CanEdit: true,
            CanIgnore: true,
            CanAddToDictionary: true));
        var editor = FreeWContextMenuPlanner.BuildEditor(
            new(false, false, false, false, true),
            new(diagnostic, CanEdit: true, CanIgnore: true, CanAddToDictionary: true));

        spelling.Items.Select(item => item.Kind == RibbonMenuItemKind.Separator ? "<separator>" : item.Header)
            .Should().Equal("the", "<separator>", "Ignore All", "Add to Dictionary", "<separator>");
        editor.Items.Select(item => item.Kind == RibbonMenuItemKind.Separator ? "<separator>" : item.Header)
            .Should().StartWith("the", "<separator>", "Ignore All", "Add to Dictionary");
        editor.Items.First(item => item.CommandId?.Value == FreeWContextMenuPlanner.EditorSpellingReplacementPrefix + "0")
            .IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void SpellingPlan_DisablesReplacementButKeepsDictionaryActionsWhenEditingIsLocked()
    {
        var diagnostic = new ProofingDiagnostic(0, 0, 0, 0, 3, "teh", "teh", "en-US");
        var menu = FreeWContextMenuPlanner.BuildSpelling(new(
            diagnostic,
            CanEdit: false,
            CanIgnore: true,
            CanAddToDictionary: true));

        menu.Items.Single(item => item.CommandId?.Value == FreeWContextMenuPlanner.EditorSpellingReplacementPrefix + "0")
            .IsEnabled.Should().BeFalse();
        menu.Items.Single(item => item.CommandId?.Value == FreeWContextMenuPlanner.EditorSpellingIgnoreAll)
            .IsEnabled.Should().BeTrue();
        menu.Items.Single(item => item.CommandId?.Value == FreeWContextMenuPlanner.EditorSpellingAddToDictionary)
            .IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GrammarDiagnostic_DoesNotAddSpellingActions()
    {
        var diagnostic = new ProofingDiagnostic(0, 0, 0, 4, 3, "teh", "teh", "en-US", ProofingDiagnosticKind.Grammar);
        var menu = FreeWContextMenuPlanner.BuildEditor(
            new(false, false, false, false, true),
            new(diagnostic, CanEdit: true, CanIgnore: true, CanAddToDictionary: true));

        CommandCount(menu).Should().Be(7);
        menu.Items
            .Where(item => item.CommandId?.Value == FreeWContextMenuPlanner.EditorSpellingIgnoreAll)
            .Should()
            .BeEmpty();
    }

    private static int CommandCount(RibbonMenu menu) =>
        menu.Items.Count(item => item.Kind == RibbonMenuItemKind.Command);

    private static bool Enabled(RibbonMenu menu, string commandId) =>
        menu.Items.Single(item => item.CommandId?.Value == commandId).IsEnabled;

    private static Paragraph Heading(string styleId, string text)
    {
        var paragraph = new Paragraph { StyleId = styleId };
        paragraph.Runs.Add(new Run(text));
        return paragraph;
    }

    private static Paragraph Body(string text)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text));
        return paragraph;
    }
}
