using System.Reflection;
using Free.Shared.Shell;
using FreeW.App.Presentation;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWProductInfoTests
{
    [Fact]
    public void About_text_is_renderer_neutral_for_paired_hosts()
    {
        var assembly = typeof(FreeWProductInfoTests).Assembly;
        var wpf = FreeWProductInfo.CreateAboutText(assembly);
        var avalonia = FreeWProductInfo.CreateAboutText(assembly);

        wpf.Should().Contain(FreeWProductInfo.DesktopRendererDescription);
        wpf.Should().Contain("WPF and Avalonia desktop renderers");
        wpf.Should().Be(avalonia);
        avalonia.Should().Contain(FreeWProductInfo.ProjectLicenseNotice);
        avalonia.Should().Contain(FreeWProductInfo.PrivacyNotice);
        avalonia.Should().Contain(FreeWProductInfo.SourceNotice);
        avalonia.Should().NotContain("Microsoft 365");
    }

    [Fact]
    public void About_presentation_contract_is_shared_by_both_hosts()
    {
        var assembly = typeof(FreeWProductInfoTests).Assembly;
        var wpf = FreeWAboutDialogPresentation.Create(assembly);
        var avalonia = FreeWAboutDialogPresentation.Create(assembly);

        wpf.WindowTitle.Should().Be(FreeWAboutDialogPresentation.WindowTitle);
        avalonia.DialogAutomationId.Should().Be(wpf.DialogAutomationId);
        avalonia.TextAutomationId.Should().Be(wpf.TextAutomationId);
        avalonia.OkAutomationId.Should().Be(wpf.OkAutomationId);
        avalonia.HelpText.Should().Be(wpf.HelpText);
        avalonia.AvaloniaRootRightMargin.Should().Be(17);
        avalonia.AvaloniaTextPaddingRight.Should().Be(AboutDialogMetrics.TextPadding);
        avalonia.AvaloniaTextFontSize.Should().Be(AboutDialogMetrics.TextFontSize);
        avalonia.AvaloniaTextPaddingTop.Should().Be(AboutDialogMetrics.TextPadding + 1);
        avalonia.AvaloniaDefaultButtonAccent.Should().BeTrue();
        avalonia.AvaloniaTextLineHeight.Should().Be(16.6);
        wpf.AboutText.Should().Be(avalonia.AboutText);
    }

    [Fact]
    public void Diagnostics_text_uses_the_supplied_host_assembly_version()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var diagnostics = FreeWProductInfo.CreateDiagnosticsText(
            assembly,
            "/tmp/freew/diagnostics",
            "/tmp/freew/options.json");

        diagnostics.Should().Contain($"Version: {FreeWProductInfo.GetBuildVersionText(assembly)}");
        diagnostics.Should().Contain("Diagnostics directory: /tmp/freew/diagnostics");
        diagnostics.Should().Contain("Options path: /tmp/freew/options.json");
    }
}
