using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class DeferredCommandMessageTests
{
    private static readonly ResourceKeyTextResolver TextResolver = new(UiText.Get, UiText.Format);

    private static class TestDeferredCommandMessages
    {
        public static DeferredCommandMessage WorkbookTheme(string commandName) =>
            Resolve(DeferredCommandMessagePlanner.WorkbookTheme(commandName));

        public static DeferredCommandMessage MultiWindow(string commandName) =>
            Resolve(DeferredCommandMessagePlanner.MultiWindow(commandName));

        public static DeferredCommandMessage OnlineTemplatesExcluded() =>
            Resolve(DeferredCommandMessagePlanner.OnlineTemplatesExcluded());

        public static DeferredCommandMessage LocalAccountInfo() =>
            Resolve(DeferredCommandMessagePlanner.LocalAccountInfo());

        public static DeferredCommandMessage PivotTableModelFirst() =>
            Resolve(DeferredCommandMessagePlanner.PivotTableModelFirst());

        public static DeferredCommandMessage AutoCorrectOptions() =>
            Resolve(DeferredCommandMessagePlanner.AutoCorrectOptions());

        public static DeferredCommandMessage EditingLanguages() =>
            Resolve(DeferredCommandMessagePlanner.EditingLanguages());

        public static DeferredCommandMessage RibbonCustomizationImportExport() =>
            Resolve(DeferredCommandMessagePlanner.RibbonCustomizationImportExport());

        public static DeferredCommandMessage OfficeAddIns() =>
            Resolve(DeferredCommandMessagePlanner.OfficeAddIns());

        public static DeferredCommandMessage TrustCenterSettings() =>
            Resolve(DeferredCommandMessagePlanner.TrustCenterSettings());

        public static DeferredCommandMessage UnsupportedXlsxFeatureSaveWarning(XlsxFeatureReport report) =>
            Resolve(DeferredCommandMessagePlanner.UnsupportedXlsxFeatureSaveWarning(report));

        public static DeferredCommandMessage UnsupportedXlsxFeatureOpenWarning(XlsxFeatureReport report) =>
            Resolve(DeferredCommandMessagePlanner.UnsupportedXlsxFeatureOpenWarning(report));

        public static string FormatUnsupportedXlsxFeatureKind(XlsxUnsupportedFeatureKind kind) =>
            DeferredCommandMessageResolver.ResolveText(
                DeferredCommandMessagePlanner.UnsupportedXlsxFeatureKindText(kind),
                TextResolver);

        private static DeferredCommandMessage Resolve(DeferredCommandMessagePlan plan) =>
            DeferredCommandMessageResolver.Resolve(plan, TextResolver);
    }

    [Fact]
    public void WorkbookThemeMessage_NamesDeferredThemeModel()
    {
        var message = TestDeferredCommandMessages.WorkbookTheme("Themes");

        message.Title.Should().Be("Themes");
        message.Body.Should().Contain("deferred");
        message.Body.Should().Contain("workbook theme model");
        message.Body.Should().Contain("documented parity gap");
    }

    [Theory]
    [InlineData("View Side by Side")]
    [InlineData("Synchronous Scrolling")]
    [InlineData("Reset Window Position")]
    public void MultiWindowMessage_NamesDeferredWindowHosting(string commandName)
    {
        var message = TestDeferredCommandMessages.MultiWindow(commandName);

        message.Title.Should().Be(commandName);
        message.Body.Should().Contain("deferred");
        message.Body.Should().Contain("multi-window workbook hosting");
        message.Body.Should().Contain("documented parity gap");
    }

    [Fact]
    public void OnlineTemplatesMessage_NamesExternalMicrosoftServiceExclusion()
    {
        var message = TestDeferredCommandMessages.OnlineTemplatesExcluded();

        message.Title.Should().Be("Online Templates");
        message.Body.Should().Contain("excluded");
        message.Body.Should().Contain("external Microsoft template service");
    }

    [Fact]
    public void AccountMessage_NamesLocalAccountDecision()
    {
        var message = TestDeferredCommandMessages.LocalAccountInfo();

        message.Title.Should().Be("Account");
        message.Body.Should().Contain("Microsoft account integration");
        message.Body.Should().Contain("not implemented");
        message.Body.Should().Contain("local files");
        message.Body.Should().Contain("Options");
    }

    [Fact]
    public void PivotTableMessage_NamesModelFirstPivotSupport()
    {
        var message = TestDeferredCommandMessages.PivotTableModelFirst();

        message.Title.Should().Be("PivotTable");
        message.Body.Should().Contain("loads and saves PivotTable");
        message.Body.Should().Contain("pivot caches");
        message.Body.Should().Contain("preserves native PivotTable package parts");
        message.Body.Should().Contain("Field List");
        message.Body.Should().Contain("slicer/timeline");
        message.Body.Should().Contain("remain partial");
    }

    [Fact]
    public void OptionsSecondaryMessages_NameHonestUnsupportedBoundaries()
    {
        TestDeferredCommandMessages.AutoCorrectOptions().Body.Should().Contain("AutoCorrect replacement dictionaries");
        TestDeferredCommandMessages.EditingLanguages().Body.Should().Contain("language packs");
        TestDeferredCommandMessages.RibbonCustomizationImportExport().Body.Should().Contain("Custom Ribbon UI");
        TestDeferredCommandMessages.OfficeAddIns().Body.Should().Contain("not installed, loaded, or executed");
        TestDeferredCommandMessages.TrustCenterSettings().Body.Should().Contain("does not execute VBA macros");
    }
}
