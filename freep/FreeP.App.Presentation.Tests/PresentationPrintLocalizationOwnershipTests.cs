using System.Globalization;
using FreeP.App.Localization;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPrintLocalizationOwnershipTests
{
    [Fact]
    public void NeutralPrintSurface_PreservesExistingEnglishText()
    {
        WithUiCulture("en-US", () =>
        {
            var surface = PresentationBackstagePrintSurfacePlanner.Build(BuildPlan());

            surface.SettingsHeading.Should().Be("Settings");
            surface.CustomRangeApplyLabel.Should().Be("Apply range");
            surface.ChoiceGroups.Select(group => group.Heading)
                .Should().Equal("Output options", "Preview", "Layouts", "Slide range");
            surface.PrintHeading.Should().Be("Print");
            surface.PrintActions.Should().OnlyContain(action => action.Label.StartsWith("Print "));
            return true;
        });
    }

    [Fact]
    public void PrintSurfaceAndDialogText_RespondToPseudoLocalization()
    {
        WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            var surface = PresentationBackstagePrintSurfacePlanner.Build(BuildPlan());

            surface.SettingsHeading.Should().StartWith("[[").And.EndWith("]]");
            surface.CustomRangeApplyLabel.Should().Contain("AAppppllyy");
            PresentationShellTextCatalog.PrintDialogText("Print_Dialog_PrinterLabel")
                .Should().Contain("PPrriinntteerr").And.StartWith("[[");
            return true;
        });
    }

    [Fact]
    public void PrintSurfaceCatalogRequiredKeys_ExistInNeutralResources()
    {
        var neutralKeys = Loc.GetNeutralResourceKeys();

        PresentationShellTextCatalog.PrintSurfaceRequiredResourceKeys
            .Should().OnlyContain(key => neutralKeys.Contains(key));
    }

    [Fact]
    public void RenderersConsumePortablePrintTextWithoutSemanticEnglishLiterals()
    {
        var planner = Read("freep", "FreeP.App.Presentation", "Backstage", "PresentationBackstagePrintSurfacePlanner.cs");
        var avalonia = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var cups = Read("freep", "FreeP.App.Avalonia", "Printing", "CupsPrintDialog.cs");
        var sharedDialog = Read("shared", "Free.Shared.Shell.Avalonia", "AvaloniaPrintDialogWorkflow.cs");
        var wpf = Read("freep", "FreeP.App.Host", "Backstage", "BackstageView.cs");

        planner.Should().NotContain("SettingsHeading: \"Settings\"");
        planner.Should().NotContain("CustomRangeApplyLabel: \"Apply range\"");
        planner.Should().NotContain("PrintHeading: \"Print\"");
        avalonia.Should().NotContain("PlaceholderText = \"Click to add notes\"");
        avalonia.Should().NotContain("ShowBackstage(\"Print\")");
        cups.Should().Contain("Text = BuildText()");
        sharedDialog.Should().Contain("var text = options.Text;");
        sharedDialog.Should().NotContain("AddRow(content, \"Printer:\"");
        wpf.Should().Contain("_backstage.Show(surface.PrintHeading)");
    }

    private static PresentationPrintBackstagePlan BuildPlan() =>
        PresentationPrintBackstagePlanner.Build(
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
            slideCount: 3,
            hostCapabilities: PresentationNativePrintHandoffHostCapabilities.Available("test host"));

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.Find(parts));

    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var original = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = original;
        }
    }
}
