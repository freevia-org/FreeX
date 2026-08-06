using System.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class NonChartDialogSurfacePlanTests
{
    [Fact]
    public void SurfacePlan_RejectsDuplicateFieldAndActionIdentifiers()
    {
        var field = new PresentationDialogFieldPlan<SlideSizeDialogSurfaceField>(
            SlideSizeDialogSurfaceField.Width,
            PresentationDialogControlKind.Text,
            "Width",
            "Slide width",
            "FreeP.Test.Width");
        var action = new PresentationDialogActionPlan<SlideSizeDialogAction>(
            SlideSizeDialogAction.Accept,
            "OK",
            "Apply",
            "FreeP.Test.Accept");

        var duplicateFields = () => new PresentationDialogSurfacePlan<
            SlideSizeDialogSurfaceField,
            SlideSizeDialogAction>(
                "Test",
                "Test dialog",
                "FreeP.Test.Dialog",
                [field, field],
                [action]);
        var duplicateActions = () => new PresentationDialogSurfacePlan<
            SlideSizeDialogSurfaceField,
            SlideSizeDialogAction>(
                "Test",
                "Test dialog",
                "FreeP.Test.Dialog",
                [field],
                [action, action]);

        duplicateFields.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate dialog surface identifier: Width*");
        duplicateActions.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate dialog surface identifier: Accept*");
    }

    [Fact]
    public void FormDialogCatalogs_OwnLabelsActionsAndAccessibilitySemantics()
    {
        AssertSurface(
            SlideShowCustomShowDialogSurfaceCatalog.Surface,
            SlideShowCustomShowDialogAction.Create,
            SlideShowCustomShowDialogAction.Close);
        SlideShowCustomShowDialogSurfaceCatalog.Surface
            .Field(SlideShowCustomShowDialogField.OrderedSlides).Label
            .Should().Be("Custom show order");
        SlideShowCustomShowDialogSurfaceCatalog.Surface
            .Action(SlideShowCustomShowDialogAction.AddSlide).AccessibleName
            .Should().Be("Add slide to custom show");

        AssertSurface(
            HeaderFooterDialogSurfaceCatalog.Surface,
            HeaderFooterDialogAction.Apply,
            HeaderFooterDialogAction.Cancel);
        HeaderFooterDialogSurfaceCatalog.Surface
            .Field(HeaderFooterDialogField.SuppressOnTitleSlide).Label
            .Should().Be("Don't show on title slide");

        AssertSurface(
            SlideSizeDialogSurfaceCatalog.Surface.Schema,
            SlideSizeDialogAction.Accept,
            SlideSizeDialogAction.Cancel);
        SlideSizeDialogSurfaceCatalog.Surface.PresetNames.Should().Equal(
            "Standard (4:3)",
            "Widescreen (16:9)",
            "Custom");
        SlideSizeDialogSurfaceCatalog.Surface.UnitOptions
            .Should().Equal(
                new SlideSizeDialogUnitOption(SlideSizeDialogUnit.Inches, "Inches"),
                new SlideSizeDialogUnitOption(SlideSizeDialogUnit.Centimeters, "Centimeters"));

        AssertSurface(
            SlideShowSettingsDialogSurfaceCatalog.Surface,
            SlideShowSettingsDialogAction.Accept,
            SlideShowSettingsDialogAction.Cancel);
        SlideShowSettingsDialogSurfaceCatalog.Surface
            .Field(SlideShowSettingsDialogField.KioskRestartMilliseconds).AccessibleName
            .Should().Be("Kiosk restart milliseconds");
    }

    [Theory]
    [InlineData(HeaderFooterCommandFocus.DateTime, HeaderFooterDialogField.DateTime)]
    [InlineData(HeaderFooterCommandFocus.Footer, HeaderFooterDialogField.FooterText)]
    [InlineData(HeaderFooterCommandFocus.SlideNumber, HeaderFooterDialogField.SlideNumber)]
    public void HeaderFooterSession_MapsCommandFocusToPortableFieldSemantics(
        HeaderFooterCommandFocus focus,
        HeaderFooterDialogField expectedField)
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));

        var session = new HeaderFooterDialogSession(editor, focus);

        session.RequestedFocusField.Should().Be(expectedField);
        session.Surface.Should().BeSameAs(HeaderFooterDialogSurfaceCatalog.Surface);
    }

    [Fact]
    public void PairedFormRenderers_ConsumeSharedSchemasWithoutOwnedSurfaceTextOrPolicy()
    {
        AssertRendererPair(
            "CustomShowDialog.cs",
            "_session.Surface",
            [
                "\"Custom Shows\"", "\"Custom show order\"", "\"Deck slides\"",
                "\"Create\"", "\"Update Slides\"", "\"Start Show\"",
            ],
            ["SlideShowCustomShowPlanner."]);
        AssertRendererPair(
            "HeaderFooterDialog.cs",
            "_session.Surface",
            [
                "\"Header and Footer\"", "\"Date and time\"", "\"Fixed\"",
                "\"Footer\"", "\"Slide number\"", "\"Don't show on title slide\"",
                "\"Apply to All\"",
            ],
            ["HeaderFooterCommandPlanner.", "switch (RequestedFocus)"]);
        AssertRendererPair(
            "SlideSizeDialog.cs",
            "_session.Surface",
            [
                "\"Slide Size\"", "\"Preset:\"", "\"Unit:\"", "\"Width:\"",
                "\"Height:\"", "\"Inches\"", "\"Centimeters\"", "\"in\"", "\"cm\"",
            ],
            ["SlideSizeDialogPlanner.", "SlideSizeDialogSession.PresetNames"]);
        AssertRendererPair(
            "SlideShowSettingsDialog.cs",
            "_session.Surface",
            [
                "\"Set Up Slide Show\"", "\"Use timings, if present\"",
                "\"Show without animation\"", "\"Play narration\"",
                "\"Show media controls\"", "\"Show master graphics\"",
                "\"Loop until stopped\"", "\"Show scrollbar when browsing\"",
                "\"Kiosk restart milliseconds (optional)\"",
            ],
            ["SlideShowSettingsPlanner."]);
    }

    private static void AssertSurface<TField, TAction>(
        PresentationDialogSurfacePlan<TField, TAction> surface,
        TAction defaultAction,
        TAction cancelAction)
        where TField : notnull
        where TAction : notnull
    {
        surface.Fields.Select(field => field.Id).Should().OnlyHaveUniqueItems();
        surface.Actions.Select(action => action.Id).Should().OnlyHaveUniqueItems();
        surface.Fields.Select(field => field.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Actions.Select(action => action.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Fields.Should().OnlyContain(field =>
            !string.IsNullOrWhiteSpace(field.AccessibleName) &&
            !string.IsNullOrWhiteSpace(field.AutomationId));
        surface.Actions.Should().OnlyContain(action =>
            !string.IsNullOrWhiteSpace(action.AccessibleName) &&
            !string.IsNullOrWhiteSpace(action.AutomationId));
        surface.Action(defaultAction).IsDefault.Should().BeTrue();
        surface.Action(cancelAction).IsCancel.Should().BeTrue();
    }

    private static void AssertRendererPair(
        string fileName,
        string requiredSource,
        IEnumerable<string> forbiddenLiterals,
        IEnumerable<string> forbiddenPolicy)
    {
        foreach (var source in RendererSources(fileName))
        {
            source.Should().Contain(requiredSource);
            source.Should().Contain("AutomationProperties.SetName(");
            source.Should().Contain("AutomationProperties.SetAutomationId(");
            foreach (var literal in forbiddenLiterals)
                source.Should().NotContain(literal);
            foreach (var policy in forbiddenPolicy)
                source.Should().NotContain(policy);
        }
    }

    private static IEnumerable<string> RendererSources(string fileName)
    {
        yield return ReadWorkspaceFile("freep", "FreeP.App.Host", fileName);
        yield return ReadWorkspaceFile("freep", "FreeP.App.Avalonia", fileName);
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
    }
}
