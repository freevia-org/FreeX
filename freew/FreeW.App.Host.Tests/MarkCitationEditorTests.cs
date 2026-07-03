using System.Linq;
using FreeW.App.Host.Editing;
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

        var toa = view.Model.Blocks
            .Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .OfType<Paragraph>()
            .Select(p => p.PlainText)
            .ToList();

        toa.Should().Contain(TableOfAuthorities.HeadingText);
        toa.Should().Contain("Cases");
        toa.Should().Contain("Roe v. Wade");
        toa.Should().Contain("Statutes");
        toa.Should().Contain("42 U.S.C. § 1983");
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
        entry.PlainText.Should().Be("Formatted Case");
        entry.Runs.Single().Formatting.Bold.Should().BeTrue();
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
        entries.Should().Contain("First Case");
        entries.Should().Contain("Second Statute");
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
                "Late Case");
    }
}
