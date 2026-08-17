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

    // DocumentFieldStories reports BodyBlockIndex = -1 for header/footer/footnote/endnote/comment
    // stories, so a date field in a header must still climb the paragraph's style chain the same way
    // a body CREATEDATE/SAVEDATE/PRINTDATE field does -- not fall straight through to the document
    // default just because it lives outside document.Blocks.
    [Fact]
    public void Update_resolves_createdate_field_in_header_from_owning_paragraphs_style_language()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Created = new DateTimeOffset(new DateTime(2026, 1, 5), TimeSpan.Zero);
        document.Styles["Journal"] = new DocumentStyle
        {
            Id = "Journal",
            Name = "Journal",
            Run = new RunFormatting { LanguageTag = "fr-FR" },
        };
        document.Header = new HeaderFooter();
        var headerParagraph = new Paragraph { StyleId = "Journal" };
        headerParagraph.Runs.Add(new Run("stale")
        {
            ComplexField = new ComplexField(" CREATEDATE \\@ \"d MMMM yyyy\" "),
        });
        document.Header.Paragraphs.Add(headerParagraph);

        var updated = DocumentFieldUpdateCoordinator.Update(
            document,
            document,
            "current.docx",
            new DateTime(2026, 8, 12, 12, 0, 0),
            CultureInfo.InvariantCulture,
            pageNumberText: "1",
            pageCount: 1);

        updated.Should().Be(1);
        headerParagraph.Runs[0].Text.Should().Be("5 janvier 2026");
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

    [Fact]
    public void UpdateComplexFields_updates_only_selected_model_runs_across_stories()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Author = "Grace";
        var body = (Paragraph)document.Blocks[0];
        body.Runs.Clear();
        var selectedBody = new Run("old author")
        {
            ComplexField = new ComplexField(" AUTHOR ")
        };
        var unselectedBody = new Run("old file")
        {
            ComplexField = new ComplexField(" FILENAME ")
        };
        var lockedBody = new Run("locked")
        {
            ComplexField = new ComplexField(" AUTHOR ").WithLock(true)
        };
        body.Runs.Add(selectedBody);
        body.Runs.Add(unselectedBody);
        body.Runs.Add(lockedBody);

        document.Header = new HeaderFooter();
        var selectedHeader = new Run("old header file")
        {
            ComplexField = new ComplexField(" FILENAME ")
        };
        document.Header.Paragraphs.Add(new Paragraph { Runs = { selectedHeader } });

        var updated = DocumentFieldUpdateCoordinator.UpdateComplexFields(
            document,
            document,
            [selectedBody, selectedHeader, lockedBody],
            "current.docx",
            new DateTime(2026, 8, 12, 12, 0, 0),
            CultureInfo.InvariantCulture,
            pageNumberText: "1",
            pageCount: 3);

        updated.Should().Be(2);
        selectedBody.Text.Should().Be("Grace");
        selectedHeader.Text.Should().Be("current.docx");
        unselectedBody.Text.Should().Be("old file");
        lockedBody.Text.Should().Be("locked");
    }

    [Fact]
    public void UpdateComplexFields_matches_projected_fields_by_identity_not_value()
    {
        var document = TextDocument.CreateEmpty();
        var body = (Paragraph)document.Blocks[0];
        body.Runs.Clear();
        var selectedField = new ComplexField(" FILENAME ");
        var equalButUnselectedField = new ComplexField(" FILENAME ");
        var selected = new Run("selected") { ComplexField = selectedField };
        var unselected = new Run("unselected") { ComplexField = equalButUnselectedField };
        body.Runs.Add(selected);
        body.Runs.Add(unselected);

        var updated = DocumentFieldUpdateCoordinator.UpdateComplexFields(
            document,
            document,
            [selectedField],
            "current.docx",
            new DateTime(2026, 8, 12, 12, 0, 0),
            CultureInfo.InvariantCulture,
            pageNumberText: "1",
            pageCount: 1);

        updated.Should().Be(1);
        selected.Text.Should().Be("current.docx");
        unselected.Text.Should().Be("unselected");
    }

    [Fact]
    public void Selection_mutations_share_code_visibility_and_lock_behavior()
    {
        var document = TextDocument.CreateEmpty();
        var body = (Paragraph)document.Blocks[0];
        body.Runs.Clear();
        var projectedField = new ComplexField(" PAGE ");
        var projectedRun = new Run("1") { ComplexField = projectedField };
        var nativeRun = new Run("Ada") { ComplexField = new ComplexField(" AUTHOR ") };
        var ordinaryRun = new Run("plain");
        body.Runs.Add(projectedRun);
        body.Runs.Add(nativeRun);
        body.Runs.Add(ordinaryRun);

        DocumentFieldUpdateCoordinator.ToggleCode(document, [projectedField]).Should().Be(1);
        projectedRun.ComplexField!.ShowCode.Should().BeTrue();

        DocumentFieldUpdateCoordinator.ToggleCode([nativeRun, nativeRun, ordinaryRun]).Should().Be(1);
        nativeRun.ComplexField!.ShowCode.Should().BeTrue();

        DocumentFieldUpdateCoordinator.SetLock(document, [projectedRun.ComplexField], true).Should().Be(1);
        projectedRun.ComplexField.IsLocked.Should().BeTrue();

        DocumentFieldUpdateCoordinator.SetLock([nativeRun], true).Should().Be(1);
        nativeRun.ComplexField.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Unlink_preserves_cached_results_for_model_native_selection()
    {
        var selected = new Run("cached result")
        {
            ComplexField = new ComplexField(" AUTHOR ")
        };
        var unselected = new Run("other cached result")
        {
            ComplexField = new ComplexField(" FILENAME ")
        };
        var ordinary = new Run("plain");

        DocumentFieldUpdateCoordinator.Unlink([selected, selected, ordinary]).Should().Be(1);

        selected.Text.Should().Be("cached result");
        selected.ComplexField.Should().BeNull();
        unselected.ComplexField.Should().NotBeNull();
        ordinary.Text.Should().Be("plain");
    }

    [Fact]
    public void Unlink_projected_fields_uses_displayed_results_and_reference_identity_across_stories()
    {
        var document = TextDocument.CreateEmpty();
        var body = (Paragraph)document.Blocks[0];
        body.Runs.Clear();
        var selectedBodyField = new ComplexField(" AUTHOR ");
        var equalButUnselectedField = new ComplexField(" AUTHOR ");
        var selectedBody = new Run("committed body") { ComplexField = selectedBodyField };
        var unselectedBody = new Run("unselected") { ComplexField = equalButUnselectedField };
        body.Runs.Add(selectedBody);
        body.Runs.Add(unselectedBody);

        document.Header = new HeaderFooter();
        var selectedHeaderField = new ComplexField(" FILENAME ");
        var selectedHeader = new Run("committed header") { ComplexField = selectedHeaderField };
        document.Header.Paragraphs.Add(new Paragraph { Runs = { selectedHeader } });

        DocumentFieldUpdateCoordinator.Unlink(
            document,
            [
                new DocumentComplexFieldUnlinkTarget(selectedBodyField, "displayed body"),
                new DocumentComplexFieldUnlinkTarget(selectedHeaderField, "displayed header")
            ]).Should().Be(2);

        selectedBody.Text.Should().Be("displayed body");
        selectedBody.ComplexField.Should().BeNull();
        selectedHeader.Text.Should().Be("displayed header");
        selectedHeader.ComplexField.Should().BeNull();
        unselectedBody.Text.Should().Be("unselected");
        unselectedBody.ComplexField.Should().BeSameAs(equalButUnselectedField);
    }

    [Fact]
    public void ToggleAllCodes_uses_one_majority_state_across_document_stories()
    {
        var document = TextDocument.CreateEmpty();
        var body = (Paragraph)document.Blocks[0];
        body.Runs.Clear();
        var hiddenBody = new Run("1") { ComplexField = new ComplexField(" PAGE ") };
        var shownBody = new Run("Ada")
        {
            ComplexField = new ComplexField(" AUTHOR ", ShowCode: true)
        };
        body.Runs.Add(hiddenBody);
        body.Runs.Add(shownBody);

        document.Header = new HeaderFooter();
        var hiddenHeader = new Run("file.docx")
        {
            ComplexField = new ComplexField(" FILENAME ")
        };
        document.Header.Paragraphs.Add(new Paragraph { Runs = { hiddenHeader } });

        DocumentFieldUpdateCoordinator.ToggleAllCodes(document).Should().Be(3);
        new[] { hiddenBody, shownBody, hiddenHeader }
            .Should().OnlyContain(run => run.ComplexField!.ShowCode);

        DocumentFieldUpdateCoordinator.ToggleAllCodes(document).Should().Be(3);
        new[] { hiddenBody, shownBody, hiddenHeader }
            .Should().OnlyContain(run => !run.ComplexField!.ShowCode);
    }
}
