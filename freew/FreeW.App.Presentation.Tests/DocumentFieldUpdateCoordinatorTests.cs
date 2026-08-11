using System.Globalization;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentFieldUpdateCoordinatorTests
{
    [Fact]
    public void ResolveLiveValue_uses_one_culture_file_and_property_contract()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Author = "Ada";
        var now = new DateTime(2026, 8, 12, 17, 5, 0);
        var culture = CultureInfo.GetCultureInfo("en-GB");

        ComplexFieldDisplayPlanner.ResolveLiveValue(
            RunFieldKind.Date, "cached", document, "report.docx", now, culture, "iv", 7)
            .Should().Be(now.ToString("d", culture));
        ComplexFieldDisplayPlanner.ResolveLiveValue(
            RunFieldKind.Time, "cached", document, "report.docx", now, culture, "iv", 7)
            .Should().Be(now.ToString("t", culture));
        ComplexFieldDisplayPlanner.ResolveLiveValue(
            RunFieldKind.FileName, "cached", document, "report.docx", now, culture, "iv", 7)
            .Should().Be("report.docx");
        ComplexFieldDisplayPlanner.ResolveLiveValue(
            RunFieldKind.Author, "cached", document, null, now, culture, "iv", 7)
            .Should().Be("Ada");
        ComplexFieldDisplayPlanner.ResolveLiveValue(
            RunFieldKind.NumPages, "cached", document, null, now, culture, "iv", null)
            .Should().Be("cached");
    }

    [Fact]
    public void Update_refreshes_simple_and_complex_fields_across_document_stories_and_honors_locks()
    {
        var document = TextDocument.CreateEmpty();
        var body = (Paragraph)document.Blocks[0];
        body.Runs.Clear();
        body.Runs.Add(new Run("old.docx") { FieldKind = RunFieldKind.FileName });
        body.Runs.Add(new Run("old author")
        {
            ComplexField = new ComplexField(" AUTHOR ")
        });
        body.Runs.Add(new Run("locked")
        {
            ComplexField = new ComplexField(" FILENAME ").WithLock(true)
        });
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run("old header file") { FieldKind = RunFieldKind.FileName } }
        });
        document.Properties.Author = "Grace";

        var updated = DocumentFieldUpdateCoordinator.Update(
            document,
            document,
            "current.docx",
            new DateTime(2026, 8, 12, 12, 0, 0),
            CultureInfo.InvariantCulture,
            pageNumberText: "1",
            pageCount: 3);

        updated.Should().Be(3);
        body.Runs[0].Text.Should().Be("current.docx");
        body.Runs[1].Text.Should().Be("Grace");
        body.Runs[2].Text.Should().Be("locked");
        document.Header.Paragraphs[0].Runs[0].Text.Should().Be("current.docx");
    }

    [Fact]
    public void RequiresPageResolver_detects_simple_and_complex_page_references()
    {
        var document = TextDocument.CreateEmpty();
        var paragraph = (Paragraph)document.Blocks[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run("1")
        {
            ComplexField = new ComplexField(" PAGEREF Target ")
        });

        DocumentFieldUpdateCoordinator.RequiresPageResolver(document).Should().BeTrue();
    }
}
