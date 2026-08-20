using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentParagraphFormattingCoordinatorTests
{
    [Fact]
    public void AggregateTogglesApplyOneValueAcrossTargetsAndUndoAtomically()
    {
        var document = Document("alpha", "bravo");
        var first = (Paragraph)document.Blocks[0];
        var second = (Paragraph)document.Blocks[1];
        second.Formatting = second.Formatting with { KeepWithNext = true };
        var session = Session(document);

        session.Paragraphs.ToggleKeepWithNext([0, 1]).Should().BeTrue();

        first.Formatting.KeepWithNext.Should().BeTrue();
        second.Formatting.KeepWithNext.Should().BeTrue();
        session.Commands.Undo().Should().BeTrue();
        first.Formatting.KeepWithNext.Should().BeFalse();
        second.Formatting.KeepWithNext.Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ListToggleAppliesOneKindAcrossSelectionAndUndoRestoresMixedState()
    {
        var document = Document("alpha", "bravo", "charlie");
        var first = (Paragraph)document.Blocks[0];
        var second = (Paragraph)document.Blocks[1];
        second.Formatting = second.Formatting with
        {
            ListKind = ListKind.Bullet,
            ListLevel = 2,
        };
        var session = Session(document);

        session.Paragraphs.ToggleListKind([0, 1, 2], ListKind.Bullet).Should().BeTrue();

        document.Blocks.Cast<Paragraph>()
            .Should().OnlyContain(paragraph => paragraph.Formatting.ListKind == ListKind.Bullet);
        second.Formatting.ListLevel.Should().Be(2);

        session.Commands.Undo().Should().BeTrue();
        first.Formatting.ListKind.Should().Be(ListKind.None);
        second.Formatting.ListKind.Should().Be(ListKind.Bullet);
        second.Formatting.ListLevel.Should().Be(2);
        ((Paragraph)document.Blocks[2]).Formatting.ListKind.Should().Be(ListKind.None);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ListToggleClearsListOnlyStateWhenEveryTargetAlreadyUsesKind()
    {
        var document = Document("alpha", "bravo");
        foreach (var paragraph in document.Blocks.Cast<Paragraph>())
        {
            paragraph.Formatting = paragraph.Formatting with
            {
                ListKind = ListKind.Number,
                ListLevel = 3,
                ListStartOverride = 7,
            };
        }
        var session = Session(document);

        session.Paragraphs.ToggleListKind([1, 0, 1, -1, 999], ListKind.Number).Should().BeTrue();

        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph =>
            paragraph.Formatting.ListKind == ListKind.None
            && paragraph.Formatting.ListLevel == 0
            && !paragraph.Formatting.ListStartOverride.HasValue);
    }

    [Fact]
    public void ListToggleRejectsNoneAndClearsNumberRestartWhenSwitchingKind()
    {
        var document = Document("alpha");
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Formatting = paragraph.Formatting with
        {
            ListKind = ListKind.Number,
            ListStartOverride = 12,
        };
        var session = Session(document);

        session.Paragraphs.ToggleListKind([0], ListKind.Bullet).Should().BeTrue();
        paragraph.Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraph.Formatting.ListStartOverride.Should().BeNull();

        var act = () => session.Paragraphs.ToggleListKind([0], ListKind.None);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // freew-numbering-restart F3: turning Numbering on for a paragraph that is NOT adjacent to an
    // existing Number list (an unrelated body paragraph sits between them) must start a fresh
    // sequence at 1 -- this is the only ribbon/command path that ever creates a Number list, so it
    // is also the only place able to tell "new list" apart from "resume that list".
    [Fact]
    public void ListToggleOnUnrelatedParagraphRestartsNumberListAtOne()
    {
        var document = Document("One", "Two", "Interrupting body text.", "Alpha");
        var first = (Paragraph)document.Blocks[0];
        var second = (Paragraph)document.Blocks[1];
        first.Formatting = first.Formatting with { ListKind = ListKind.Number };
        second.Formatting = second.Formatting with { ListKind = ListKind.Number };
        var newListParagraph = (Paragraph)document.Blocks[3];
        var session = Session(document);

        session.Paragraphs.ToggleListKind([3], ListKind.Number).Should().BeTrue();

        newListParagraph.Formatting.ListKind.Should().Be(ListKind.Number);
        newListParagraph.Formatting.ListStartOverride.Should().Be(1,
            "clicking Numbering on a paragraph separated from the earlier list by unrelated body " +
            "text starts an independent list, which must be able to begin again at 1");

        // Assert the two paths (the coordinator that sets the override, and the renderer's marker
        // planner that consumes it) actually agree on the rendered numbers, not just that a field
        // was set.
        var planner = new DocumentListMarkerSequencePlanner();
        planner.Advance((Paragraph)document.Blocks[0]).NumberValue.Should().Be(1);
        planner.Advance((Paragraph)document.Blocks[1]).NumberValue.Should().Be(2);
        planner.Advance((Paragraph)document.Blocks[2]); // unrelated body paragraph, not a list item
        planner.Advance(newListParagraph).NumberValue.Should().Be(1,
            "the renderer must show the restarted list starting at 1, matching the override the toggle set");
    }

    // Sibling no-regression case: toggling Numbering on for a paragraph immediately following an
    // existing Number list (no unrelated paragraph in between) is resuming that same list and must
    // keep continuing its count, exactly like ListNumberingRestartWpfTests'
    // NumberList_InterruptedByBodyParagraph_ContinuesNumberingAcrossInterruption already pins for
    // the render layer.
    [Fact]
    public void ListToggleOnAdjacentParagraphContinuesExistingNumberList()
    {
        var document = Document("One", "Two", "Three");
        var first = (Paragraph)document.Blocks[0];
        var second = (Paragraph)document.Blocks[1];
        first.Formatting = first.Formatting with { ListKind = ListKind.Number };
        second.Formatting = second.Formatting with { ListKind = ListKind.Number };
        var continuedParagraph = (Paragraph)document.Blocks[2];
        var session = Session(document);

        session.Paragraphs.ToggleListKind([2], ListKind.Number).Should().BeTrue();

        continuedParagraph.Formatting.ListKind.Should().Be(ListKind.Number);
        continuedParagraph.Formatting.ListStartOverride.Should().BeNull(
            "resuming a list right where it left off must not force a restart");

        var planner = new DocumentListMarkerSequencePlanner();
        planner.Advance(first).NumberValue.Should().Be(1);
        planner.Advance(second).NumberValue.Should().Be(2);
        planner.Advance(continuedParagraph).NumberValue.Should().Be(3,
            "the renderer must keep counting 1, 2, 3 -- this toggle continues the same list, it did not start a new one");
    }

    [Fact]
    public void BordersAndShadingUseSharedTargetFilteringAndCaseInsensitiveTogglePolicy()
    {
        var document = Document("alpha", "bravo");
        var first = (Paragraph)document.Blocks[0];
        var second = (Paragraph)document.Blocks[1];
        first.Formatting = first.Formatting with { ShadingColorHex = "#abcdef" };
        second.Formatting = second.Formatting with { ShadingColorHex = "#ABCDEF" };
        document.Blocks.Insert(1, new Table());
        var session = Session(document);

        session.Paragraphs.ToggleShading([0, 1, 2, 999], "#AbCdEf").Should().BeTrue();
        first.Formatting.ShadingColorHex.Should().BeNull();
        second.Formatting.ShadingColorHex.Should().BeNull();

        session.Paragraphs.ToggleBorder([0, 1, 2], "#123456", 1.25).Should().BeTrue();
        first.Formatting.Border.Should().Be(new ParagraphBorder("#123456", 1.25));
        second.Formatting.Border.Should().Be(new ParagraphBorder("#123456", 1.25));
    }

    [Fact]
    public void ParagraphDialogFormattingNormalizesValuesAndAuthorsExplicitFlags()
    {
        var document = Document("alpha", "bravo");
        var session = Session(document);

        session.Paragraphs.ApplyDialogFormatting(
                [0, 1],
                indentLeftPt: -4,
                indentRightPt: -5,
                firstLineIndentPt: -6,
                spaceBeforePt: -7,
                spaceAfterPt: -8,
                lineSpacing: 0.2,
                keepWithNext: true,
                keepLinesTogether: true,
                widowControl: false,
                pageBreakBefore: true,
                suppressAutoHyphens: true,
                suppressLineNumbers: true,
                contextualSpacing: true)
            .Should().BeTrue();

        foreach (var paragraph in document.Blocks.Cast<Paragraph>())
        {
            paragraph.Formatting.IndentLeftPt.Should().Be(0);
            paragraph.Formatting.IndentRightPt.Should().Be(0);
            paragraph.Formatting.FirstLineIndentPt.Should().Be(-6);
            paragraph.Formatting.SpaceBeforePt.Should().Be(0);
            paragraph.Formatting.SpaceAfterPt.Should().Be(0);
            paragraph.Formatting.SpaceBeforeIsSet.Should().BeTrue();
            paragraph.Formatting.SpaceAfterIsSet.Should().BeTrue();
            paragraph.Formatting.LineRule.Should().Be(LineSpacingRule.Multiple);
            paragraph.Formatting.LineSpacing.Should().Be(0.5);
            paragraph.Formatting.LineSpacingIsSet.Should().BeTrue();
            paragraph.Formatting.WidowControlIsSet.Should().BeTrue();
            paragraph.Formatting.SuppressAutoHyphensIsSet.Should().BeTrue();
            paragraph.Formatting.SuppressLineNumbersIsSet.Should().BeTrue();
            paragraph.Formatting.ContextualSpacing.Should().BeTrue();
        }

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>()
            .Should().OnlyContain(paragraph => paragraph.Formatting == ParagraphFormatting.Default);
    }

    [Fact]
    public void ProofingLanguagePlanFiltersInvalidAndRendererRejectedParagraphs()
    {
        var document = Document("alpha", "bravo");
        var session = Session(document);
        var plan = new ProofingLanguageApplyPlan(
            "fr-FR",
            [
                new ProofingLanguageTextRange(-1, 0, 1),
                new ProofingLanguageTextRange(0, 1, 4),
                new ProofingLanguageTextRange(1, 0, 2),
                new ProofingLanguageTextRange(99, 0, 1),
            ]);

        session.TryApplyProofingLanguage(plan, (index, _) => index == 0).Should().BeTrue();

        LanguageText((Paragraph)document.Blocks[0], "fr-FR").Should().Be("lph");
        LanguageText((Paragraph)document.Blocks[1], "fr-FR").Should().BeEmpty();
        session.Commands.Undo().Should().BeTrue();
        LanguageText((Paragraph)document.Blocks[0], "fr-FR").Should().BeEmpty();
    }

    private static DocumentEditingSession Session(TextDocument document)
    {
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }

    private static TextDocument Document(params string[] texts)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in texts)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static string LanguageText(Paragraph paragraph, string languageTag) =>
        string.Concat(paragraph.Runs
            .Where(run => string.Equals(run.Formatting.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase))
            .Select(run => run.Text));
}

public sealed class DocumentParagraphFormattingOwnershipTests
{
    [Fact]
    public void PairedRenderersDelegateSharedParagraphAndProofingPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sources = new[]
        {
            Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"),
        }.Select(File.ReadAllText).ToArray();

        foreach (var source in sources)
        {
            source.Should().Contain("DocumentParagraphFormattingCoordinator ParagraphEdits");
            source.Should().Contain("ParagraphEdits.ToggleKeepWithNext");
            source.Should().Contain("ParagraphEdits.ToggleKeepLinesTogether");
            source.Should().Contain("ParagraphEdits.ToggleWidowControl");
            source.Should().Contain("ParagraphEdits.ToggleListKind");
            source.Should().Contain("ParagraphEdits.ToggleBorder");
            source.Should().Contain("ParagraphEdits.ToggleShading");
            source.Should().Contain("ParagraphEdits.SetTabStops");
            source.Should().Contain("ParagraphEdits.ApplyDialogFormatting");
            source.Should().Contain("_editingSession.TryApplyProofingLanguage(");
            source.Should().NotContain("var ranges = plan.Ranges");
            source.Should().NotContain("formatting => formatting with { LanguageTag = plan.LanguageTag }");
            source.Should().NotContain(".Any(p => !p.Formatting.KeepWithNext)");
            source.Should().NotContain(".Any(p => !p.Formatting.KeepLinesTogether)");
            source.Should().NotContain(".Any(p => !p.Formatting.WidowControl)");
        }
    }

    [Fact]
    public void PairedRibbonAdaptersRouteBulletAndNumberCommandsThroughSharedListPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("editor.ToggleList(ListKind.Bullet)");
            source.Should().Contain("editor.ToggleList(ListKind.Number)");
        }

        wpf.Should().NotContain("ToggleBullets: new RoutedEditCommand");
        wpf.Should().NotContain("ToggleNumbering: new RoutedEditCommand");
    }
}
