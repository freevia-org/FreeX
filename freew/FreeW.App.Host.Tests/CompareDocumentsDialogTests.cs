using FreeW.App.Host;
using Xunit;
using FluentAssertions;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA coverage for the <see cref="CompareDocumentsDialog"/>: verifies the control-wiring (seeding from
/// the supplied defaults), the author-override path, and the validation that rejects an empty reviewer name.
/// Uses the <see cref="CompareDocumentsDialog.CreateForTest"/>/<see cref="CompareDocumentsDialog.AcceptForTest"/>
/// seam to exercise the dialog without a real file picker or modal loop.
/// </summary>
public sealed class CompareDocumentsDialogTests
{
    private const string FakePath = @"C:\docs\Contract_v1.docx";
    private const string DefaultAuthor = "Alice Editor";
    private const string RevisedTitle = "Contract_v2.docx";

    [StaFact]
    public void Dialog_SeedsAuthorBox_WithDefaultAuthor()
    {
        // The dialog must pre-populate the "Label revisions with:" box from the supplied default author
        // so the user sees their name immediately and can confirm or override.
        var dlg = CompareDocumentsDialog.CreateForTest(FakePath, DefaultAuthor, RevisedTitle);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Author.Should().Be(DefaultAuthor);
    }

    [StaFact]
    public void Dialog_Result_CarriesTheOriginalFilePath()
    {
        var dlg = CompareDocumentsDialog.CreateForTest(FakePath, DefaultAuthor, RevisedTitle);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.OriginalFilePath.Should().Be(FakePath);
    }

    [StaFact]
    public void Dialog_Accept_WithEmptyAuthor_ReturnsNull()
    {
        // Validation: if the user clears the author box, Accept must refuse to close the dialog
        // and AcceptForTest must return null (the result was not set).
        var dlg = CompareDocumentsDialog.CreateForTest(FakePath, " ", RevisedTitle);
        var result = dlg.AcceptForTest();

        // Empty/whitespace author triggers the validation warning and leaves _result unset.
        result.Should().BeNull();
    }

    [StaFact]
    public void Dialog_RevisedTitle_IsPresented_WithoutError()
    {
        // Smoke-test: constructing the dialog with a non-empty revised title must not throw.
        var act = () =>
        {
            var dlg = CompareDocumentsDialog.CreateForTest(FakePath, DefaultAuthor, "My Document.docx");
            dlg.AcceptForTest();
        };
        act.Should().NotThrow();
    }

    [StaFact]
    public void Dialog_EmptyRevisedTitle_IsAccepted_Gracefully()
    {
        // Constructing without a revised title (empty string) must also work.
        var dlg = CompareDocumentsDialog.CreateForTest(FakePath, DefaultAuthor, revisedTitle: "");
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.OriginalFilePath.Should().Be(FakePath);
    }

    // --- CompareSettings depth ---

    [StaFact]
    public void Dialog_DefaultSettings_AllChangesEnabled_NewDocument()
    {
        // When the user doesn't open "More", the result must carry all-enabled default settings.
        var dlg = CompareDocumentsDialog.CreateForTest(FakePath, DefaultAuthor, RevisedTitle);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Settings.Insertions.Should().BeTrue();
        result.Settings.Deletions.Should().BeTrue();
        result.Settings.Moves.Should().BeTrue();
        result.Settings.Comments.Should().BeTrue();
        result.Settings.Formatting.Should().BeTrue();
        result.Settings.CaseChanges.Should().BeTrue();
        result.Settings.Whitespace.Should().BeTrue();
        result.Settings.ShowChangesIn.Should().Be(FreeW.Core.Model.CompareShowChangesIn.NewDocument);
    }

    [StaFact]
    public void Dialog_Result_CarriesSettingsInstance()
    {
        var dlg = CompareDocumentsDialog.CreateForTest(FakePath, DefaultAuthor, RevisedTitle);
        var result = dlg.AcceptForTest();

        result.Should().NotBeNull();
        result!.Settings.Should().NotBeNull();
    }

    [StaFact]
    public void Dialog_MoreExpander_StartsCollapsed_AndCanRevealSettings()
    {
        var dlg = CompareDocumentsDialog.CreateForTest(FakePath, DefaultAuthor, RevisedTitle);

        dlg.MoreExpanderForTest.IsExpanded.Should().BeFalse();
        dlg.MoreExpanderForTest.Header.Should().Be("More");
        dlg.MoreExpanderForTest.Content.Should().NotBeNull();

        dlg.MoreExpanderForTest.IsExpanded = true;

        dlg.MoreExpanderForTest.IsExpanded.Should().BeTrue();
    }
}
