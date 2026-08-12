using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Panes;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R131 family sweep: FreeX.App.Avalonia's Gradient Fill dialog referenced a nonexistent resx key
/// (<c>UiText.Get("FormatCells_InvalidColor")</c>), surfacing the raw <c>[[key]]</c> sentinel to the
/// user instead of a message. This durable contract test guards the FreeW WPF host the same way:
/// every literal <c>UiText.Get/Format/GetNeutral("Key")</c> call site under FreeW.App.Host must
/// resolve against the neutral resource catalog (app-owned or shared).
/// Mirrors FreeX.App.Host.Tests.LocalizationUsageTests.
/// </summary>
public sealed class LocalizationKeyIntegrityTests
{
    [Fact]
    public void AppSourceLocalizationKeys_AllExistInNeutralResources() =>
        LocalizationKeyIntegrityTestSupport.AssertAllLiteralUiTextKeysExist(
            "FreeW.slnx",
            UiText.GetNeutralResourceKeys(),
            requireLiteralUses: true,
            "freew",
            "FreeW.App.Host");

    /// <summary>
    /// Sibling no-regression: a representative set of real, currently-used keys must keep
    /// resolving to real, non-sentinel text, proving the sweep above is not vacuously green.
    /// </summary>
    [Fact]
    public void RepresentativeCommonKeys_ResolveToRealNonSentinelText() =>
        LocalizationKeyIntegrityTestSupport.AssertKeysResolveToRealNonSentinelText(
            UiText.GetNeutral,
            "Common_Ok",
            "Common_Cancel");

    [Fact]
    public void SharedFreeWSurfaceCatalogKeys_AllExistInNeutralResources()
    {
        var available = UiText.GetNeutralResourceKeys();
        var required = MailMergeRuleDialogPlanner.RequiredResourceKeys
            .Concat(NavigationPaneTextCatalog.RequiredResourceKeys)
            .Concat(SmartArtDialogPlanner.RequiredResourceKeys)
            .Concat(TableTextConversionDialogPlanner.RequiredResourceKeys)
            .Append(TableFormulaDialogPlanner.CursorOutsideTableResourceKey)
            .Append(TablePropertiesDialogPlanner.CursorOutsideTableResourceKey)
            .Concat(FreeWBackstagePaneTextCatalog.RequiredResourceKeys)
            .Concat(SourceManagementDialogPlanner.RequiredResourceKeys)
            .Concat(BackstageInfoSafetyPanePlanner.RequiredResourceKeys)
            .Concat(DesignDialogTextCatalog.RequiredResourceKeys)
            .Concat(DrawTableCommandPlanner.RequiredResourceKeys)
            .Concat(ProofingLanguageDialogPlanner.RequiredResourceKeys)
            .Concat(AltTextDialogPlanner.RequiredResourceKeys)
            .Concat(QuickPartCommandPlanner.RequiredResourceKeys)
            .Concat(InsertChartDialogPlanner.RequiredResourceKeys)
            .Concat(ChartTitleDialogPlanner.RequiredResourceKeys)
            .Concat(ChartAxisTitlesDialogPlanner.RequiredResourceKeys)
            .Concat(ChartSizeDialogPlanner.RequiredResourceKeys)
            .Concat(AutosaveRecoveryTextCatalog.RequiredResourceKeys)
            .Distinct(StringComparer.Ordinal);

        required.Should().OnlyContain(key => available.Contains(key));
    }
}
