using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class ResidualReferenceDialogSessionTests
{
    [Fact]
    public void CrossReferenceSession_owns_catalog_transitions_and_acceptance()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Heading") { StyleId = "Heading1" });
        var bookmarked = new Paragraph("Bookmark target");
        bookmarked.BookmarkNames.Add("Details");
        document.Blocks.Add(bookmarked);
        var session = new CrossReferenceDialogSession(document);

        session.TypeChoices[session.State.TypeIndex].Type.Should().Be(CrossRefType.Heading);
        session.TargetChoices.Should().ContainSingle(choice => choice.Label == "Heading");

        session.UpdateType(session.TypeChoices.ToList().FindIndex(choice => choice.Type == CrossRefType.Bookmark));
        session.UpdateInsertAs(session.InsertAsChoices.ToList().FindIndex(choice => choice.InsertAs == CrossRefInsertAs.PageNumber));
        session.UpdateTarget(0);
        session.UpdateHyperlink(false);

        var acceptance = session.PlanAcceptance();
        acceptance.IsAccepted.Should().BeTrue();
        acceptance.Result.Should().NotBeNull();
        acceptance.Result!.Type.Should().Be(CrossRefType.Bookmark);
        acceptance.Result.Target.Display.Should().Be("Details");
        acceptance.Result.InsertAs.Should().Be(CrossRefInsertAs.PageNumber);
        acceptance.Result.Hyperlink.Should().BeFalse();
    }

    [Fact]
    public void CrossReferenceSession_returns_shared_validation_for_an_empty_catalog()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));
        var session = new CrossReferenceDialogSession(document);

        session.PlanAcceptance().Should().Be(new CrossReferenceDialogAcceptance(
            Result: null,
            CrossReferenceDialogPlanner.MissingTargetMessage));
    }

    [Fact]
    public void MarkIndexSession_owns_enabled_state_validation_and_mark_all_result()
    {
        var session = new MarkIndexEntryDialogSession("Animals", ["ChapterOne"]);

        session.BookmarkNames.Should().Equal("ChapterOne");
        session.PlanEnabledState(IndexEntryReferenceKind.PageRange).Should().Be(
            new MarkIndexEntryDialogEnabledState(
                BookmarkSelectorEnabled: true,
                CrossReferenceEnabled: false,
                PageNumberFormattingEnabled: true,
                MarkAllEnabled: false));

        var invalid = session.InitialState with
        {
            ReferenceKind = IndexEntryReferenceKind.CrossReference,
            CrossReference = " ",
        };
        session.PlanAcceptance(invalid, markAll: false).Validation.Should().Be(
            new MarkIndexEntryValidation(MarkIndexEntryDialogPlanner.MissingCrossReferenceMessage));

        var accepted = session.PlanAcceptance(
            session.InitialState with { BoldPageNumber = true },
            markAll: true);
        accepted.Result.Should().Be(new MarkIndexEntryDialogResult(
            new IndexMark("Animals", BoldPageNumber: true),
            MarkAll: true));
    }

    [Fact]
    public void MarkCitationSession_owns_categories_and_acceptance()
    {
        var session = new MarkCitationDialogSession("  Brown v. Board  ");
        var input = session.InitialState with
        {
            Category = CitationCategory.Statutes,
            ShortCitation = "  Brown  ",
        };

        session.CategoryChoices.Should().Contain(choice => choice.Category == CitationCategory.Statutes);
        session.PlanAcceptance(input).Result.Should().BeEquivalentTo(new MarkCitationDialogResult(
            new Citation("Brown v. Board", CitationCategory.Statutes, "Brown")));
        session.PlanAcceptance(input with { LongCitation = " " }).Validation.Should().Be(
            new MarkCitationValidation(MarkCitationDialogPlanner.MissingLongCitationMessage));
    }
}

public sealed class ResidualReferenceDialogSessionOwnershipTests
{
    [Theory]
    [InlineData("FreeW.App.Host", "CrossReferenceDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "ReferencesDialogs.cs")]
    public void CrossReferenceRenderers_delegate_catalog_state_and_acceptance_to_session(
        string project,
        string fileName)
    {
        var source = ReadSource(project, fileName);

        source.Should().Contain("CrossReferenceDialogSession");
        source.Should().Contain("_session.UpdateType(");
        source.Should().Contain("_session.UpdateInsertAs(");
        source.Should().Contain("_session.UpdateTarget(");
        source.Should().Contain("_session.PlanAcceptance()");
        source.Should().NotContain("CrossReferenceDialogPlanner.BuildTypeChoices(");
        source.Should().NotContain("CrossReferenceDialogPlanner.BuildInsertAsChoices(");
        source.Should().NotContain("CrossReferenceDialogPlanner.BuildTargetChoices(");
        source.Should().NotContain("CrossReferenceDialogPlanner.TryCreateChoice(");
    }

    [Theory]
    [InlineData("FreeW.App.Host", "MarkIndexEntryDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "ReferencesDialogs.cs")]
    public void MarkIndexRenderers_delegate_enabled_state_and_acceptance_to_session(
        string project,
        string fileName)
    {
        var source = ReadSource(project, fileName);

        source.Should().Contain("MarkIndexEntryDialogSession");
        source.Should().Contain("_session.PlanEnabledState(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("MarkIndexEntryDialogPlanner.CanMarkAll(");
        source.Should().NotContain("MarkIndexEntryDialogPlanner.TryBuildMark(");
    }

    [Theory]
    [InlineData("FreeW.App.Host", "MarkCitationDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "ReferencesDialogs.cs")]
    public void MarkCitationRenderers_delegate_categories_and_acceptance_to_session(
        string project,
        string fileName)
    {
        var source = ReadSource(project, fileName);

        source.Should().Contain("MarkCitationDialogSession");
        source.Should().Contain("_session.CategoryChoices");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("MarkCitationDialogPlanner.BuildCategoryChoices(");
        source.Should().NotContain("MarkCitationDialogPlanner.TryBuildCitation(");
    }

    [Theory]
    [InlineData("FreeW.App.Host", "CrossReferenceDialog.cs", "CrossReferenceDialogPlanner.AutomationId")]
    [InlineData("FreeW.App.Avalonia", "ReferencesDialogs.cs", "MarkIndexEntryDialogPlanner.AutomationId")]
    [InlineData("FreeW.App.Host", "MarkCitationDialog.cs", "MarkCitationDialogPlanner.AutomationId")]
    public void Renderers_consume_shared_accessibility_semantics(
        string project,
        string fileName,
        string expectedToken)
    {
        ReadSource(project, fileName).Should().Contain(expectedToken);
    }

    private static string ReadSource(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", project, fileName));
    }
}
