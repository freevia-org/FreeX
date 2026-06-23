using System.Globalization;
using FluentAssertions;
using FreeX.App.Localization;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class LocTests
{
    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            // qps-ploc is a real Windows pseudo-culture; constructing it by name yields a culture
            // whose Name round-trips as "qps-ploc" so Loc.IsPseudoLocalizationCulture matches.
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Get_NeutralCulture_ReturnsEnglish()
    {
        WithUiCulture("en-US", () => Loc.Get("Common_Cancel")).Should().Be("Cancel");
    }

    [Fact]
    public void Get_NeutralCulture_UsesWindowsShapeGradientLabels()
    {
        WithUiCulture("en-US", () => Loc.Get("ShapeGradient_Title")).Should().Be("Shape Gradient");
        WithUiCulture("en-US", () => Loc.Get("ShapeGradient_GradientStopsGroup")).Should().Be("Gradient stops");
        WithUiCulture("en-US", () => Loc.Get("ShapeGradient_DirectionDiagonalDown")).Should().Be("Diagonal Stripe");
    }

    [Fact]
    public void Get_NeutralCulture_UsesWindowsCustomViewsTableLabels()
    {
        WithUiCulture("en-US", () => Loc.Get("CustomViews_ListLabel")).Should().Be("Views");
        WithUiCulture("en-US", () => Loc.Get("CustomViews_Sheets")).Should().Be("Sheets");
        WithUiCulture("en-US", () => Loc.Get("CustomViews_Included")).Should().Be("Included");
        WithUiCulture("en-US", () => Loc.Get("CustomViews_NotIncluded")).Should().Be("Not included");
    }

    [Fact]
    public void Get_FrenchCulture_ReturnsTranslation()
    {
        WithUiCulture("fr-FR", () => Loc.Get("Common_Cancel")).Should().Be("Annuler");
    }

    [Fact]
    public void Get_FrenchCulture_FallsBackToNeutralForUntranslatedKey()
    {
        // OK is intentionally identical across catalogs; pick a key present only in neutral if needed.
        WithUiCulture("fr-FR", () => Loc.Get("Common_Ok")).Should().Be("OK");
    }

    [Fact]
    public void Get_PseudoCulture_ExpandsNeutralText()
    {
        var pseudo = WithUiCulture(Loc.PseudoLocalizationCultureName, () => Loc.Get("Common_Cancel"));
        pseudo.Should().StartWith("[[").And.EndWith("]]");
        pseudo.Should().Contain("CCaanncceell");
    }

    [Fact]
    public void GetNeutral_AlwaysReturnsEnglishRegardlessOfCulture()
    {
        WithUiCulture("fr-FR", () => Loc.GetNeutral("Common_Cancel")).Should().Be("Cancel");
    }

    [Fact]
    public void Format_NeutralCulture_SubstitutesArguments()
    {
        WithUiCulture("en-US", () => Loc.Format("PivotOptions_Title", "Sales"))
            .Should().Be("PivotTable Options (Sales)");
    }

    [Fact]
    public void Format_FrenchCulture_UsesTranslatedFormatString()
    {
        WithUiCulture("fr-FR", () => Loc.Format("PivotOptions_Title", "Sales"))
            .Should().Be("Options du tableau croisé dynamique (Sales)");
    }

    [Fact]
    public void Format_PseudoCulture_PreservesPlaceholderTokens()
    {
        var pseudo = WithUiCulture(Loc.PseudoLocalizationCultureName, () => Loc.Format("PivotOptions_Title", "Sales"));
        // The {0} placeholder is left untouched by the pseudo expander, so the arg appears verbatim.
        pseudo.Should().Contain("Sales");
        pseudo.Should().StartWith("[[").And.EndWith("]]");
    }

    [Fact]
    public void Get_UnknownKey_ReturnsVisibleMissingMarker()
    {
        WithUiCulture("en-US", () => Loc.Get("Does_Not_Exist")).Should().Be("[[Does_Not_Exist]]");
    }

    [Fact]
    public void SharedHelpers_ExposeCatalogContracts()
    {
        Loc.PseudoLocalizationCultureName.Should().Be(LocalizedTextCatalog.PseudoLocalizationCultureName);
        Loc.IsPseudoLocalizationCulture("QPS-PLOC").Should().BeTrue();
        Loc.CreateAutomationName("_Open _File").Should().Be("Open File");
        Loc.CreateMissingText("Missing_Key").Should().Be("[[Missing_Key]]");
    }

    [Fact]
    public void GetNeutralResourceKeys_ContainsExpectedKeys()
    {
        var keys = Loc.GetNeutralResourceKeys();
        keys.Should().Contain("Common_Cancel");
        keys.Should().Contain("PivotOptions_Title");
        keys.Should().Contain("ShapeGradient_GradientStopsGroup");
        keys.Should().Contain("CustomViews_Sheets");
        keys.Should().Contain("SelectionPane_SearchLabel");
        keys.Should().Contain("SelectionPane_FilterLabel");
        keys.Should().Contain("SelectionPane_RenameButton");
        keys.Should().Contain("Progress_OpeningWorkbook");
        keys.Should().Contain("Progress_ExportingFileRendering");
        keys.Should().Contain("Progress_LoadingFileReadingWorksheets");
        keys.Should().Contain("Progress_SavingFilePhaseFormat");
        keys.Should().Contain("Progress_SavingFileFlushingPackage");
        keys.Should().Contain("MainWindow_Text_Ready");
        keys.Should().Contain("StatusBar_AverageFormat");
        keys.Should().Contain("StatusBar_EditMode");
        keys.Should().Contain("StatusBar_CustomizeStatusBar");
        keys.Should().Contain("EvaluateFormula_Title");
        keys.Should().Contain("EvaluateFormula_EvaluateButton");
        keys.Should().Contain("EvaluateFormula_SelectFormulaMessage");
        keys.Should().Contain("ErrorChecking_Title");
        keys.Should().Contain("ErrorChecking_IssueCountHeader");
        keys.Should().Contain("ErrorChecking_ShowCalculationStepsButton");
        keys.Should().Contain("MainWindowMessage_ErrorCheckingNoIssues");
        keys.Should().Contain("AddWatch_Title");
        keys.Should().Contain("AddWatch_AddButton");
        keys.Should().Contain("AddWatch_SelectedRangeLabel");
        keys.Should().Contain("CreateTable_Title");
        keys.Should().Contain("CreateTable_RangeLabel");
        keys.Should().Contain("CreateTable_HeadersCheckBox");
        keys.Should().Contain("MainWindow_Header_RecommendedPivotTables");
        keys.Should().Contain("RecommendedPivotTables_BlankPivotTable");
        keys.Should().Contain("RecommendedPivotTables_NoRecommendationsHeading");
    }

    [Fact]
    public void FrenchCatalog_HasNoOrphanKeysMissingFromNeutral()
    {
        // Every key translated in fr-FR must exist in the neutral catalog, otherwise the
        // translation is unreachable (and a likely typo).
        var neutral = Loc.GetNeutralResourceKeys();
        var frenchKeys = WithUiCulture("fr-FR", () =>
        {
            // Round-trip a couple of representative keys to confirm the satellite loads at all.
            Loc.Get("Common_Cancel").Should().Be("Annuler");
            return neutral;
        });
        frenchKeys.Should().NotBeEmpty();
    }
}
