using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentDesignCommandOwnershipSourceGuardTests
{
    [Fact]
    public void AvaloniaDocumentViewKeepsNativeCaretStateButDelegatesPortableDesignCommands()
    {
        var source = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        source.Should().Contain("DocumentDesignEditingCoordinator DesignEdits");
        source.Should().Contain("DesignEdits.SetPageSettings(settings, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("DesignEdits.ApplyDocumentProperties(values)");
        source.Should().Contain("DesignEdits.ApplyTheme(theme)");
        source.Should().Contain("DesignEdits.SetPageColor(colorHex, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("DesignEdits.SetPageBorder(border, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("DesignEdits.SetWatermark(options, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("DesignEdits.TogglePageBorder(colorHex, widthPt, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("DesignEdits.SetWatermarkText(text, CurrentPageSettingsSectionIndex())");
        source.Should().Contain("PageSettingsSectionResolver.ResolveSectionIndex(_doc, _caret.Block)");
        source.Should().Contain("DocumentObjectEditingCoordinator.PlanWordArtInsertion(wordArt)");
        source.Should().Contain("ObjectEdits.InsertObjectRun(index, run)");
        source.Should().Contain("_caret = new DocPosition(index, BlockLength(index))");
        source.Should().NotContain("new DesignCatalogCommand(");
        source.Should().NotContain("new SetPageSettingsCommand(");
        source.Should().NotContain("new ApplyDocumentPropertiesCommand(");
        source.Should().NotContain("new SetPageColorCommand(");
        source.Should().NotContain("new SetPageBorderCommand(");
        source.Should().NotContain("new SetWatermarkCommand(");
        source.Should().NotContain("new InsertObjectRunCommand(");
        source.Should().NotContain("NormalizePageColor(");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx") }
                .Concat(parts)
                .ToArray()));
}
