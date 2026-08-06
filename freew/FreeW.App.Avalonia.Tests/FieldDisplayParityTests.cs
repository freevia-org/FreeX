using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class FieldDisplayParityTests
{
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
