using System.Text.RegularExpressions;

using FreeX.App.Localization;

namespace FreeX.App.Avalonia.Tests;

public sealed class FreeXLocalizationFinalTailTests
{
    private static readonly Regex OptionsTextCall = new(
        "OptionsText\\(\"(?<key>[^\"]+)\"\\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void OptionsDialog_UsesCanonicalResourcesWithoutAnEnglishFallbackCatalog()
    {
        var source = Source("MainWindow.Options.cs");
        var neutralKeys = Loc.GetNeutralResourceKeys().ToHashSet(StringComparer.Ordinal);
        var usedKeys = OptionsTextCall.Matches(source)
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        usedKeys.Where(key => !neutralKeys.Contains(key)).Should().BeEmpty();
        source.Should().Contain("private static string OptionsText(string resourceKey) =>");
        source.Should().Contain("StripDisplayMnemonic(UiText.Get(resourceKey));");
        source.Should().NotContain("resourceKey switch");
        source.Should().NotContain("LooksLikeMissingResource");
        source.Should().NotContain("NormalizeOptionAccessText");
    }

    [Fact]
    public void IdentifiedRendererTail_DoesNotOwnUserFacingEnglishLiterals()
    {
        var options = Source("MainWindow.Options.cs");
        options.Should().NotContain("\"FreeX Quick Access Toolbar\"");
        options.Should().NotContain("\"Quick Access Toolbar import requires a local file path.\"");
        options.Should().NotContain("\"Could not import Quick Access Toolbar customization.\"");
        options.Should().NotContain("\"Quick Access Toolbar export requires a local file path.\"");

        var comments = Source("MainWindow.CommentInlineEditor.cs");
        comments.Should().NotContain("RefreshShell(\"Ready\")");
        comments.Should().NotContain("SetName(editor, \"Note\")");
        comments.Should().NotContain("SetName(_inlineNoteEditBox, \"Note\")");
        comments.Should().NotContain("Text = $\"Note -");
        comments.Should().NotContain("Text = $\"Comment -");
        comments.Should().NotContain("ShowInlineNoteError(\"Enter a note.\")");

        Source("MainWindow.cs").Should().NotContain("Header = \"(No Recent Workbooks)\"");

        var drawBorder = Source("MainWindow.DrawBorder.cs");
        drawBorder.Should().NotContain("RefreshShell(\"Draw Border mode active");
        drawBorder.Should().NotContain("RefreshShell(\"Draw Border mode cancelled.\")");
        drawBorder.Should().NotContain("\"Draw Border failed.\"");
        drawBorder.Should().NotContain("RefreshShell($\"Draw Border applied");

        var keyboard = Source("MainWindow.KeyboardParity.cs");
        foreach (var literal in new[]
                 {
                     "Inserted current time.",
                     "Inserted current date.",
                     "Could not insert the current date or time.",
                     "Showing outline symbols.",
                     "Hiding outline symbols.",
                     "Could not change outline symbols.",
                     "Copied value from above.",
                     "Copied formula from above.",
                     "Could not copy from above.",
                     "Inserted chart sheet.",
                     "Selected formula dependents.",
                     "Selected formula precedents.",
                 })
        {
            keyboard.Should().NotContain($"\"{literal}\"");
        }

        keyboard.Should().NotContain("$\"No {depth} dependents\"");
        keyboard.Should().NotContain("$\"No {depth} precedents\"");
        keyboard.Should().NotContain("RefreshShell(\"Ready\")");
    }

    [Fact]
    public void FinalTailResourceKeys_ResolveAsRealText()
    {
        var keys = new[]
        {
            "Options_CategoryLanguage",
            "Options_CategoryEaseOfAccess",
            "Options_CategoryAdvanced",
            "Options_CategoryCustomizeRibbon",
            "Options_CategoryQuickAccessToolbar",
            "Options_CategoryAddIns",
            "Options_CategoryTrustCenter",
            "Options_AppLanguageSystemDefault",
            "Options_AppLanguageEnglishUnitedStates",
            "Options_QuickAccessToolbarFileType",
            "Options_QuickAccessImportRequiresLocalPath",
            "Options_QuickAccessImportFailed",
            "Options_QuickAccessExportRequiresLocalPath",
            "DrawBorder_ModeActiveStatus",
            "DrawBorder_ModeCancelledStatus",
            "DrawBorder_FailedMessage",
            "DrawBorder_AppliedStatusFormat",
            "KeyboardLoc_InsertedCurrentTime",
            "KeyboardLoc_InsertedCurrentDate",
            "KeyboardLoc_InsertCurrentDateOrTimeFailed",
            "KeyboardLoc_ShowingOutlineSymbols",
            "KeyboardLoc_HidingOutlineSymbols",
            "KeyboardLoc_ChangeOutlineSymbolsFailed",
            "KeyboardLoc_CopiedValueFromAbove",
            "KeyboardLoc_CopiedFormulaFromAbove",
            "KeyboardLoc_CopyFromAboveFailed",
            "KeyboardLoc_InsertedChartSheet",
            "KeyboardLoc_NoTraceableDependents",
            "KeyboardLoc_NoDirectDependents",
            "KeyboardLoc_NoTraceablePrecedents",
            "KeyboardLoc_NoDirectPrecedents",
            "KeyboardLoc_SelectedFormulaDependents",
            "KeyboardLoc_SelectedFormulaPrecedents",
        };

        foreach (var key in keys)
        {
            Loc.GetNeutralResourceKeys().Should().Contain(key);
            Loc.GetNeutral(key).Should().NotBeNullOrWhiteSpace().And.NotBe($"[[{key}]]");
        }
    }

    private static string Source(string fileName) =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", fileName);
}
