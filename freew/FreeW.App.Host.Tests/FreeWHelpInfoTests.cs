using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeW.App.Host.Tests;

public sealed class FreeWHelpInfoTests
{
    [Fact]
    public void AppInfo_UsesFreeWBrandingAndHonestLocalUrls()
    {
        FreeWAppInfo.ProductName.Should().Be("FreeW");
        FreeWAppInfo.HelpUrl.Should().Contain("/freew");
        FreeWAppInfo.FeedbackUrl.Should().Contain("FreeW%20feedback");
        FreeWAppInfo.LatestReleaseUrl.Should().Contain("freew-release.yml");
        FreeWAppInfo.AboutText.Should().Contain("FreeW");
        FreeWAppInfo.AboutText.Should().NotContain("Microsoft 365");
    }

    [Fact]
    public void DiagnosticsText_IncludesFreeWVersionAndLocalPaths()
    {
        var text = FreeWAppInfo.CreateDiagnosticsText(@"C:\Users\test\AppData\Local\FreeW\Diagnostics", @"C:\Users\test\AppData\Local\FreeW\options.json");

        text.Should().Contain("FreeW Diagnostics");
        text.Should().Contain("Version:");
        text.Should().Contain("Diagnostics directory:");
        text.Should().Contain("Options path:");
        text.Should().Contain("Review this text before sharing it.");
    }

    [Fact]
    public void LegalNoticeProvider_LoadsPackagedOfflineDocuments()
    {
        var documents = FreeWLegalNoticeProvider.GetDocuments();

        documents.Select(document => document.Title)
            .Should()
            .Equal("Project License", "Legal Notices", "Privacy Notice", "Third-Party Notices", "Third-Party License Texts");
        documents.Should().OnlyContain(document => !string.IsNullOrWhiteSpace(document.Text));
    }

    [StaFact]
    public void AboutDialog_ExposesStableAutomationMetadata()
    {
        var dialog = new AboutDialog();

        dialog.Title.Should().Be("About FreeW");
        AutomationProperties.GetAutomationId(dialog).Should().Be("AboutFreeWDialog");
        LogicalDescendants<TextBox>(dialog)
            .Single(textBox => AutomationProperties.GetAutomationId(textBox) == "AboutFreeWText")
            .Text.Should().Contain("FreeW");
    }

    [StaFact]
    public void LegalNoticesDialog_ExposesPackagedNoticeTabsWithAutomationMetadata()
    {
        var dialog = new LegalNoticesDialog(
        [
            ("Project License", "license text"),
            ("Privacy Notice", "privacy text")
        ]);

        dialog.Title.Should().Be("Legal Notices");
        AutomationProperties.GetAutomationId(dialog).Should().Be("LegalNoticesDialog");
        var tabs = LogicalDescendants<TabControl>(dialog)
            .Single(tabControl => AutomationProperties.GetAutomationId(tabControl) == "LegalNoticesSectionTabs")
            .Items
            .OfType<TabItem>()
            .ToList();
        tabs.Select(tab => tab.Header?.ToString()).Should().Equal("Project License", "Privacy Notice");
        tabs.Should().OnlyContain(tab => AutomationProperties.GetAutomationId(tab).StartsWith("LegalNotices", StringComparison.Ordinal));
    }

    [StaFact]
    public void LegalNoticesDialog_uses_the_Wpf_authority_chrome_metrics()
    {
        var dialog = new LegalNoticesDialog(
        [
            ("Project License", "license text"),
            ("Privacy Notice", "privacy text")
        ]);

        dialog.Width.Should().Be(840);
        dialog.Height.Should().Be(620);
        dialog.MinWidth.Should().Be(620);
        dialog.MinHeight.Should().Be(420);
        LogicalDescendants<TabControl>(dialog)
            .Single(tab => AutomationProperties.GetAutomationId(tab) == "LegalNoticesSectionTabs")
            .Items.Count.Should().Be(2);
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependencyObject)
            {
                if (dependencyObject is T result)
                    yield return result;

                foreach (var descendant in LogicalDescendants<T>(dependencyObject))
                    yield return descendant;
            }
    }

}
