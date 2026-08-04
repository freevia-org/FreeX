using System.Linq;
using System.Xml.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the live multilevel-list outline markers (the "1.1.1" accumulated text the editor now
/// shows on screen, matching what FreeW writes to <c>numbering.xml</c>). The accumulation logic
/// (<see cref="DocumentView.MultiLevelMarkerSequence"/>) is pure and tested directly; the render/commit
/// behaviour (markers appear in the surface but never enter the model, and the list level round-trips)
/// is tested on an STA thread via <c>[StaFact]</c>.
/// </summary>
public sealed class MultiLevelMarkerTests
{
    // ---- Pure accumulation logic --------------------------------------------------------------------

    [Fact]
    public void SingleLevel_ProducesPlainDecimalSequence()
    {
        var markers = DocumentView.MultiLevelMarkerSequence(new[] { 0, 0, 0 });

        markers.Should().Equal("1.", "2.", "3.");
    }

    [Fact]
    public void Nesting_AccumulatesAncestorCounters()
    {
        // 1.
        //   1.1.
        //     1.1.1.
        //   1.2.
        // 2.
        var levels = new[] { 0, 1, 2, 1, 0 };

        var markers = DocumentView.MultiLevelMarkerSequence(levels);

        markers.Should().Equal("1.", "1.1.", "1.1.1.", "1.2.", "2.");
    }

    [Fact]
    public void Nesting_UsesPerLevelNumberFormats()
    {
        var levels = new[] { 0, 1, 2, 1, 0 };

        var markers = DocumentView.MultiLevelMarkerSequence(
            levels,
            MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats);

        markers.Should().Equal("1.", "1.a.", "1.a.i.", "1.b.", "2.");
    }

    [Fact]
    public void DeeperLevel_ResetsAllDescendantCounters()
    {
        // 1.
        //   1.1.
        //     1.1.1.
        // 2.        (back to level 0 resets levels 1 and 2)
        //   2.1.    (level-1 counter restarts at 1, not continuing 2)
        var levels = new[] { 0, 1, 2, 0, 1 };

        var markers = DocumentView.MultiLevelMarkerSequence(levels);

        markers.Should().Equal("1.", "1.1.", "1.1.1.", "2.", "2.1.");
    }

    [Fact]
    public void JumpingIntoDeepLevelFirst_ShowsAncestorStartValues()
    {
        // A list that starts at level 2 with no level-0/1 ancestors shows the ancestors at their start (1),
        // matching Word, rather than printing a "0.0." prefix.
        var markers = DocumentView.MultiLevelMarkerSequence(new[] { 2 });

        markers.Should().Equal("1.1.1.");
    }

    [Fact]
    public void LevelsBeyondDepth_AreClampedNotCrashing()
    {
        // Level 99 clamps to the deepest supported level (8 -> nine dotted counters).
        var markers = DocumentView.MultiLevelMarkerSequence(new[] { 99 });

        markers.Should().ContainSingle()
            .Which.Should().Be("1.1.1.1.1.1.1.1.1.");
    }

    [Fact]
    public void EmptyInput_ProducesEmptySequence()
    {
        DocumentView.MultiLevelMarkerSequence(System.Array.Empty<int>()).Should().BeEmpty();
    }

    // ---- Render + commit behaviour ------------------------------------------------------------------

    private static TextDocument BuildMultiLevelDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        (string Text, int Level)[] items =
        {
            ("Alpha", 0),
            ("Beta", 1),
            ("Gamma", 2),
            ("Delta", 0)
        };
        foreach (var (text, level) in items)
        {
            doc.Blocks.Add(new Paragraph(text)
            {
                Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = level }
            });
        }
        return doc;
    }

    [StaFact]
    public void AccumulatedMarkersAppearInTheRenderedSurface()
    {
        var view = new DocumentView();
        view.LoadModel(BuildMultiLevelDoc());

        var rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;

        rendered.Should().Contain("1.");
        rendered.Should().Contain("1.1.");
        rendered.Should().Contain("1.1.1.");
        rendered.Should().Contain("2."); // the final level-0 item restarts the top counter at 2
    }

    [StaFact]
    public void AccumulatedMarkersUseACompactSeparator()
    {
        var view = new DocumentView();
        view.LoadModel(BuildMultiLevelDoc());

        var list = view.Document.Blocks.OfType<List>().Should().ContainSingle().Which;
        list.ListItems.Cast<ListItem>().First().Blocks.OfType<System.Windows.Documents.Paragraph>().Single()
            .Inlines.OfType<System.Windows.Documents.Run>().First()
            .Text.Should().Be("1. ");
    }

    [StaFact]
    public void StyledMarkersAppearInTheRenderedSurface()
    {
        var view = new DocumentView();
        view.LoadModel(BuildMultiLevelDoc());
        view.ApplyMultiLevelNumberFormats(MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats);

        var rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;

        rendered.Should().Contain("1.a.");
        rendered.Should().Contain("1.a.i.");
    }

    [StaFact]
    public void Markers_AreViewOnly_AndNeverEnterTheModelOnCommit()
    {
        var view = new DocumentView();
        view.LoadModel(BuildMultiLevelDoc());
        view.CommitToModel();

        var paragraphs = view.Model.Blocks.OfType<Paragraph>().ToList();

        // The synthetic "1.1.1" marker runs are dropped on commit: each paragraph's text is exactly its
        // original content, with no accumulated number prefixed into the model.
        paragraphs.Select(p => p.PlainText).Should().Equal("Alpha", "Beta", "Gamma", "Delta");
    }

    [StaFact]
    public void ListLevel_RoundTripsThroughCommit()
    {
        // The editor flattens a list run into one WPF List, so the nesting depth has no structural slot;
        // it is carried on the paragraph Tag so it survives commit (keeping the markers stable after edits).
        var view = new DocumentView();
        view.LoadModel(BuildMultiLevelDoc());
        view.CommitToModel();

        var levels = view.Model.Blocks.OfType<Paragraph>().Select(p => p.Formatting.ListLevel).ToList();

        levels.Should().Equal(0, 1, 2, 0);
        view.Model.Blocks.OfType<Paragraph>()
            .Should().OnlyContain(p => p.Formatting.ListKind == ListKind.MultiLevel);
    }

    [StaFact]
    public void ListStartOverride_RoundTripsThroughCommit()
    {
        var document = BuildMultiLevelDoc();
        var paragraph = document.Blocks.OfType<Paragraph>().ElementAt(1);
        paragraph.Formatting = paragraph.Formatting with { ListStartOverride = 5 };
        var view = new DocumentView();
        view.LoadModel(document);

        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>().ElementAt(1).Formatting.ListStartOverride.Should().Be(5);
    }

    [StaFact]
    public void PreservedStyleNumbering_AppearsAsViewOnlyMarkerAndSurvivesCommit()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        document.Styles["Legal"] = new DocumentStyle
        {
            Id = "Legal",
            Name = "Legal",
            PreservedNumbering = new PreservedNumbering(2, 0)
        };
        document.Preserved.OriginalNumbering = XElement.Parse(
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="10">
                <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="Section %1."/></w:lvl>
              </w:abstractNum>
              <w:num w:numId="2"><w:abstractNumId w:val="10"/></w:num>
            </w:numbering>
            """);
        document.Blocks.Add(new Paragraph("First") { StyleId = "Legal" });
        document.Blocks.Add(new Paragraph("Second") { StyleId = "Legal" });

        var view = new DocumentView();
        view.LoadModel(document);

        var rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        rendered.Should().Contain("Section I.");
        rendered.Should().Contain("Section II.");

        view.CommitToModel();
        view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("First", "Second");
        view.Model.Styles["Legal"].PreservedNumbering.Should().Be(new PreservedNumbering(2, 0));
    }

    [StaFact]
    public void PreservedNumbering_InTableCellAppearsAsViewOnlyMarkerAndContinuesBodySequence()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        document.Preserved.OriginalNumbering = XElement.Parse(
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="10">
                <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="Section %1."/></w:lvl>
              </w:abstractNum>
              <w:num w:numId="2"><w:abstractNumId w:val="10"/></w:num>
            </w:numbering>
            """);
        document.Blocks.Add(new Paragraph("Before") { PreservedNumbering = new PreservedNumbering(2, 0) });
        var table = new Table();
        var row = new FreeW.Core.Model.TableRow();
        var cell = new FreeW.Core.Model.TableCell();
        cell.Paragraphs.Add(new Paragraph("Inside") { PreservedNumbering = new PreservedNumbering(2, 0) });
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph("After") { PreservedNumbering = new PreservedNumbering(2, 0) });

        var view = new DocumentView();
        view.LoadModel(document);

        var rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        rendered.Should().Contain("Section I.");
        rendered.Should().Contain("Section II.");
        rendered.Should().Contain("Section III.");

        view.CommitToModel();
        view.Model.Blocks.OfType<Table>().Single().Rows.Single().Cells.Single().PlainText.Should().Be("Inside");
    }
}
