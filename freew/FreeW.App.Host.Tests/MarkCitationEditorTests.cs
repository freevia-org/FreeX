using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA editor coverage for Mark Citation / Table of Authorities (<see cref="DocumentView.MarkCitation"/>,
/// <see cref="DocumentView.InsertTableOfAuthorities"/>, <see cref="DocumentView.RefreshTableOfAuthorities"/>).
/// These run on an STA thread (<c>[StaFact]</c>) because the RichTextBox/FlowDocument need STA + a
/// Dispatcher. Mirrors the existing footnote/index editor tests.
/// </summary>
public sealed class MarkCitationEditorTests
{
    private static DocumentView LoadedView(out TextDocument model)
    {
        model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("Body text"));
        var view = new DocumentView();
        view.LoadModel(model);
        return view;
    }

    [StaFact]
    public void MarkCitation_DropsAHiddenCitationMarkThatSurvivesCommit()
    {
        var view = LoadedView(out _);

        view.MarkCitation(new Citation("Brown v. Board, 347 U.S. 483 (1954)", CitationCategory.Cases, "Brown"));
        view.CommitToModel();

        var marks = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.Citation is not null)
            .ToList();
        marks.Should().ContainSingle();
        var citation = marks[0].Citation!;
        citation.LongCitation.Should().Be("Brown v. Board, 347 U.S. 483 (1954)");
        citation.Category.Should().Be(CitationCategory.Cases);
        // The mark carries no visible text.
        marks[0].Text.Should().BeEmpty();
    }

    [StaFact]
    public void MarkCitation_IgnoresABlankLongCitation()
    {
        var view = LoadedView(out _);

        view.MarkCitation(new Citation("   ", CitationCategory.Cases));
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Should().NotContain(r => r.Citation != null);
    }

    [StaFact]
    public void InsertTableOfAuthorities_BuildsAGroupedTableFromTheMarks()
    {
        var view = LoadedView(out _);
        view.MarkCitation(new Citation("Roe v. Wade", CitationCategory.Cases));
        view.MarkCitation(new Citation("42 U.S.C. § 1983", CitationCategory.Statutes));

        view.InsertTableOfAuthorities();
        view.CommitToModel();

        var toaParagraphs = view.Model.Blocks
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .OfType<Paragraph>()
            .ToList();
        var toa = toaParagraphs.Select(p => p.PlainText).ToList();

        toa.Should().Contain(TableOfAuthorities.HeadingText);
        toa.Should().Contain("Cases");
        toa.Should().Contain("Roe v. Wade\t1");
        toa.Should().Contain("Statutes");
        toa.Should().Contain("42 U.S.C. § 1983\t1");
        toaParagraphs[0].SpanningFieldOwner.Should().BeNull();
        toaParagraphs.Skip(1).Select(paragraph => paragraph.SpanningFieldOwner!.Instruction)
            .Should().Equal(
                " TOA \\h \\c \"1\" \\f ",
                " TOA \\h \\c \"1\" \\f ",
                " TOA \\h \\c \"2\" \\f ",
                " TOA \\h \\c \"2\" \\f ");
        toaParagraphs[1].SpanningFieldStart.Should().NotBeNull();
        toaParagraphs[2].EndsSpanningField.Should().BeTrue();
        toaParagraphs[3].SpanningFieldStart.Should().NotBeNull();
        toaParagraphs[^1].EndsSpanningField.Should().BeTrue();
    }

    [StaFact]
    public void InsertTableOfAuthorities_WithOptions_CarriesLeaderThroughWpfHost()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        var mark = Run.CitationMark(new Citation("Formatted Case", CitationCategory.Cases));
        mark.Formatting = new RunFormatting { Bold = true };
        model.Blocks.Add(new Paragraph { Runs = { mark } });
        var view = new DocumentView();
        view.LoadModel(model);

        view.InsertTableOfAuthorities(new ToaOptions
        {
            KeepOriginalFormatting = true,
            TabLeader = ToaTabLeader.Underline
        });
        view.CommitToModel();

        var entry = view.Model.Blocks.OfType<Paragraph>()
            .Single(p => p.StyleId == TableOfAuthorities.EntryStyleId);
        entry.Formatting.TabStops.Should().ContainSingle()
            .Which.Leader.Should().Be(TabLeader.Underline);
        entry.PlainText.Should().Be("Formatted Case\t1");
        entry.Runs.Select(run => run.Text).Should().Equal("Formatted Case", "\t", "1");
        entry.Runs[0].Formatting.Bold.Should().BeTrue();
    }

    [StaFact]
    public void InsertTableOfAuthorities_UsesSharedExplicitBreakPageReferencesInWpfHost()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(CitationMarkParagraph("Brown v. Board", formatted: false));
        model.Blocks.Add(DocumentOps.CreatePageBreak());
        model.Blocks.Add(CitationMarkParagraph("Brown v. Board", formatted: false));
        var view = new DocumentView();
        view.LoadModel(model);

        view.InsertTableOfAuthorities();
        view.CommitToModel();

        var entry = view.Model.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.PlainText.Should().Be("Brown v. Board\t1, 2");
        entry.Runs.Select(run => run.Text).Should().Equal("Brown v. Board", "\t", "1, 2");
    }

    [StaFact]
    public void InsertTableOfAuthorities_StabilizesPageReferencesAfterRegionReflow()
    {
        var model = ReflowingTableOfAuthoritiesDocument(includeExistingRegion: false);
        var view = new DocumentView();
        view.LoadModel(model);

        view.InsertTableOfAuthorities();
        view.CommitToModel();
        var firstPass = TableOfAuthoritiesEntries(view.Model);

        view.RefreshTableOfAuthorities();
        view.CommitToModel();
        var secondPass = TableOfAuthoritiesEntries(view.Model);

        firstPass.Should().Equal(secondPass);
        firstPass.Should().Equal(ExpectedReflowEntries());
    }

    [StaFact]
    public void RefreshTableOfAuthorities_StabilizesPageReferencesAfterReplacementReflow()
    {
        var model = ReflowingTableOfAuthoritiesDocument(includeExistingRegion: true);
        var view = new DocumentView();
        view.LoadModel(model);

        view.RefreshTableOfAuthorities();
        view.CommitToModel();
        var firstPass = TableOfAuthoritiesEntries(view.Model);

        view.RefreshTableOfAuthorities();
        view.CommitToModel();
        var secondPass = TableOfAuthoritiesEntries(view.Model);

        firstPass.Should().Equal(secondPass);
        firstPass.Should().Equal(ExpectedReflowEntries());
    }

    [StaFact]
    public void InsertTableOfAuthorities_StabilizesSectionFormattedPageReferences()
    {
        var model = ReflowingTableOfAuthoritiesDocument(includeExistingRegion: false);
        model.Page.PageNumberStartAt = 9;
        model.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        var view = new DocumentView();
        view.LoadModel(model);

        view.InsertTableOfAuthorities();
        view.CommitToModel();
        var firstPass = TableOfAuthoritiesEntries(view.Model);

        view.RefreshTableOfAuthorities();
        view.CommitToModel();

        firstPass.Should().Equal(Enumerable.Range(1, 8).Select(index => $"Reflow Case {index}\tXI"));
        TableOfAuthoritiesEntries(view.Model).Should().Equal(firstPass);
    }

    [StaFact]
    public void UpdateFields_RefreshesExistingTableOfAuthoritiesWithExplicitBreakPageReferences()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("Before"));
        model.Blocks.Add(CitationMarkParagraph("Case A", formatted: false));
        model.Blocks.Add(DocumentOps.CreatePageBreak());
        model.Blocks.Add(CitationMarkParagraph("Case A", formatted: false));
        model.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));
        model.Blocks.Add(new Paragraph("After"));
        var view = new DocumentView();
        view.LoadModel(model);

        view.UpdateFields();
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Case A\t1, 2");
        view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Old Case")
            .And.EndWith("After");

        var entry = view.Model.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.Runs.Select(run => run.Text).Should().Equal("Case A", "\t", "1, 2");
    }

    [StaFact]
    public void UpdateFields_RefreshesExistingTableOfAuthoritiesWithOverflowPageReferences()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(CitationMarkParagraph("Overflow Case", formatted: false));
        for (var i = 0; i < 120; i++)
            model.Blocks.Add(new Paragraph($"Overflow filler {i + 1}: The quick brown fox jumps over the lazy dog."));
        model.Blocks.Add(CitationMarkParagraph("Overflow Case", formatted: false));
        model.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));

        var view = new DocumentView();
        view.LoadModel(model);

        view.UpdateFields();
        view.CommitToModel();

        var entry = view.Model.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.PlainText.Should().MatchRegex(@"^Overflow Case\t1, [2-9][0-9]*$");
        entry.Runs.Select(run => run.Text).Should().HaveCount(3);
    }

    [StaFact]
    public void RefreshTableOfAuthorities_UsesDirectAndNestedPaginatedTableCitationPages()
    {
        var model = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var table = model.Blocks.OfType<Table>().Single();
        model.Blocks.Insert(model.Blocks.IndexOf(table), DocumentOps.CreatePageBreak());
        table.Rows[1].Cells[0].Paragraphs[0] = CitationMarkParagraph("Table Case", formatted: false);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = CitationMarkParagraph("Table Case", formatted: false);
        table.Rows[8].Cells[0].NestedTables.Add(nested);
        var oldRegion = TableOfAuthorities.Build(new[] { new Citation("Old Case", CitationCategory.Cases) });
        model.Blocks.AddRange(oldRegion);
        model.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        model.Page.PageNumberStartAt = 4;
        var view = new DocumentView();
        view.LoadModel(model);

        view.CommitToModel();
        var markedAddresses = new List<TableParagraphAddress?>();
        _ = TableOfAuthorities.BuildWithTableAddresses(
            view.Model,
            ToaOptions.Default,
            (_, _, tableParagraph, _, _) =>
            {
                markedAddresses.Add(tableParagraph);
                return TableOfAuthorities.CreatePageReference(1);
            });
        markedAddresses.Should().HaveCount(2);
        markedAddresses.Should().NotContain((TableParagraphAddress?)null);

        view.RefreshTableOfAuthorities();
        view.CommitToModel();

        var entry = view.Model.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.PlainText.Should().Be("Table Case\tV, VI");
        view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Old Case");
    }

    [StaFact]
    public void RefreshTableOfAuthorities_UsesDistinctPagesForPassimInWpfHost()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Page.WidthPt = 700;
        model.Page.MarginLeftPt = 80;
        model.Page.MarginRightPt = 90;
        model.Blocks.Add(new Paragraph("Before"));
        for (var i = 0; i < 5; i++)
            model.Blocks.Add(CitationMarkParagraph("Roe v. Wade", i == 0));
        model.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));
        model.Blocks.Add(new Paragraph("After"));

        var view = new DocumentView();
        view.LoadModel(model);

        view.RefreshTableOfAuthorities(new ToaOptions
        {
            CategoryFilter = CitationCategory.Cases,
            KeepOriginalFormatting = true,
            UsePassim = true,
            TabLeader = ToaTabLeader.Dashes
        });
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>()
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Table of Authorities", "Cases", "Roe v. Wade\t1");
        view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Old Case")
            .And.EndWith("After");

        var entry = view.Model.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId);
        entry.Formatting.TabStops.Should().Equal(
            new TabStop(530, TabStopAlignment.Right, TabLeader.Dashes));
        entry.Runs.Select(run => run.Text).Should().Equal("Roe v. Wade", "\t", "1");
        view.Model.Blocks.OfType<Paragraph>()
            .Single(paragraph => paragraph.StyleId == TableOfAuthorities.CategoryStyleId)
            .SpanningFieldStart!.Instruction.Should().Be(" TOA \\h \\c \"1\" \\p ");
        var entryFormatting = entry.Runs[0].Formatting;
        entryFormatting.Bold.Should().BeTrue();
        entryFormatting.Underline.Should().BeTrue();
        entryFormatting.ColorHex.Should().Be("#C00000");
    }

    [StaFact]
    public void RefreshTableOfAuthorities_ReplacesThePriorRegionInPlaceWithoutDuplicating()
    {
        var view = LoadedView(out _);
        view.MarkCitation(new Citation("First Case", CitationCategory.Cases));
        view.MarkCitation(new Citation("Second Statute", CitationCategory.Statutes));
        view.InsertTableOfAuthorities();
        view.CommitToModel();

        // Refresh after the region already exists: it must rebuild the same single region in place, not add a
        // second copy.
        view.RefreshTableOfAuthorities();
        view.CommitToModel();

        var headings = view.Model.Blocks
            .OfType<Paragraph>()
            .Count(p => p.StyleId == TableOfAuthorities.HeadingStyleId);
        headings.Should().Be(1); // exactly one table, not two

        var entries = view.Model.Blocks
            .OfType<Paragraph>()
            .Where(p => p.StyleId == TableOfAuthorities.EntryStyleId)
            .Select(p => p.PlainText)
            .ToList();
        entries.Should().Contain("First Case\t1");
        entries.Should().Contain("Second Statute\t1");
    }

    [StaFact]
    public void RefreshTableOfAuthorities_WithoutExistingRegion_AppendsAtDocumentEnd()
    {
        var view = LoadedView(out _);
        view.MarkCitation(new Citation("Late Case", CitationCategory.Cases));

        view.RefreshTableOfAuthorities();
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal(
                "Body text",
                "Table of Authorities",
                "Cases",
                "Late Case\t1");
    }

    private static Paragraph CitationMarkParagraph(string longCitation, bool formatted)
    {
        var mark = Run.CitationMark(new Citation(longCitation, CitationCategory.Cases));
        if (formatted)
            mark.Formatting = new RunFormatting { Bold = true, Underline = true, ColorHex = "#C00000" };
        return new Paragraph { Runs = { mark } };
    }

    private static TextDocument ReflowingTableOfAuthoritiesDocument(bool includeExistingRegion)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Page.WidthPt = 300;
        document.Page.HeightPt = 180;
        document.Page.MarginTopPt = 12;
        document.Page.MarginBottomPt = 12;
        document.Page.MarginLeftPt = 18;
        document.Page.MarginRightPt = 18;
        if (includeExistingRegion)
        {
            document.Blocks.AddRange(TableOfAuthorities.Build(
                new[] { new Citation("Old Case", CitationCategory.Cases) }));
        }

        for (var i = 0; i < 8; i++)
            document.Blocks.Add(CitationMarkParagraph($"Reflow Case {i + 1}", formatted: false));
        return document;
    }

    private static string[] TableOfAuthoritiesEntries(TextDocument document) =>
        document.Blocks.OfType<Paragraph>()
            .Where(paragraph => paragraph.StyleId == TableOfAuthorities.EntryStyleId)
            .Select(paragraph => paragraph.PlainText)
            .ToArray();

    private static string[] ExpectedReflowEntries() =>
        Enumerable.Range(1, 8)
            .Select(index => $"Reflow Case {index}\t3")
            .ToArray();
}
