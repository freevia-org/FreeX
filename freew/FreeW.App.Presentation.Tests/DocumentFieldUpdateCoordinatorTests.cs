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
}
