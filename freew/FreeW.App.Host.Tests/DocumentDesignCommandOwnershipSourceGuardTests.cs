using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class DocumentDesignCommandOwnershipSourceGuardTests
{
    [Fact]
    public void WpfDocumentViewKeepsNativeProjectionButDelegatesPortableDesignCommands()
    {
        var source = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");

        source.Should().Contain("DocumentDesignEditingCoordinator DesignEdits");
        source.Should().Contain("DesignEdits.UpdatePage(apply, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("DesignEdits.ApplyDocumentProperties(values)");
        source.Should().Contain("DesignEdits.ApplyTheme(theme)");
        source.Should().Contain("DesignEdits.SetPageColor(colorHex, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("DesignEdits.SetWatermark(options, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("DocumentObjectEditingCoordinator.PlanWordArtInsertion(wordArt)");
        source.Should().Contain("ObjectEdits.SetWordArtStyle(");
        source.Should().Contain("ObjectEdits.SetWordArtWarp(");
        source.Should().Contain("ObjectEdits.SetAltText(");
        source.Should().Contain("InsertInlineContainer(BuildWordArtRun(");
        source.Should().NotContain("new DesignCatalogCommand(");
        source.Should().NotContain("new SetPageSettingsCommand(");
        source.Should().NotContain("new ApplyDocumentPropertiesCommand(");
        source.Should().NotContain("new SetWordArtStyleCommand(");
        source.Should().NotContain("new SetWordArtWarpCommand(");
        source.Should().NotContain("new SetWordArtAltTextCommand(");
        source.Should().NotContain("NormalizePageColor(");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx") }
                .Concat(parts)
                .ToArray()));
}
