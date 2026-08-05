namespace FreeW.Core.Model.Tests;

public class CrossReferencesTests
{
    [Fact]
    public void ExplicitPageNumberAtBlock_UsesAuthoredBreaksAndAvoidsUnpaginatedGuesses()
    {
        var unpaginated = TextDocument.CreateEmpty();
        CrossReferences.ExplicitPageNumberAtBlock(unpaginated, 0).Should().BeNull();

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First"));
        document.Blocks.Add(DocumentOps.CreatePageBreak());
        document.Blocks.Add(new Paragraph("Second"));
        document.Blocks.Add(new Paragraph("Third")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true },
        });

        CrossReferences.ExplicitPageNumberAtBlock(document, 0).Should().Be(1);
        CrossReferences.ExplicitPageNumberAtBlock(document, 1).Should().Be(2);
        CrossReferences.ExplicitPageNumberAtBlock(document, 2).Should().Be(2);
        CrossReferences.ExplicitPageNumberAtBlock(document, 3).Should().Be(3);
        CrossReferences.ExplicitPageNumberAtBlock(document, 4).Should().BeNull();

        var evenSection = TextDocument.CreateEmpty();
        evenSection.Blocks.Clear();
        evenSection.Blocks.Add(DocumentOps.CreateSectionBreak(SectionBreakKind.EvenPage));
        evenSection.Blocks.Add(new Paragraph("Even section"));
        CrossReferences.ExplicitPageNumberAtBlock(evenSection, 1).Should().Be(2);

        var oddSection = TextDocument.CreateEmpty();
        oddSection.Blocks.Clear();
        oddSection.Blocks.Add(DocumentOps.CreateSectionBreak(SectionBreakKind.OddPage));
        oddSection.Blocks.Add(new Paragraph("Odd section"));
        CrossReferences.ExplicitPageNumberAtBlock(oddSection, 1).Should().Be(3);
    }

    [Theory]
    [InlineData(CrossRefType.Heading)]
    [InlineData(CrossRefType.Bookmark)]
    [InlineData(CrossRefType.Figure)]
    [InlineData(CrossRefType.Table)]
    [InlineData(CrossRefType.Equation)]
    [InlineData(CrossRefType.Footnote)]
    [InlineData(CrossRefType.Endnote)]
    [InlineData(CrossRefType.NumberedItem)]
    public void Targets_EmptyDocument_YieldsEmpty(CrossRefType type)
    {
        var doc = new TextDocument();

        CrossReferences.Targets(doc, type).Should().BeEmpty();
    }

    [Fact]
    public void Targets_Heading_EnumeratesOutlineHeadingsInOrder()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("My Title") { StyleId = "Title" });
        doc.Blocks.Add(new Paragraph("Intro body")); // excluded: not a heading
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Section A") { StyleId = "Heading2" });

        var targets = CrossReferences.Targets(doc, CrossRefType.Heading);

        targets.Should().Equal(
            new CrossRefTarget("My Title", null, 0),
            new CrossRefTarget("Chapter One", null, 2),
            new CrossRefTarget("Section A", null, 3));
    }

    [Fact]
    public void Targets_Heading_WithBookmark_CarriesAnchor()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1", BookmarkName = "ch1" });

        var targets = CrossReferences.Targets(doc, CrossRefType.Heading);

        targets.Should().ContainSingle()
            .Which.Should().Be(new CrossRefTarget("Chapter One", "ch1", 0));
    }

    [Fact]
    public void Targets_Bookmark_EnumeratesNamedParagraphsWithAnchorAsName()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("First") { BookmarkName = "alpha" });
        doc.Blocks.Add(new Paragraph("No bookmark"));
        doc.Blocks.Add(new Paragraph("Second") { BookmarkName = "beta" });

        var targets = CrossReferences.Targets(doc, CrossRefType.Bookmark);

        targets.Should().Equal(
            new CrossRefTarget("alpha", "alpha", 0),
            new CrossRefTarget("beta", "beta", 2));
    }

    [Fact]
    public void Targets_Bookmark_DeduplicatesRepeatedNames()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("First") { BookmarkName = "dup" });
        doc.Blocks.Add(new Paragraph("Second") { BookmarkName = "dup" });

        CrossReferences.Targets(doc, CrossRefType.Bookmark)
            .Should().ContainSingle()
            .Which.BlockIndex.Should().Be(0);
    }

    [Fact]
    public void Targets_CaptionTypes_EnumerateOnlyMatchingCaptions()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 1, "Diagram"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Table, 2, "Data"));
        doc.Blocks.Add(Captions.BuildCaption(CaptionLabel.Equation, 3, "Energy"));

        var figures = CrossReferences.Targets(doc, CrossRefType.Figure);
        var tables = CrossReferences.Targets(doc, CrossRefType.Table);
        var equations = CrossReferences.Targets(doc, CrossRefType.Equation);

        figures.Should().ContainSingle().Which.Should().Be(new CrossRefTarget("Figure 1: Diagram", null, 1));
        tables.Should().ContainSingle().Which.Should().Be(new CrossRefTarget("Table 2: Data", null, 2));
        equations.Should().ContainSingle().Which.Should().Be(new CrossRefTarget("Equation 3: Energy", null, 3));
    }

    [Fact]
    public void Targets_Footnote_EnumeratesByAscendingIdWithNoteId()
    {
        var doc = new TextDocument();
        doc.Footnotes[2] = new Footnote(2, "second");
        doc.Footnotes[1] = new Footnote(1, "first");

        var targets = CrossReferences.Targets(doc, CrossRefType.Footnote);

        targets.Should().Equal(
            new CrossRefTarget("Footnote 1", null, null, 1),
            new CrossRefTarget("Footnote 2", null, null, 2));
    }

    [Fact]
    public void Targets_Endnote_EnumeratesByAscendingIdWithNoteId()
    {
        var doc = new TextDocument();
        doc.Endnotes[1] = new Endnote(1, "note one");

        CrossReferences.Targets(doc, CrossRefType.Endnote)
            .Should().ContainSingle()
            .Which.Should().Be(new CrossRefTarget("Endnote 1", null, null, 1));
    }

    [Fact]
    public void Targets_NumberedItem_EnumeratesNumberedListParagraphs()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Plain"));
        doc.Blocks.Add(new Paragraph("Step one") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });
        doc.Blocks.Add(new Paragraph("Step two") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });

        var targets = CrossReferences.Targets(doc, CrossRefType.NumberedItem);

        targets.Should().Equal(
            new CrossRefTarget("Step one", null, 1),
            new CrossRefTarget("Step two", null, 2));
    }

    [Fact]
    public void InsertOptions_NotesOfferPageAndAboveBelowButNoNumberSwitches()
    {
        CrossReferences.InsertOptions(CrossRefType.Footnote).Should().Equal(
            CrossRefInsertAs.Text, CrossRefInsertAs.PageNumber, CrossRefInsertAs.AboveBelow);
        CrossReferences.InsertOptions(CrossRefType.Heading).Should().Contain(CrossRefInsertAs.HeadingNumber);
    }

    [Theory]
    [InlineData(CrossRefType.Heading, CrossRefInsertAs.Text, CrossRefFieldKind.Ref)]
    [InlineData(CrossRefType.Heading, CrossRefInsertAs.PageNumber, CrossRefFieldKind.PageRef)]
    [InlineData(CrossRefType.Footnote, CrossRefInsertAs.Text, CrossRefFieldKind.NoteRef)]
    [InlineData(CrossRefType.Footnote, CrossRefInsertAs.PageNumber, CrossRefFieldKind.PageRef)]
    public void FieldKindFor_MapsTypeAndInsertAsToFieldKeyword(
        CrossRefType type, CrossRefInsertAs insertAs, CrossRefFieldKind expected)
    {
        CrossReferences.FieldKindFor(type, insertAs).Should().Be(expected);
    }

    [Fact]
    public void BuildField_BodyTarget_UsesAnchorAsTarget()
    {
        var target = new CrossRefTarget("Chapter One", "ch1", 0);

        var field = CrossReferences.BuildField(CrossRefType.Heading, target, CrossRefInsertAs.PageNumber, hyperlink: true);

        field.Should().Be(new CrossReferenceField(CrossRefFieldKind.PageRef, "ch1", CrossRefInsertAs.PageNumber, Hyperlink: true));
    }

    [Fact]
    public void BuildField_NoteTarget_UsesNoteIdAsTarget()
    {
        var target = new CrossRefTarget("Footnote 3", null, null, 3);

        var field = CrossReferences.BuildField(CrossRefType.Footnote, target, CrossRefInsertAs.Text, hyperlink: false);

        field.Should().Be(new CrossReferenceField(CrossRefFieldKind.NoteRef, "3", CrossRefInsertAs.Text, Hyperlink: false));
    }

    [Fact]
    public void ResolveText_Text_ReturnsTargetDisplay()
    {
        var doc = new TextDocument();
        var target = new CrossRefTarget("Chapter One", "ch1", 0);

        CrossReferences.ResolveText(doc, CrossRefType.Heading, target, CrossRefInsertAs.Text, sourceBlockIndex: 5)
            .Should().Be("Chapter One");
    }

    [Fact]
    public void ResolveText_AboveBelow_ComparesTargetToSource()
    {
        var doc = new TextDocument();

        CrossReferences.ResolveText(doc, CrossRefType.Heading, new CrossRefTarget("H", null, 2), CrossRefInsertAs.AboveBelow, sourceBlockIndex: 5)
            .Should().Be("above");
        CrossReferences.ResolveText(doc, CrossRefType.Heading, new CrossRefTarget("H", null, 9), CrossRefInsertAs.AboveBelow, sourceBlockIndex: 5)
            .Should().Be("below");
    }

    [Fact]
    public void ResolveText_HeadingNumber_BuildsOutlineNumber()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });   // 1
        doc.Blocks.Add(new Paragraph("Section A") { StyleId = "Heading2" });     // 1.1
        doc.Blocks.Add(new Paragraph("Section B") { StyleId = "Heading2" });     // 1.2
        doc.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });   // 2

        CrossReferences.ResolveText(doc, CrossRefType.Heading, new CrossRefTarget("Section B", null, 2), CrossRefInsertAs.HeadingNumber, 0)
            .Should().Be("1.2");
        CrossReferences.ResolveText(doc, CrossRefType.Heading, new CrossRefTarget("Chapter Two", null, 3), CrossRefInsertAs.HeadingNumber, 0)
            .Should().Be("2");
    }

    [Fact]
    public void ResolveText_ParagraphNumber_CountsWithinNumberedRun()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Step one") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });
        doc.Blocks.Add(new Paragraph("Step two") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });

        CrossReferences.ResolveText(doc, CrossRefType.NumberedItem, new CrossRefTarget("Step two", null, 1), CrossRefInsertAs.ParagraphNumber, 0)
            .Should().Be("2)");
    }

    [Fact]
    public void ResolveField_RefText_UsesCurrentBookmarkedParagraphText()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter Two") { BookmarkName = "_Ref1", StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.CrossReferenceFieldRun(
                    new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref1", CrossRefInsertAs.Text, Hyperlink: true),
                    "Chapter One")
            }
        });

        var run = ((Paragraph)doc.Blocks[1]).Runs[0];

        CrossReferences.ResolveField(doc, run.CrossReference!, run.Text, sourceBlockIndex: 1)
            .Should().Be("Chapter Two");
    }

    [Fact]
    public void ResolveField_DanglingRef_PreservesCachedText()
    {
        var doc = new TextDocument();
        var field = new CrossReferenceField(CrossRefFieldKind.Ref, "missing", CrossRefInsertAs.Text, Hyperlink: false);

        CrossReferences.ResolveField(doc, field, "Stale text", sourceBlockIndex: 0)
            .Should().Be("Stale text");
    }

    [Fact]
    public void ResolveField_HeadingAndParagraphNumbers_RecomputeFromCurrentDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Section A") { StyleId = "Heading2", BookmarkName = "sectionA" });
        doc.Blocks.Add(new Paragraph("Step one") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });
        doc.Blocks.Add(new Paragraph("Step two") { BookmarkName = "step2", Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });

        CrossReferences.ResolveField(
                doc,
                new CrossReferenceField(CrossRefFieldKind.Ref, "sectionA", CrossRefInsertAs.HeadingNumber, Hyperlink: false),
                "old",
                sourceBlockIndex: 3)
            .Should().Be("1.1");
        CrossReferences.ResolveField(
                doc,
                new CrossReferenceField(CrossRefFieldKind.Ref, "step2", CrossRefInsertAs.ParagraphNumber, Hyperlink: false),
                "old",
                sourceBlockIndex: 0)
            .Should().Be("2)");
    }

    [Fact]
    public void ResolveField_AboveBelow_AndPageRef_UseCurrentTargetPosition()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Target") { BookmarkName = "target" });
        doc.Blocks.Add(new Paragraph("Reference"));

        CrossReferences.ResolveField(
                doc,
                new CrossReferenceField(CrossRefFieldKind.Ref, "target", CrossRefInsertAs.AboveBelow, Hyperlink: false),
                "below",
                sourceBlockIndex: 1)
            .Should().Be("above");
        CrossReferences.ResolveField(
                doc,
                new CrossReferenceField(CrossRefFieldKind.PageRef, "target", CrossRefInsertAs.PageNumber, Hyperlink: false),
                "1",
                sourceBlockIndex: 1,
                pageOf: block => block == 0 ? 4 : null)
            .Should().Be("4");
    }

    [Fact]
    public void ResolveField_NoteRef_ReturnsCurrentNoteMarkerAndPreservesDanglingCache()
    {
        var doc = new TextDocument();
        doc.Footnotes[10] = new Footnote(10, "first");
        doc.Footnotes[20] = new Footnote(20, "second");
        doc.FootnoteNumbering.StartAt = 3;
        doc.FootnoteNumbering.NumberFormat = NoteNumberFormat.UpperLetter;

        CrossReferences.ResolveField(
                doc,
                new CrossReferenceField(CrossRefFieldKind.NoteRef, "20", CrossRefInsertAs.Text, Hyperlink: true),
                "2",
                sourceBlockIndex: 0)
            .Should().Be("D");
        CrossReferences.ResolveField(
                doc,
                new CrossReferenceField(CrossRefFieldKind.NoteRef, "99", CrossRefInsertAs.Text, Hyperlink: true),
                "stale",
                sourceBlockIndex: 0)
            .Should().Be("stale");
    }

    [Fact]
    public void ReferenceText_ReturnsDisplayForEachTarget()
    {
        CrossReferences.ReferenceText(new CrossRefTarget("Chapter One", "ch1", 0))
            .Should().Be("Chapter One");
        CrossReferences.ReferenceText(new CrossRefTarget("Footnote 3", null, null, 3))
            .Should().Be("Footnote 3");
    }

    [Fact]
    public void PlanInsertion_BodyTargetAllocatesSmallestGapWithoutMutatingDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Heading") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Existing one") { BookmarkName = "_Ref1" });
        doc.Blocks.Add(new Paragraph("Existing three") { BookmarkName = "_Ref3" });

        var plan = CrossReferences.PlanInsertion(
            doc,
            CrossRefType.Heading,
            new CrossRefTarget("Heading", null, 0),
            CrossRefInsertAs.Text,
            hyperlink: true,
            sourceBlockIndex: 3);

        plan.Target.Anchor.Should().Be("_Ref2");
        plan.BookmarkNameToAdd.Should().Be("_Ref2");
        plan.FieldRun.CrossReference.Should().Be(new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref2", CrossRefInsertAs.Text, true));
        plan.FieldRun.Text.Should().Be("Heading");
        ((Paragraph)doc.Blocks[0]).BookmarkName.Should().BeNull();
    }

    [Fact]
    public void PlanInsertion_AllocatesGapAcrossAllBookmarkNames()
    {
        var doc = new TextDocument();
        var target = new Paragraph("Heading");
        target.BookmarkNames.Add("heading");
        target.BookmarkNames.Add("_Ref2");
        doc.Blocks.Add(target);
        doc.Blocks.Add(new Paragraph("Existing one") { BookmarkName = "_Ref1" });
        doc.Blocks.Add(new Paragraph("Existing four") { BookmarkName = "_Ref4" });

        var plan = CrossReferences.PlanInsertion(
            doc,
            CrossRefType.Heading,
            new CrossRefTarget("Heading", null, 0),
            CrossRefInsertAs.Text,
            hyperlink: false,
            sourceBlockIndex: 3);

        plan.BookmarkNameToAdd.Should().Be("_Ref3");
        plan.FieldRun.CrossReference!.Target.Should().Be("_Ref3");
        target.BookmarkNames.Should().Equal("heading", "_Ref2");
    }

    [Fact]
    public void PlanInsertion_AllocatesSmallestGapWhenSecondaryNameUsesRefOne()
    {
        var doc = new TextDocument();
        var target = new Paragraph("Heading");
        target.BookmarkNames.Add("heading");
        target.BookmarkNames.Add("_Ref1");
        doc.Blocks.Add(target);
        doc.Blocks.Add(new Paragraph("Existing three") { BookmarkName = "_Ref3" });

        var plan = CrossReferences.PlanInsertion(
            doc,
            CrossRefType.Heading,
            new CrossRefTarget("Heading", null, 0),
            CrossRefInsertAs.Text,
            hyperlink: false,
            sourceBlockIndex: 2);

        plan.BookmarkNameToAdd.Should().Be("_Ref2");
        target.BookmarkNames.Should().Equal("heading", "_Ref1");
    }

    [Fact]
    public void PlanInsertion_ExistingBodyAnchorIsReused()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Heading") { StyleId = "Heading1", BookmarkName = "chapter" });

        var plan = CrossReferences.PlanInsertion(
            doc,
            CrossRefType.Heading,
            new CrossRefTarget("Heading", "chapter", 0),
            CrossRefInsertAs.PageNumber,
            hyperlink: false,
            sourceBlockIndex: 1);

        plan.BookmarkNameToAdd.Should().BeNull();
        plan.FieldRun.CrossReference.Should().Be(new CrossReferenceField(CrossRefFieldKind.PageRef, "chapter", CrossRefInsertAs.PageNumber, false));
        plan.FieldRun.Text.Should().Be("1");
    }

    [Fact]
    public void PlanInsertion_NoteTargetBuildsNoteFieldWithoutBodyAnchor()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Footnotes[7] = new Footnote(7, "note");

        var plan = CrossReferences.PlanInsertion(
            doc,
            CrossRefType.Footnote,
            new CrossRefTarget("Footnote 7", null, null, 7),
            CrossRefInsertAs.Text,
            hyperlink: true,
            sourceBlockIndex: 0);

        plan.BookmarkNameToAdd.Should().BeNull();
        plan.FieldRun.CrossReference!.Kind.Should().Be(CrossRefFieldKind.NoteRef);
        plan.FieldRun.CrossReference.Target.Should().Be("7");
        plan.FieldRun.Text.Should().Be("Footnote 7");
    }

    [Fact]
    public void PlanInsertion_CachesRelativePositionTextInFieldRun()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Target"));
        doc.Blocks.Add(new Paragraph("Reference"));

        var plan = CrossReferences.PlanInsertion(
            doc,
            CrossRefType.Bookmark,
            new CrossRefTarget("Target", null, 0),
            CrossRefInsertAs.AboveBelow,
            hyperlink: false,
            sourceBlockIndex: 1);

        plan.FieldRun.Text.Should().Be("above");
        plan.FieldRun.CrossReference!.InsertAs.Should().Be(CrossRefInsertAs.AboveBelow);
    }
}
