using FluentAssertions;
using FreeX.App.Presentation.Shell;
using FreeX.Core.IO;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class DeferredCommandMessagePlannerTests
{
    [Fact]
    public void WorkbookTheme_UsesLiteralCommandTitleAndResourceBody()
    {
        var plan = DeferredCommandMessagePlanner.WorkbookTheme("Themes");

        plan.Title.LiteralText.Should().Be("Themes");
        plan.Title.ResourceKey.Should().BeNull();
        plan.Body.ResourceKey.Should().Be("DeferredCommand_WorkbookTheme_Body");
        plan.Body.Arguments.Should().ContainSingle()
            .Which.LiteralText.Should().Be("Themes");
    }

    [Fact]
    public void SimpleDeferredMessages_ExposeResourceKeys()
    {
        var templates = DeferredCommandMessagePlanner.OnlineTemplatesExcluded();

        templates.Title.ResourceKey.Should().Be("DeferredCommand_OnlineTemplates_Title");
        templates.Body.ResourceKey.Should().Be("DeferredCommand_OnlineTemplates_Body");

        DeferredCommandMessagePlanner.TrustCenterSettings().Body.ResourceKey
            .Should().Be("DeferredCommand_TrustCenter_Body");
    }

    [Fact]
    public void UnsupportedXlsxFeatureOpenWarning_PlansFeatureListAndDigitalSignatureSuffix()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.DigitalSignatures, "_xmlsignatures/sig1.xml")
        ]);

        var plan = DeferredCommandMessagePlanner.UnsupportedXlsxFeatureOpenWarning(report);

        plan.Title.ResourceKey.Should().Be("DeferredCommand_UnsupportedXlsxFeatureOpenWarning_Title");
        plan.Body.ResourceKey.Should().Be("DeferredCommand_UnsupportedXlsxFeatureOpenWarning_Body");
        plan.Body.Arguments.Should().HaveCount(2);
        plan.Body.Arguments[0].SortResolvedText.Should().BeTrue();
        plan.Body.Arguments[0].TextItems.Select(item => item.ResourceKey)
            .Should()
            .Equal(
                "UnsupportedXlsxFeatureKind_Macros",
                "UnsupportedXlsxFeatureKind_DigitalSignatures");
        plan.Body.Arguments[1].ResourceKey
            .Should()
            .Be("DeferredCommand_UnsupportedXlsxFeature_DigitalSignatureWarningSuffix");
    }

    [Theory]
    [InlineData(XlsxUnsupportedFeatureKind.Macros, "UnsupportedXlsxFeatureKind_Macros")]
    [InlineData(XlsxUnsupportedFeatureKind.Charts, "UnsupportedXlsxFeatureKind_Charts")]
    [InlineData(XlsxUnsupportedFeatureKind.EmbeddedObjects, "UnsupportedXlsxFeatureKind_EmbeddedObjects")]
    [InlineData(XlsxUnsupportedFeatureKind.CustomXmlParts, "UnsupportedXlsxFeatureKind_CustomXmlParts")]
    [InlineData(XlsxUnsupportedFeatureKind.ConditionalFormats, "UnsupportedXlsxFeatureKind_ConditionalFormats")]
    [InlineData(XlsxUnsupportedFeatureKind.DrawingObjects, "UnsupportedXlsxFeatureKind_DrawingObjects")]
    [InlineData(XlsxUnsupportedFeatureKind.PowerQuery, "UnsupportedXlsxFeatureKind_PowerQuery")]
    [InlineData(XlsxUnsupportedFeatureKind.DataModel, "UnsupportedXlsxFeatureKind_DataModel")]
    [InlineData(XlsxUnsupportedFeatureKind.LinkedDataTypes, "UnsupportedXlsxFeatureKind_LinkedDataTypes")]
    [InlineData(XlsxUnsupportedFeatureKind.ThreadedComments, "UnsupportedXlsxFeatureKind_ThreadedComments")]
    [InlineData(XlsxUnsupportedFeatureKind.TrackChanges, "UnsupportedXlsxFeatureKind_TrackChanges")]
    [InlineData(XlsxUnsupportedFeatureKind.FormControls, "UnsupportedXlsxFeatureKind_FormControls")]
    [InlineData(XlsxUnsupportedFeatureKind.DigitalSignatures, "UnsupportedXlsxFeatureKind_DigitalSignatures")]
    [InlineData(XlsxUnsupportedFeatureKind.CustomRibbonUi, "UnsupportedXlsxFeatureKind_CustomRibbonUi")]
    [InlineData(XlsxUnsupportedFeatureKind.OfficeAddIns, "UnsupportedXlsxFeatureKind_OfficeAddIns")]
    [InlineData(XlsxUnsupportedFeatureKind.LiveWebQueries, "UnsupportedXlsxFeatureKind_LiveWebQueries")]
    [InlineData(XlsxUnsupportedFeatureKind.SensitivityLabels, "UnsupportedXlsxFeatureKind_SensitivityLabels")]
    [InlineData(XlsxUnsupportedFeatureKind.SmartArtDiagrams, "UnsupportedXlsxFeatureKind_SmartArtDiagrams")]
    [InlineData(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes, "UnsupportedXlsxFeatureKind_UnsupportedSheetTypes")]
    public void UnsupportedXlsxFeatureKindText_MapsKnownKindsToResourceKeys(
        XlsxUnsupportedFeatureKind kind,
        string expectedResourceKey)
    {
        DeferredCommandMessagePlanner.UnsupportedXlsxFeatureKindText(kind)
            .ResourceKey
            .Should()
            .Be(expectedResourceKey);
    }

    [Fact]
    public void HostDeferredCommandMessages_RoutesThroughPresentationPlanner()
    {
        var hostRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var facadePath = Path.Combine(hostRoot, "DeferredCommandMessages.cs");
        var resolverSource = File.ReadAllText(Path.Combine(hostRoot, "WpfResourceKeyTextResolver.cs"));
        var consumerSources = new[]
        {
            "MainWindow.Backstage.cs",
            "MainWindow.PageLayout.cs",
            "MainWindow.ScreenshotTour.cs",
            "OptionsDialog.xaml.cs"
        }
            .Select(fileName => File.ReadAllText(Path.Combine(hostRoot, fileName)))
            .ToArray();
        var combinedConsumers = string.Join(Environment.NewLine, consumerSources);

        File.Exists(facadePath).Should().BeFalse("the WPF compatibility facade was removed");
        resolverSource.Should().Contain("DeferredCommandMessageResolver.Resolve(");
        consumerSources.Should().OnlyContain(source =>
            source.Contains("WpfResourceKeyTextResolver.Resolve(", StringComparison.Ordinal));
        combinedConsumers.Should().Contain("DeferredCommandMessagePlanner.");
        combinedConsumers.Should().NotContain("\"DeferredCommand_WorkbookTheme_Body\"");
        combinedConsumers.Should().NotContain("\"DeferredCommand_UnsupportedXlsxFeatureOpenWarning_Body\"");
        combinedConsumers.Should().NotContain("XlsxUnsupportedFeatureKind.Macros =>");
    }
}
