using System.IO.Compression;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

public sealed class MailMergeRichContentRoundTripTests
{
    [Fact]
    public void NativeSetAndRefFields_RoundTripAndResolveLiteralBookmarkValue()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(
            " SET Department \"engineering team\" \\* MERGEFORMAT ",
            "cached set"));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            " REF Department \\* Upper ",
            "cached reference"));
        var template = new TextDocument { Blocks = { paragraph } };
        using var stream = new MemoryStream();
        DocxWriter.Write(template, stream);
        stream.Position = 0;

        var reopened = DocxReader.Read(stream);
        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(
            reopened,
            new MergeData(["Name"], [["Ada"]]),
            state);

        merged.Should().ContainSingle().Which.PlainText.Should().Be("ENGINEERING TEAM");
        merged[0].Paragraphs.Single().Runs.Should().AllSatisfy(run => run.ComplexField.Should().BeNull());
        state.Bookmarks["Department"].Should().Be("engineering team");
    }

    [Fact]
    public void NativeInteractiveFields_RoundTripDiscoverAndResolveWithCollectedAnswers()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(
            " FILLIN \"Department\" \\d \"Operations\" \\o \\* Upper ",
            "cached department"));
        paragraph.Runs.Add(new Run(" | "));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            " ASK Manager \"Who is the manager?\" \\d \"Alex\" \\o \\* Caps ",
            "cached manager"));
        paragraph.Runs.Add(new Run(" | "));
        paragraph.Runs.Add(Run.ComplexFieldRun(" REF Manager ", "cached reference"));
        paragraph.Runs.Add(new Run(" | «Name»"));
        var template = new TextDocument { Blocks = { paragraph } };
        using var stream = new MemoryStream();
        DocxWriter.Write(template, stream);
        stream.Position = 0;

        var reopened = DocxReader.Read(stream);
        var prompts = MailMergeInteractivePromptPlanner.Plan(reopened);
        var state = new MergeState();
        state.FillInAnswers["Department"] = "Engineering team";
        state.AskAnswers["Manager"] = "margaret hamilton";
        var merged = MailMerge.MergeAllWithRules(
            reopened,
            new MergeData(["Name"], [["Ada"]]),
            state);

        prompts.Should().Equal(
            new MailMergeInteractivePrompt(
                MailMergeInteractivePromptKind.FillIn,
                "Department",
                "Department",
                "Operations"),
            new MailMergeInteractivePrompt(
                MailMergeInteractivePromptKind.Ask,
                "Manager",
                "Who is the manager?",
                "Alex"));
        merged.Should().ContainSingle().Which.PlainText.Should().Be(
            "ENGINEERING TEAM |  | Margaret Hamilton | Ada");
        merged[0].Paragraphs.Single().Runs.Should().AllSatisfy(run => run.ComplexField.Should().BeNull());
        state.Bookmarks["Manager"].Should().Be("Margaret Hamilton");
    }

    [Fact]
    public void MergedRichRuns_SubstituteNestedTextAndSurviveDocxRoundTrip()
    {
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(new SmartArtNode("Lead «Name»", [new SmartArtNode("Member «Name»")]));
        var group = new DrawingGroup();
        group.Children.Add(Shape.TextBoxWith("Grouped «Name»", 90, 32));
        group.Children.Add(new WordArt("Banner «Name»"));
        group.ChildOffsets.Add((0, 0));
        group.ChildOffsets.Add((92, 0));
        var chart = Chart.Create(
            ChartKind.Column,
            ["Category «Name»"],
            [1d],
            seriesName: "Series «Name»",
            title: "Chart «Name»");
        chart.CategoryAxisTitle = "Category axis «Name»";
        chart.ValueAxisTitle = "Value axis «Name»";

        var paragraph = new Paragraph("Dear «Name»");
        paragraph.Runs.Add(Run.FromEquation(Equation.FromText("x+1")));
        paragraph.Runs.Add(Run.FromShape(Shape.TextBoxWith("Box «Name»", 120, 40)));
        paragraph.Runs.Add(Run.FromWordArt(new WordArt("Art «Name»")));
        paragraph.Runs.Add(Run.FromSmartArt(smartArt));
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        paragraph.Runs.Add(Run.FromChart(chart));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        var template = new TextDocument { Blocks = { paragraph } };
        template.Properties.Title = "Rich merge";
        template.Footnotes[1] = new Footnote(1, "Footnote «Name»");

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["Name"] = "Ada" });
        using var stream = new MemoryStream();
        DocxWriter.Write(merged, stream);
        stream.Position = 0;

        var reopened = DocxReader.Read(stream);
        var runs = reopened.Paragraphs.Single().Runs;

        runs[0].Text.Should().Be("Dear Ada");
        runs[1].Equation.Should().NotBeNull();
        runs[2].Shape!.PlainText.Should().Be("Box Ada");
        runs[3].WordArt!.Text.Should().Be("Art Ada");
        var reopenedSmartArt = runs[4].SmartArt!;
        reopenedSmartArt.Nodes[0].Text.Should().Be("Lead Ada");
        reopenedSmartArt.Nodes[0].Children[0].Text.Should().Be("Member Ada");
        var reopenedGroup = runs[5].DrawingGroup!;
        ((Shape)reopenedGroup.Children[0]).PlainText.Should().Be("Grouped Ada");
        ((WordArt)reopenedGroup.Children[1]).Text.Should().Be("Banner Ada");
        var reopenedChart = runs[6].Chart!;
        reopenedChart.Title.Should().Be("Chart Ada");
        reopenedChart.CategoryAxisTitle.Should().Be("Category axis Ada");
        reopenedChart.ValueAxisTitle.Should().Be("Value axis Ada");
        reopenedChart.Categories.Should().Equal("Category Ada");
        reopenedChart.Series.Single().Name.Should().Be("Series Ada");
        runs[7].FootnoteId.Should().Be(1);
        reopened.Footnotes[1].PlainText.Should().Be("Footnote Ada");
        reopened.Properties.Title.Should().Be("Rich merge");
    }

    [Fact]
    public void MergedPreservedDrawing_CarriesReferencedPackageGraphThroughDocxRoundTrip()
    {
        const string chartRelationship =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        const string chartContentType =
            "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
        var template = new TextDocument();
        template.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/chart1.xml",
            System.Text.Encoding.UTF8.GetBytes(
                "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/>"),
            chartContentType,
            chartRelationship));
        template.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/_rels/chart1.xml.rels",
            System.Text.Encoding.UTF8.GetBytes(
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"image\" Target=\"../media/image1.png\" /></Relationships>")));
        template.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", [1, 2, 3]));
        template.Preserved.ContentTypeDefaults["png"] = "image/png";
        var paragraph = new Paragraph("Chart for «Name»");
        paragraph.Runs.Add(Run.FromPreservedDrawing(new PreservedDrawing(
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId7\" /></w:drawing>",
            [new PreservedDrawingReference("rId7", "/word/charts/chart1.xml", chartRelationship)])));
        template.Blocks.Add(paragraph);

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["Name"] = "Ada" });

        merged.Preserved.Should().NotBeSameAs(template.Preserved);
        merged.Preserved.Parts.Should().HaveCount(3);
        merged.Preserved.Parts[0].Bytes.Should().NotBeSameAs(template.Preserved.Parts[0].Bytes);
        using var stream = new MemoryStream();
        DocxWriter.Write(merged, stream);
        var bytes = stream.ToArray();

        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/charts/chart1.xml").Should().NotBeNull();
            zip.GetEntry("word/charts/_rels/chart1.xml.rels").Should().NotBeNull();
            zip.GetEntry("word/media/image1.png").Should().NotBeNull();
        }

        var reopened = DocxReader.Read(new MemoryStream(bytes));
        reopened.Paragraphs.Single().PlainText.Should().Be("Chart for Ada");
        reopened.Paragraphs.Single().Runs[1].PreservedDrawing.Should().NotBeNull();
        reopened.Preserved.Parts.Should().Contain(part => part.PartName == "/word/charts/chart1.xml");
        reopened.Preserved.Parts.Should().Contain(part => part.PartName == "/word/media/image1.png");
    }

    [Fact]
    public void CombinedLetters_RemapRecipientFootnotesThroughDocxRoundTrip()
    {
        var template = new TextDocument();
        var paragraph = new Paragraph("Dear «Name»");
        paragraph.Runs.Add(Run.FootnoteReference(1));
        template.Blocks.Add(paragraph);
        template.Footnotes[1] = new Footnote(1, "Private note for «Name»");
        var records = MailMerge.MergeAll(
            template,
            new MergeData(["Name"], [["Ada"], ["Grace"]]));
        var combined = MailMerge.CombineMergedRecords(records, MailMergeOutputMode.Letters);
        using var stream = new MemoryStream();
        DocxWriter.Write(combined, stream);
        stream.Position = 0;

        var reopened = DocxReader.Read(stream);

        reopened.Footnotes.Keys.Should().BeEquivalentTo([1, 2]);
        reopened.Footnotes[1].PlainText.Should().Be("Private note for Ada");
        reopened.Footnotes[2].PlainText.Should().Be("Private note for Grace");
        reopened.Paragraphs
            .SelectMany(item => item.Runs)
            .Where(run => run.FootnoteId is not null)
            .Select(run => run.FootnoteId)
            .Should().Equal(1, 2);
    }
}
