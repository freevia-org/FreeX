using System.Reflection;
using FluentAssertions;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class AboutLegalSharedBoundaryTests
{
    [Fact]
    public void AssemblyVersionMetadata_MatchesTheAssemblyReflectionContract()
    {
        var assembly = typeof(AboutLegalSharedBoundaryTests).Assembly;

        var metadata = AssemblyVersionMetadata.FromAssembly(assembly);

        metadata.InformationalVersion.Should().Be(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        metadata.AssemblyVersion.Should().Be(assembly.GetName().Version?.ToString());
        metadata.PreferredVersion.Should().Be(metadata.InformationalVersion ?? metadata.AssemblyVersion);
    }

    [Fact]
    public void EmbeddedLegalNoticeLoader_PreservesManifestOrderIdentityAndUtf8Text()
    {
        var assembly = typeof(LegalNoticeProvider).Assembly;

        var documents = EmbeddedLegalNoticeLoader.GetDocuments(
            assembly,
            LegalNoticeProvider.ExpectedEmbeddedResources);

        documents.Select(document => document.Title).Should().Equal(
            LegalNoticeProvider.ExpectedEmbeddedResources.Select(resource => resource.Title));
        documents.Select(document => document.ResourceName).Should().Equal(
            LegalNoticeProvider.ExpectedEmbeddedResources.Select(resource => resource.ResourceName));
        documents.Should().OnlyContain(document => !string.IsNullOrWhiteSpace(document.Text));
    }

    [Fact]
    public void EmbeddedLegalNoticeLoader_ReportsMissingResourceAsReleaseIntegrityError()
    {
        var load = () => EmbeddedLegalNoticeLoader.GetDocuments(
            typeof(AboutLegalSharedBoundaryTests).Assembly,
            [new LegalNoticeResource("Missing notice", "Missing.Legal.Notice.md")]);

        load.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing.Legal.Notice.md*was not found*");
    }

    [Fact]
    public void RenderersDependOnSharedAboutLegalAndVersionOwnership()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        Read(root, "src", "FreeX.App.Services", "AppHelpInfo.cs")
            .Should().Contain("AssemblyVersionMetadata.FromAssembly").And
            .NotContain("AssemblyInformationalVersionAttribute");
        Read(root, "src", "FreeX.App.Services", "LegalNoticeProvider.cs")
            .Should().Contain("EmbeddedLegalNoticeLoader.GetDocuments").And
            .NotContain("GetManifestResourceStream");

        Read(root, "freew", "FreeW.App.Presentation", "FreeWProductInfo.cs")
            .Should().Contain("AssemblyVersionMetadata.FromAssembly").And
            .NotContain("AssemblyInformationalVersionAttribute");
        Read(root, "freew", "FreeW.App.Presentation", "FreeWLegalNoticeManifest.cs")
            .Should().Contain("EmbeddedLegalNoticeLoader.GetDocuments");

        foreach (var renderer in new[] { "FreeW.App.Host", "FreeW.App.Avalonia" })
        {
            var legalDialog = Read(root, "freew", renderer, "LegalNoticesDialog.cs");
            legalDialog.Should().Contain("FreeWLegalNoticeProvider.GetDocuments");
            legalDialog.Should().Contain("FreeWLegalNoticesPresentation.Create");
            legalDialog.Should().NotContain("GetManifestResourceStream");
            legalDialog.Should().NotContain("class FreeWLegalNoticeProvider");
            legalDialog.Should().NotContain("windowTitle:");
            legalDialog.Should().NotContain("These notices are packaged with FreeW");
        }

        foreach (var renderer in new[] { "FreeX.App.Host", "FreeX.App.Avalonia" })
        {
            var legalDialog = Read(root, "src", renderer, "LegalNoticesDialog.cs");
            legalDialog.Should().Contain("FreeXLegalNoticesPresentation.Create");
            legalDialog.Should().NotContain("UiText.Get(\"LegalNotices_");
            legalDialog.Should().NotContain("windowTitle:");
        }

        var wpfLegalDialog = Read(root, "shared", "Free.Shared.Shell.Wpf", "SharedLegalNoticesDialog.cs");
        var avaloniaLegalDialog = Read(root, "shared", "Free.Shared.Shell.Avalonia", "AvaloniaLegalNoticesDialog.cs");
        foreach (var legalDialog in new[] { wpfLegalDialog, avaloniaLegalDialog })
        {
            legalDialog.Should().Contain("LegalNoticesDialogPresentation presentation");
            legalDialog.Should().Contain("presentation.SectionLinkHelpText");
            legalDialog.Should().Contain("presentation.ReadOnlyBodyHelpText");
            legalDialog.Should().Contain("presentation.CloseIsDefault");
            legalDialog.Should().Contain("presentation.CloseIsCancel");
            legalDialog.Should().NotContain("NonAutomationIdCharacter");
            legalDialog.Should().NotContain("Choose a legal notice section to read and copy.");
            legalDialog.Should().NotContain("Read-only legal notice text. Use Ctrl+C to copy selected text.");
        }

        Read(root, "freew", "FreeW.App.Host", "Backstage", "BackstageView.cs")
            .Should().Contain("EntryAssemblyVersion.Resolve()");
        Read(root, "freew", "FreeW.App.Avalonia", "Backstage", "BackstageView.cs")
            .Should().Contain("EntryAssemblyVersion.Resolve()").And
            .NotContain("Assembly.GetName().Version");
        Read(root, "freep", "FreeP.App.Host", "Backstage", "BackstageView.cs")
            .Should().Contain("EntryAssemblyVersion.Resolve()");
        Read(root, "freep", "FreeP.App.Avalonia", "Backstage", "BackstageView.cs")
            .Should().Contain("EntryAssemblyVersion.Resolve()");

        File.Exists(Path.Combine(
                root,
                "shared",
                "Free.Shared.Shell.Wpf",
                "SharedLegalNoticeLoader.cs"))
            .Should().BeFalse("portable embedded-resource loading belongs in Free.Shared.Shell");
        File.Exists(Path.Combine(
                root,
                "freew",
                "FreeW.App.Host",
                "FreeWLegalNoticeProvider.cs"))
            .Should().BeFalse("FreeW product manifests and providers belong above both renderers");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
}
