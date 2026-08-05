using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Host;
using FreeX.App.Services;

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

            var tabControl = WpfTestTree.FindLogicalDescendants<TabControl>(dialog).Single();
            AutomationProperties.GetName(tabControl).Should().Be("Legal notice sections");
            AutomationProperties.GetAutomationId(tabControl).Should().Be("LegalNoticesSectionTabs");

            var tab = tabControl.Items.Cast<object>().Single().Should().BeOfType<TabItem>().Subject;
            tab.Header.Should().Be("Legal Notices");
            AutomationProperties.GetName(tab).Should().Be("Legal Notices");
            AutomationProperties.GetAutomationId(tab).Should().Be("LegalNoticesLegalNoticesTab");

            var noticeText = tab.Content.Should().BeOfType<TextBox>().Subject;
            noticeText.Text.Should().Be("Offline legal text");
            noticeText.IsReadOnly.Should().BeTrue();
            noticeText.AcceptsReturn.Should().BeTrue();
            AutomationProperties.GetName(noticeText).Should().Be("Legal Notices");
            AutomationProperties.GetAutomationId(noticeText).Should().Be("LegalNoticesLegalNoticesText");

            var close = WpfTestTree.FindLogicalDescendants<Button>(dialog).Single();
            close.Content.Should().Be(UiText.Get("LegalNotices_CloseButton"));
            close.IsDefault.Should().BeTrue();
            close.IsCancel.Should().BeTrue();
            AutomationProperties.GetAutomationId(close).Should().Be("LegalNoticesCloseButton");
            AutomationProperties.GetHelpText(close).Should().Be("Shows the legal, privacy, and third-party notices packaged with this FreeX executable.");
        });
    }
}
