using System.Reflection;
using FreeW.App.Presentation;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWProductInfoTests
{
    [Fact]
    public void About_text_shares_product_content_and_varies_only_the_true_host_framework()
    {
        var assembly = typeof(FreeWProductInfoTests).Assembly;
        var wpf = FreeWProductInfo.CreateAboutText(assembly, "WPF");
        var avalonia = FreeWProductInfo.CreateAboutText(assembly, "Avalonia");

        wpf.Should().Contain("Built with .NET 10 and WPF.");
        avalonia.Should().Contain("Built with .NET 10 and Avalonia.");
        wpf.Replace("WPF", "Avalonia", StringComparison.Ordinal).Should().Be(avalonia);
        avalonia.Should().Contain(FreeWProductInfo.ProjectLicenseNotice);
        avalonia.Should().Contain(FreeWProductInfo.PrivacyNotice);
        avalonia.Should().Contain(FreeWProductInfo.SourceNotice);
        avalonia.Should().NotContain("Microsoft 365");
    }

    [Fact]
    public void About_presentation_contract_is_shared_by_both_hosts()
    {
        var assembly = typeof(FreeWProductInfoTests).Assembly;
        var wpf = FreeWAboutDialogPresentation.Create(assembly, "WPF");
        var avalonia = FreeWAboutDialogPresentation.Create(assembly, "Avalonia");

        wpf.WindowTitle.Should().Be(FreeWAboutDialogPresentation.WindowTitle);
        avalonia.DialogAutomationId.Should().Be(wpf.DialogAutomationId);
        avalonia.TextAutomationId.Should().Be(wpf.TextAutomationId);
        avalonia.OkAutomationId.Should().Be(wpf.OkAutomationId);
        avalonia.HelpText.Should().Be(wpf.HelpText);
        avalonia.AvaloniaRootRightMargin.Should().Be(17);
        wpf.AboutText.Replace("WPF", "Avalonia", StringComparison.Ordinal).Should().Be(avalonia.AboutText);
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
