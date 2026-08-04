using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class FreeXAboutDialogPresentationTests
{
    [Fact]
    public void Both_hosts_share_dialog_configuration_and_versioned_product_content()
    {
        var assembly = typeof(FreeXAboutDialogPresentationTests).Assembly;
        const string wpfRuntimeNotice = "WPF runtime notice.";
        var wpf = FreeXAboutDialogPresentation.Create(
            assembly,
            "WPF",
            thirdPartyRuntimeNotice: wpfRuntimeNotice);
        var avalonia = FreeXAboutDialogPresentation.Create(assembly, "Avalonia");

        wpf.WindowTitle.Should().Be(FreeXAboutDialogPresentation.WindowTitle);
        avalonia.WindowTitle.Should().Be(wpf.WindowTitle);
        avalonia.DialogAutomationId.Should().Be(wpf.DialogAutomationId);
        avalonia.TextAutomationId.Should().Be(wpf.TextAutomationId);
        avalonia.OkAutomationId.Should().Be(wpf.OkAutomationId);
        avalonia.HelpText.Should().Be(wpf.HelpText);
        avalonia.AboutText.Should().Be(
            AppHelpInfo.BuildAboutText(
                AppHelpInfo.GetVersionText(assembly),
                AppHelpInfo.AvaloniaPlatformSummary));
        wpf.AboutText.Should().Be(
            AppHelpInfo.BuildWpfAboutText(AppHelpInfo.GetVersionText(assembly), wpfRuntimeNotice));
        wpf.AboutText.Should().Contain(wpfRuntimeNotice);
        avalonia.AboutText.Should().NotContain(wpfRuntimeNotice);
    }

    [Fact]
    public void Host_supplied_title_and_help_text_are_preserved()
    {
        var presentation = FreeXAboutDialogPresentation.Create(
            typeof(FreeXAboutDialogPresentationTests).Assembly,
            "WPF",
            windowTitle: "Localized About FreeX",
            helpText: "Localized About help.",
            thirdPartyRuntimeNotice: "WPF runtime notice.");

        presentation.WindowTitle.Should().Be("Localized About FreeX");
        presentation.HelpText.Should().Be("Localized About help.");
    }
}
