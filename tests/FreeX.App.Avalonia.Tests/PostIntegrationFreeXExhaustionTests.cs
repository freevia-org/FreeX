using FreeX.App.Localization;

namespace FreeX.App.Avalonia.Tests;

public sealed class PostIntegrationFreeXExhaustionTests
{
    [Fact]
    public void SheetTabRenderer_ConsumesCanonicalCommandMetadata()
    {
        var source = Source("MainWindow.cs");

        source.Should().Contain("SheetTabContextMenuPlanner.BuildSheetTabCommands(");
        source.Should().Contain("Header(SheetTabContextMenuAction.Rename)");
        source.Should().Contain("Enabled(SheetTabContextMenuAction.DeleteSheet)");
        source.Should().Contain("Header(SheetTabContextMenuAction.SelectAllSheets)");
        source.Should().NotContain("CreateSheetTabContextMenuItem(tab, \"Rename...\"");
        source.Should().NotContain("CreateSheetTabContextMenuItem(tab, \"Delete Sheet\"");
        source.Should().NotContain("CreateSheetTabContextMenuItem(tab, \"Select All Sheets\"");
        source.Should().NotContain("CreateSheetTabContextMenuItem(tab, \"Ungroup Sheets\"");
    }

    [Fact]
    public void AuditedRendererTail_DoesNotOwnItsUserFacingEnglish()
    {
        var source = Source("MainWindow.cs");

        source.Should().NotContain("private const string SheetTabContextHelpText");
        source.Should().NotContain("Title = \"Confirm\"");
        source.Should().NotContain("Content = \"No\"");
        source.Should().NotContain("RefreshShell(\"Selected all visible sheets\")");
        source.Should().NotContain("ToolTip.SetTip(dropdown, \"Pick from list\")");
        source.Should().NotContain("SetName(statisticsBlock, \"Workbook Statistics\")");
        source.Should().NotContain("Summarizes sheet, cell, formula, comment, and object counts for the workbook.\"");
    }

    [Fact]
    public void AuditedRendererTail_ResourcesResolveAsRealText()
    {
        foreach (var key in new[]
                 {
                     "DataValidation_SelectAValueFromList",
                     "SelectionMoveOverwrite_No",
                     "SheetTabs_ContextHelpText",
                     "SheetTabs_SelectedAllVisibleStatus",
                 })
        {
            Loc.GetNeutralResourceKeys().Should().Contain(key);
            Loc.GetNeutral(key).Should().NotBeNullOrWhiteSpace().And.NotBe($"[[{key}]]");
        }
    }

    private static string Source(string fileName) =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", fileName);
}
