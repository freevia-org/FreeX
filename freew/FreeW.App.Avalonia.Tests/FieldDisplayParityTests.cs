using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class FieldDisplayParityTests
{
    [Fact]
    public void InsertComplexField_Template_ResolvesResultFromExtendedProperties()
    {
        var document = TextDocument.CreateEmpty();
        document.Preserved.Parts.Add(new PreservedPart(
            "/docProps/app.xml",
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Template>Proposal.dotx</Template>
                </Properties>
                """)));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.InsertComplexField("TEMPLATE");

        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "TEMPLATE")
            .Text.Should().Be("Proposal.dotx");
    }

    [Fact]
    public void UpdateFields_DoesNotRecomputeLockedImportedSimpleField()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });
        var field = new Run("Locked chapter")
        {
            ComplexField = new ComplexField(
                " STYLEREF 1 ",
                SimpleField: new SimpleFieldMetadata(IsLocked: true, IsDirty: true))
        };
        document.Blocks.Add(new Paragraph { Runs = { field } });

        var view = new DocumentView();
        view.LoadDocument(document);
        view.UpdateFields();

        field.Text.Should().Be("Locked chapter");
        field.ComplexField!.SimpleField.Should().Be(new SimpleFieldMetadata(true, true));
    }

    [Fact]
    public void UpdateFields_DistinguishesDateAndTimeForSimpleAndComplexFields()
    {
        var simpleDate = new Run("stale simple date") { FieldKind = RunFieldKind.Date };
        var simpleTime = new Run("stale simple time") { FieldKind = RunFieldKind.Time };
        var complexDate = Run.ComplexFieldRun(" DATE ", "stale complex date");
        var complexTime = Run.ComplexFieldRun(" TIME ", "stale complex time");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { simpleDate, simpleTime, complexDate, complexTime }
        });
        var before = DateTime.Now;

        var view = new DocumentView();
        view.LoadDocument(document);
        view.UpdateFields();

        var after = DateTime.Now;
        var expectedDates = new[] { before, after }
            .Select(value => ComplexFieldDisplayPlanner.FormatInvariantTemporalValue(RunFieldKind.Date, value))
            .Distinct()
            .ToArray();
        var expectedTimes = new[] { before, after }
            .Select(value => ComplexFieldDisplayPlanner.FormatInvariantTemporalValue(RunFieldKind.Time, value))
            .Distinct()
            .ToArray();

        simpleDate.Text.Should().BeOneOf(expectedDates);
        complexDate.Text.Should().BeOneOf(expectedDates);
        simpleTime.Text.Should().BeOneOf(expectedTimes);
        complexTime.Text.Should().BeOneOf(expectedTimes);
        simpleTime.Text.Should().Be(complexTime.Text);
        simpleTime.Text.Should().MatchRegex(@"^\d{1,2}:\d{2} (AM|PM)$");
        simpleDate.Text.Should().MatchRegex(@"^\d{1,2}/\d{1,2}/\d{4}$");
    }

    [Fact]
    public void UpdateFields_AppliesDateAndTimePictureSwitches()
    {
        var before = DateTime.Now;
        var date = Run.ComplexFieldRun(" DATE \\@ \"yyyy-MM-dd\" ", "stale date");
        var time = Run.ComplexFieldRun(" TIME \\@ \"HH:mm\" ", "stale time");
        var created = Run.ComplexFieldRun(" CREATEDATE \\@ \"yyyy-MM-dd\" ", "stale created");
        var saved = Run.ComplexFieldRun(" SAVEDATE \\@ \"yyyy-MM-dd HH:mm\" ", "stale saved");
        var owner = Run.ComplexFieldRun(" LASTSAVEDBY ", "stale owner");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var metadataMoment = new DateTime(2026, 8, 6, 14, 5, 0);
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(metadataMoment);
        document.Properties.Created = new DateTimeOffset(metadataMoment, localOffset);
        document.Properties.Modified = new DateTimeOffset(metadataMoment.AddDays(2), localOffset);
        document.Properties.LastModifiedBy = "Ada Lovelace";
        document.Blocks.Add(new Paragraph { Runs = { date, time, created, saved, owner } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        var after = DateTime.Now;
        date.Text.Should().BeOneOf(before.ToString("yyyy-MM-dd"), after.ToString("yyyy-MM-dd"));
        time.Text.Should().BeOneOf(before.ToString("HH:mm"), after.ToString("HH:mm"));
        created.Text.Should().Be("2026-08-06");
        saved.Text.Should().Be("2026-08-08 14:05");
        owner.Text.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void UpdateFields_RefreshesDocPropertyAndDocVariableFromDocumentPackageState()
    {
        var word = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var title = Run.ComplexFieldRun(" DOCPROPERTY Title ", "stale title");
        var company = Run.ComplexFieldRun(" DOCPROPERTY Company ", "stale company");
        var manager = Run.ComplexFieldRun(" DOCPROPERTY Manager ", "stale manager");
        var templateProperty = Run.ComplexFieldRun(" DOCPROPERTY Template ", "stale property template");
        var template = Run.ComplexFieldRun(" TEMPLATE ", "stale template");
        var templatePath = Run.ComplexFieldRun(" TEMPLATE \\p ", @"C:\Templates\Proposal.dotx");
        var channel = Run.ComplexFieldRun(" DOCVARIABLE Channel ", "stale channel");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Properties.Title = "Current title";
        document.Preserved.OriginalSettings = new System.Xml.Linq.XElement(
            word + "settings",
            new System.Xml.Linq.XElement(
                word + "docVars",
                new System.Xml.Linq.XElement(
                    word + "docVar",
                    new System.Xml.Linq.XAttribute(word + "name", "Channel"),
                    new System.Xml.Linq.XAttribute(word + "val", "Preview"))));
        document.Preserved.Parts.Add(new PreservedPart(
            "/docProps/app.xml",
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Company>Contoso Research</Company>
                  <Manager>Ada Lovelace</Manager>
                  <Template>Proposal.dotx</Template>
                </Properties>
                """)));
        document.Blocks.Add(new Paragraph
        {
            Runs = { title, company, manager, templateProperty, template, templatePath, channel }
        });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        title.Text.Should().Be("Current title");
        company.Text.Should().Be("Contoso Research");
        manager.Text.Should().Be("Ada Lovelace");
        templateProperty.Text.Should().Be("Proposal.dotx");
        template.Text.Should().Be("Proposal.dotx");
        templatePath.Text.Should().Be(@"C:\Templates\Proposal.dotx");
        channel.Text.Should().Be("Preview");
    }

    [Fact]
    public void UpdateFields_RefreshesDocumentStatisticsInStoryOrder()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello world."));
        var numChars = Run.ComplexFieldRun(" NUMCHARS ", "stale");
        var numWords = Run.ComplexFieldRun(" NUMWORDS ", "stale");
        document.Blocks.Add(new Paragraph { Runs = { numChars } });
        document.Blocks.Add(new Paragraph { Runs = { numWords } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        numChars.Text.Should().Be("21");
        numWords.Text.Should().Be("4");
    }

    [Fact]
    public void UpdateFields_SeqUsesAuthoredResultPicture()
    {
        var field = Run.ComplexFieldRun(" SEQ Figure \\r 27 \\* alphabetic ", "stale");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { field } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        field.Text.Should().Be("aa");
    }

    [Fact]
    public void UpdateFields_SeqCountsTableFieldsAndClearsHiddenResult()
    {
        var first = Run.ComplexFieldRun(" SEQ Figure ", "stale");
        var hidden = Run.ComplexFieldRun(" SEQ Figure \\h ", "stale");
        var last = Run.ComplexFieldRun(" SEQ Figure ", "stale");
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph { Runs = { hidden } });
        row.Cells.Add(cell);
        table.Rows.Add(row);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { first } });
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph { Runs = { last } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        first.Text.Should().Be("1");
        hidden.Text.Should().BeEmpty();
        last.Text.Should().Be("3");
    }
}
