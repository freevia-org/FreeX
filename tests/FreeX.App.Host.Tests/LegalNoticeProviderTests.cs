using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class LegalNoticeProviderTests
{
    [Fact]
    public void GetDocuments_EmbedsFullOfflineLegalNoticeSet()
    {
        var documents = LegalNoticeProvider.GetDocuments();

        documents.Select(document => document.Title).Should().Equal(
            "Project License",
            "Legal Notices",
            "Privacy Notice",
            "Third-Party Notices",
            "Third-Party License Texts");
        documents.Should().OnlyContain(document => !string.IsNullOrWhiteSpace(document.Text));
        documents.Should().Contain(document =>
            document.Title == "Legal Notices" &&
            document.Text.Contains("FreeX is not affiliated with, endorsed by, or sponsored by Microsoft."));
        documents.Should().Contain(document =>
            document.Title == "Privacy Notice" &&
            document.Text.Contains("%LOCALAPPDATA%\\FreeX\\Diagnostics"));
        documents.Should().Contain(document =>
            document.Title == "Third-Party Notices" &&
            document.Text.Contains("Runtime Packages"));
        documents.Should().Contain(document =>
            document.Title == "Third-Party License Texts" &&
            document.Text.Contains("Apache License") &&
            document.Text.Contains("MIT License"));
    }

    [Fact]
    public void Dialog_ExposesCopyableNoticeTabsWithStableAutomationMetadata()
    {
        var documents = new[]
        {
            new LegalNoticeDocument("Legal Notices", "Test.Resource", "Offline legal text")
        };

        StaTestRunner.Run(() =>
        {
            var dialog = new LegalNoticesDialog(documents);

            dialog.Title.Should().Be("Legal Notices");
            dialog.Width.Should().BeGreaterThanOrEqualTo(800);
            dialog.ShowInTaskbar.Should().BeFalse();
            dialog.Content.Should().NotBeNull();
        });
    }
}
