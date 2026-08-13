using System.Xml.Linq;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWFinalTailOwnershipSourceTests
{
    [Fact]
    public void FormattingRenderersProjectThePortableSession()
    {
        var wpf = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("CreateFormattingSession(editor)");
            source.Should().Contain("session.ApplyParagraphValue(");
            source.Should().Contain("session.ApplyParagraphStyle(");
            source.Should().Contain("session.ApplyTheme(");
            source.Should().Contain("session.ApplyStyleSet(");
        }

        wpf.Should().NotContain("private sealed class IndentLeftCommand");
        wpf.Should().NotContain("private sealed class IndentRightCommand");
        wpf.Should().NotContain("private sealed class SpaceBeforeCommand");
        wpf.Should().NotContain("private sealed class SpaceAfterCommand");
        avalonia.Should().NotContain("DocumentTheme.FindByName(context.SelectedValue)");
        avalonia.Should().NotContain("DocumentStyleSet.FindByName(context.SelectedValue)");
    }

    [Fact]
    public void TestAndValidationFriendGrantsAreConditional()
    {
        AssertFriendCondition(
            "'$(FreeWHostTestSupport)' == 'true'",
            "FreeW.App.Avalonia.Tests",
            "freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj");
        AssertFriendCondition(
            "'$(FreeWValidationHost)' == 'true'",
            "FreeW.Validation.Avalonia",
            "freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj");
        foreach (var friend in new[] { "FreeW.App.Host.Tests", "FreeW.RenderCompare", "FreeW.FidelityRender" })
        {
            AssertFriendCondition(
                "'$(FreeWHostTestSupport)' == 'true'",
                friend,
                "freew", "FreeW.App.Host", "FreeW.App.Host.csproj");
        }
    }

    [Fact]
    public void IdentifiedDialogsResolvePortableLocalizedText()
    {
        var recovery = Read("freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs");
        var quickParts = Read("freew", "FreeW.App.Avalonia", "FinalCommandParityDialogs.cs");
        var chart = Read("freew", "FreeW.App.Avalonia", "MediaDialogParity.cs");
        var wpf = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");

        recovery.Should().Contain("AutosaveRecoveryTextCatalog.Resolve(UiText.Get)");
        recovery.Should().NotContain("Content = \"Recover\"");
        recovery.Should().NotContain("Content = \"Skip\"");
        quickParts.Should().Contain("QuickPartCommandPlanner.ResolveText(UiText.Get)");
        quickParts.Should().NotContain("Title = \"Save to Quick Parts\"");
        chart.Should().Contain("InsertChartDialogPlanner.ResolveText(UiText.Get)");
        chart.Should().NotContain("Header = \"Add Row\"");
        chart.Should().NotContain("Header = \"Remove Row\"");
        wpf.Should().Contain("QuickPartCommandPlanner.ResolveText(UiText.Get)");
    }

    [Fact]
    public void MetricBackedDialogBlocksKeepOnlyNativeProjection()
    {
        var wpfTable = Read("freew", "FreeW.App.Host", "TablePropertiesDialog.cs");
        var avaloniaTable = Read("freew", "FreeW.App.Avalonia", "TableDialogs.cs");
        var wpfReferences = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaReferences = Read("freew", "FreeW.App.Avalonia", "ReferencesDialogs.cs");

        foreach (var source in new[] { wpfTable, avaloniaTable })
        {
            source.Should().Contain("new TablePropertiesDialogSession(");
            source.Should().Contain("_session.PlanAcceptance(input)");
            source.Should().NotContain("TablePropertiesDialogPlanner.TryBuildResult(");
        }

        foreach (var source in new[] { wpfReferences, avaloniaReferences })
        {
            source.Should().Contain("new SourceManagementAuthorEditorSession(");
            source.Should().Contain("session.SelectMode(");
            source.Should().Contain("session.AddPersonalAuthorRow(");
            source.Should().Contain("session.RemoveFinalPersonalAuthorRow(");
            source.Should().Contain("session.Accept(");
            source.Should().NotContain("NormalizePrimaryAuthorEditorState(");
        }
    }

    [Fact]
    public void PictureDecodersShareCancellationAndFailurePolicy()
    {
        var wpf = Read("freew", "FreeW.App.Host", "PictureImport", "WpfPictureImportPorts.cs");
        var avalonia = Read("freew", "FreeW.App.Avalonia", "PictureImport", "AvaloniaPictureImportPorts.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FreeWPictureDecoderPolicy.DecodeOrUnavailable(");
            source.Should().NotContain("FreeWPictureDecoderFacts.Unavailable");
        }
    }

    [Fact]
    public void FloatingObjectCommandsSharePortableParsingAndEligibilityPolicy()
    {
        var wpf = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var profile = Read(
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "FreeWRibbonEditorExecutionProfile.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FreeWRibbonEditorExecutionProfile.RegisterFloatingPositionCommands(");
            source.Should().Contain("FreeWRibbonFloatingObjectCommandFactory.CreateSize(");
            source.Should().Contain("FreeWRibbonDefinitionData.FloatingPositionPresets");
            source.Should().NotContain("private static void RegisterFloatingPositionCommands(");
            source.Should().NotContain("FreeWRibbonFloatingObjectCommandFactory.CreatePosition(");
            source.Should().NotContain("private sealed class FloatingObjectPositionCommand");
            source.Should().NotContain("private sealed class FloatingObjectSizeCommand");
        }

        profile.Should().Contain("public static void RegisterFloatingPositionCommands(");
        profile.Should().Contain("FreeWRibbonFloatingObjectCommandFactory.CreatePosition(");
        profile.Should().Contain("FreeWRibbonFloatingObjectCommandFactory.CreatePositionPreset(");

        wpf.Should().NotContain("private sealed class ImagePositionCommand");
        wpf.Should().NotContain("private sealed class ImageSizeCommand");
        wpf.Should().NotContain("private sealed class ShapePositionCommand");
        wpf.Should().NotContain("private sealed class ShapeSizeCommand");
        wpf.Should().Contain("HasSelection: target =>");
        wpf.Should().Contain("CanArrange: editor.CanArrangeFloatingObjects");
        wpf.Should().NotContain("HasSelection: static _ => true");
        wpf.Should().NotContain("CanArrange: static _ => true");
    }

    private static void AssertFriendCondition(string expectedCondition, string friend, params string[] parts)
    {
        var project = XDocument.Load(TestWorkspaceFileLocator.Find(parts));
        var item = project.Descendants("InternalsVisibleTo")
            .Single(element => string.Equals((string?)element.Attribute("Include"), friend, StringComparison.Ordinal));
        ((string?)item.Parent?.Attribute("Condition")).Should().Be(expectedCondition);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(parts));
}
