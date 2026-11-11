using FreeP.App.Compositor;
using FreeW.App.Presentation;
using Free.Shared.Shell;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// The product-parameterised legal-notices and build-info helpers, plus proof that the FreeW and
/// FreeP bindings are unchanged in output: same English text as before, grayscale rendering, and
/// the {Product}.Legal.* manifest names.
/// </summary>
public sealed class ProductLegalNoticesAndBuildInfoTests
{
    private static readonly LegalNoticeDocument[] Notices =
        [new("Project License", "Test.Legal.ProjectLicense.txt", "text")];

    [Fact]
    public void ProductLegalNotices_DefaultsToLiteralEnglishWithProductName()
    {
        var presentation = ProductLegalNoticesPresentation.Create("Widget", Notices);

        presentation.WindowTitle.Should().Be("Legal Notices");
        presentation.SummaryText.Should().Be("These notices are packaged with Widget for offline review.");
        presentation.CloseButtonContent.Should().Be("Close");
        presentation.HelpText.Should().Be(
            "Shows the legal, privacy, and third-party notices packaged with this Widget executable.");
        presentation.SummaryAutomationName.Should().Be("Legal Notices summary");
        presentation.SectionsAutomationName.Should().Be("Legal notice sections");
        presentation.SectionLinkHelpText.Should().Be("Choose a legal notice section to read and copy.");
        presentation.ReadOnlyBodyHelpText.Should().Be(
            "Read-only legal notice text. Use Ctrl+C to copy selected text.");
        presentation.TextRenderingPolicy.Should().Be(LegalNoticesTextRenderingPolicy.Default);
        presentation.Sections.Should().ContainSingle().Which.Body.Should().Be("text");
    }

    [Fact]
    public void ProductLegalNotices_ResolverOverridesByKeyAndNullFallsBackToDefault()
    {
        var presentation = ProductLegalNoticesPresentation.Create(
            "Widget",
            Notices,
            key => key switch
            {
                ProductLegalNoticesPresentation.WindowTitleKey => "Mentions legales",
                ProductLegalNoticesPresentation.SummaryKey => "Fourni avec {0}.",
                _ => null,
            });

        presentation.WindowTitle.Should().Be("Mentions legales");
        presentation.SummaryText.Should().Be("Fourni avec Widget.");
        presentation.CloseButtonContent.Should().Be("Close");
        ProductLegalNoticesPresentation.RequiredResourceKeys.Should().OnlyHaveUniqueItems().And.HaveCount(8);
    }

    [Fact]
    public void ProductLegalNotices_PairOverloadWrapsTextAsDocuments()
    {
        var presentation = ProductLegalNoticesPresentation.Create(
            "Widget",
            new[] { ("Title", "Body") });

        presentation.Sections.Should().ContainSingle();
        presentation.Sections[0].Heading.Should().Be("Title");
        presentation.Sections[0].Body.Should().Be("Body");
    }

    [Theory]
    [InlineData("FreeW")]
    [InlineData("FreeP")]
    public void SisterBindings_KeepProductTextGrayscaleRenderingAndManifest(string product)
    {
        var presentation = product == "FreeW"
            ? FreeWLegalNoticesPresentation.Create(Notices)
            : FreePLegalNoticesPresentation.Create(Notices);
        var manifest = product == "FreeW"
            ? FreeWLegalNoticeManifest.Resources
            : FreePLegalNoticeManifest.Resources;

        presentation.SummaryText.Should().Be($"These notices are packaged with {product} for offline review.");
        presentation.HelpText.Should().Be(
            $"Shows the legal, privacy, and third-party notices packaged with this {product} executable.");
        presentation.TextRenderingPolicy.Should().Be(LegalNoticesTextRenderingPolicy.GrayscaleAntialias);
        manifest.Select(resource => resource.ResourceName).Should().Equal(
            $"{product}.Legal.ProjectLicense.txt",
            $"{product}.Legal.LegalNotices.md",
            $"{product}.Legal.PrivacyNotice.md",
            $"{product}.Legal.ThirdPartyNotices.md",
            $"{product}.Legal.ThirdPartyLicenses.md");
        manifest.Select(resource => resource.Title).Should().Equal(
            "Project License", "Legal Notices", "Privacy Notice", "Third-Party Notices", "Third-Party License Texts");
    }

    [Fact]
    public void ProductBuildInfo_MatchesFormatterAndKeepsVersionConventionAsAParameter()
    {
        var assembly = typeof(ProductBuildInfo).Assembly;
        var metadata = AssemblyVersionMetadata.FromAssembly(assembly);

        ProductBuildInfo.GetVersionText(assembly).Should().Be(
            AppVersionFormatter.FormatVersionText(metadata.InformationalVersion));
        ProductBuildInfo.GetVersionText(assembly, dropTrailingZeroPatch: true).Should().Be(
            AppVersionFormatter.FormatVersionText(metadata.InformationalVersion, dropTrailingZeroPatch: true));
        ProductBuildInfo.GetBuildVersionText(assembly).Should().Be(
            AppVersionFormatter.FormatBuildVersionText(metadata.InformationalVersion, metadata.AssemblyVersion));
        ProductBuildInfo.CreateFeedbackUrl("Widget", assembly).Should().Contain("Widget");
        ProductBuildInfo.CreateDiagnosticsText("Widget", assembly).Should().StartWith("Widget Diagnostics");

        Action nullAssembly = () => ProductBuildInfo.GetBuildVersionText(null!);
        nullAssembly.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SisterProductInfo_DelegatesToTheSharedBuildInfo()
    {
        var assembly = typeof(ProductBuildInfo).Assembly;

        FreeWProductInfo.CreateFeedbackUrl(assembly).Should().Be(ProductBuildInfo.CreateFeedbackUrl("FreeW", assembly));
        FreeWProductInfo.GetVersionText(assembly).Should().Be(ProductBuildInfo.GetVersionText(assembly));
        FreeWProductInfo.GetBuildVersionText(assembly).Should().Be(ProductBuildInfo.GetBuildVersionText(assembly));
        FreePProductInfo.CreateFeedbackUrl(assembly).Should().Be(ProductBuildInfo.CreateFeedbackUrl("FreeP", assembly));
        FreePProductInfo.CreateDiagnosticsText(assembly).Should().Be(ProductBuildInfo.CreateDiagnosticsText("FreeP", assembly));
    }
}
