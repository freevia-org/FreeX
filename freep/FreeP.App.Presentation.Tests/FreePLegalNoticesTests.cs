using Free.Shared.Shell;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePLegalNoticesTests
{
    [Fact]
    public void Provider_loads_the_complete_offline_family_legal_bundle()
    {
        var documents = FreePLegalNoticeProvider.GetDocuments();

        documents.Select(document => document.Title).Should().Equal(
            "Project License",
            "Legal Notices",
            "Privacy Notice",
            "Third-Party Notices",
            "Third-Party License Texts");
        documents.Should().OnlyContain(document => !string.IsNullOrWhiteSpace(document.Text));
        documents.Should().Contain(document =>
            document.Title == "Legal Notices" &&
            document.Text.Contains("FreeX, FreeW, and FreeP are independent projects.", StringComparison.Ordinal));
    }

    [Fact]
    public void Presentation_owns_FreeP_text_and_shared_read_only_semantics()
    {
        var presentation = FreePLegalNoticesPresentation.Create(
        [
            new LegalNoticeDocument("Privacy Notice", "FreeP.Legal.PrivacyNotice.md", "privacy text"),
        ]);

        presentation.WindowTitle.Should().Be("Legal Notices");
        presentation.SummaryText.Should().Be("These notices are packaged with FreeP for offline review.");
        presentation.HelpText.Should().Contain("packaged with this FreeP executable");
        presentation.CloseButtonContent.Should().Be("Close");
        presentation.TextRenderingPolicy.Should().Be(LegalNoticesTextRenderingPolicy.GrayscaleAntialias);
        presentation.Sections.Should().ContainSingle().Which.Should().Be(
            new LegalNoticeSectionPresentation(
                "Privacy Notice",
                "privacy text",
                "FreeP.Legal.PrivacyNotice.md",
                "LegalNoticesPrivacyNoticeTab",
                "LegalNoticesPrivacyNoticeText"));
    }

    [Fact]
    public void Both_renderers_use_the_shared_presentation_and_options_owned_modal_route()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpfDialog = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "LegalNoticesDialog.cs"));
        var avaloniaDialog = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "LegalNoticesDialog.cs"));
        var wpfOptions = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "OptionsDialog.cs"));
        var avaloniaOptions = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "OptionsDialog.cs"));

        wpfDialog.Should().Contain("SharedLegalNoticesDialog");
        avaloniaDialog.Should().Contain("AvaloniaLegalNoticesDialog");
        wpfDialog.Should().Contain("FreePLegalNoticesPresentation.Create(notices)");
        avaloniaDialog.Should().Contain("FreePLegalNoticesPresentation.Create(notices)");
        wpfOptions.Should().Contain("FreePOptionsLegalNoticesButton");
        wpfOptions.Should().Contain("new LegalNoticesDialog { Owner = this }.ShowDialog()");
        avaloniaOptions.Should().Contain("FreePOptionsLegalNoticesButton");
        avaloniaOptions.Should().Contain("new LegalNoticesDialog().ShowDialog(this)");
    }
}
