using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Shell;
using FreeW.App.Presentation;

namespace FreeW.App.Host.Tests;

public sealed class FreeWHelpInfoTests
{
    [Fact]
    public void AppInfo_UsesFreeWBrandingAndHonestLocalUrls()
    {
        FreeWProductInfo.ProductName.Should().Be("FreeW");
        FreeWProductInfo.HelpUrl.Should().Contain("/freew");
        FreeWProductInfo.FeedbackUrl.Should().Contain("FreeW%20feedback");
        FreeWProductInfo.LatestReleaseUrl.Should().Contain("freew-release.yml");
        FreeWAppInfo.AboutText.Should().Contain("FreeW");
        FreeWAppInfo.AboutText.Should().NotContain("Microsoft 365");
    }

    [Fact]
    public void AppInfo_IsOnlyTheWpfAssemblyContextAdapter()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FreeWAppInfo.cs"));

        source.Should().Contain("typeof(FreeWAppInfo).Assembly");
        source.Should().Contain("FreeWAboutDialogPresentation.Create");
        source.Should().Contain("FreeWProductInfo.CreateDiagnosticsText");
        source.Should().NotContain("public const string");
        source.Should().NotContain("AppVersionFormatter");
        source.Should().NotContain("GetVersionText(Assembly");
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
        var documents = FreeWLegalNoticeProvider.GetDocuments(typeof(FreeWAppInfo).Assembly);

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
        dialog.Width.Should().Be(AboutDialogMetrics.Width);
        dialog.Height.Should().Be(AboutDialogMetrics.Height);
        dialog.MinWidth.Should().Be(AboutDialogMetrics.MinWidth);
        dialog.MinHeight.Should().Be(AboutDialogMetrics.MinHeight);
        AutomationProperties.GetAutomationId(dialog).Should().Be("AboutFreeWDialog");
        LogicalDescendants<TextBox>(dialog)
            .Single(textBox => AutomationProperties.GetAutomationId(textBox) == "AboutFreeWText")
            .Should().Match<TextBox>(textBox =>
                textBox.Text.Contains("FreeW") &&
                textBox.FontSize == AboutDialogMetrics.TextFontSize &&
                textBox.MinHeight == AboutDialogMetrics.TextMinHeight &&
                textBox.Padding == new Thickness(AboutDialogMetrics.TextPadding));
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

        dialog.Width.Should().Be(LegalNoticesDialogMetrics.Width);
        dialog.Height.Should().Be(LegalNoticesDialogMetrics.Height);
        dialog.MinWidth.Should().Be(LegalNoticesDialogMetrics.MinWidth);
        dialog.MinHeight.Should().Be(LegalNoticesDialogMetrics.MinHeight);
        LogicalDescendants<TabControl>(dialog)
            .Single(tab => AutomationProperties.GetAutomationId(tab) == "LegalNoticesSectionTabs")
            .Items.Count.Should().Be(2);
    }

    [StaFact]
    public void LegalNoticesDialog_uses_shared_read_only_text_metrics_for_every_notice_tab()
    {
        var notices = new[]
        {
            ("Project License", "license text"),
            ("Legal Notices", "legal text"),
            ("Privacy Notice", "privacy text"),
            ("Third-Party Notices", "third-party notices"),
            ("Third-Party License Texts", "third-party license texts"),
        };
        var dialog = new LegalNoticesDialog(notices);

        var tabs = LogicalDescendants<TabControl>(dialog).Single();
        var textBoxes = tabs.Items
            .OfType<TabItem>()
            .Select(tab => tab.Content)
            .OfType<TextBox>()
            .ToArray();
        textBoxes.Select(text => text.Text).Should().Equal(notices.Select(notice => notice.Item2));
        textBoxes.Should().OnlyContain(text =>
            text.IsReadOnly &&
            text.AcceptsReturn &&
            text.AcceptsTab &&
            text.TextWrapping == TextWrapping.Wrap &&
            text.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
            text.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled &&
            text.FontSize == LegalNoticesDialogMetrics.TextFontSize &&
            text.Padding == new Thickness(LegalNoticesDialogMetrics.TextPadding) &&
            text.Margin == new Thickness(0) &&
            text.MinHeight == LegalNoticesDialogMetrics.TextMinHeight);
        textBoxes.Select(text => text.FontFamily.Source)
            .Should()
            .OnlyContain(source => source == "Consolas");
    }

    [StaFact]
    public void LegalNoticesDialog_preserves_the_shared_close_and_read_only_copy_contract()
    {
        var dialog = new LegalNoticesDialog(
        [
            ("Project License", "license text"),
            ("Legal Notices", "legal text"),
        ]);

        var close = LogicalDescendants<Button>(dialog)
            .Single(button => AutomationProperties.GetAutomationId(button) == "LegalNoticesCloseButton");
        close.IsDefault.Should().BeTrue();
        close.IsCancel.Should().BeTrue();

        LogicalDescendants<TextBox>(dialog)
            .Should()
            .OnlyContain(text => text.IsReadOnly && text.AcceptsReturn && text.AcceptsTab);
    }

    [StaFact]
    public void WpfAuthority_read_only_textbox_key_contract_is_observable_at_runtime()
    {
        var dialog = new LegalNoticesDialog(
        [
            ("Project License", "license text"),
            ("Legal Notices", "legal text"),
        ]);
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var text = VisualDescendants<TextBox>(dialog).First();
            Keyboard.Focus(text).Should().BeSameAs(text);

            var tab = CreateKeyDown(dialog, Key.Tab);
            text.RaiseEvent(tab);
            tab.Handled.Should().BeTrue("WPF consumes plain Tab from a read-only AcceptsTab text box");
            Keyboard.FocusedElement.Should().BeSameAs(text);

            Keyboard.Focus(text).Should().BeSameAs(text);
            var enter = CreateKeyDown(dialog, Key.Enter);
            text.RaiseEvent(enter);
            enter.Handled.Should().BeFalse("WPF read-only text must not consume plain Enter");
            dialog.IsVisible.Should().BeTrue("the routed authority probe does not synthesize a default-button click");
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    private static KeyEventArgs CreateKeyDown(Window source, Key key)
    {
        var args = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(source)!,
            0,
            key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        };
        return args;
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

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result)
            yield return result;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var descendant in VisualDescendants<T>(VisualTreeHelper.GetChild(root, index)))
                yield return descendant;
    }

}
