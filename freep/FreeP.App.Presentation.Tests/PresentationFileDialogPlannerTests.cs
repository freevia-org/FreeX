using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationFileDialogPlannerTests
{
    [Fact]
    public void DialogPlans_DefaultToPptxAndKeepLegacyFxpFilters()
    {
        PresentationFileDialogPlanner.LegacyFxpExtension.Should().Be(FxpFormat.Extension);

        var openPlan = PresentationFileDialogPlanner.BuildOpenDialogPlan();
        openPlan.Filter.Should().Be(
            "PowerPoint presentations (*.pptx)|*.pptx|PowerPoint macro-enabled presentations (*.pptm)|*.pptm|PowerPoint templates (*.potx)|*.potx|PowerPoint macro-enabled templates (*.potm)|*.potm|PowerPoint slide shows (*.ppsx)|*.ppsx|PowerPoint macro-enabled slide shows (*.ppsm)|*.ppsm|FreeP legacy presentations (*.fxp)|*.fxp|All files (*.*)|*.*");
        openPlan.DefaultExtensionWithDot.Should().Be(".pptx");

        var savePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan(null);
        savePlan.SuggestedFileName.Should().Be("Presentation.pptx");
        savePlan.DefaultExtensionWithDot.Should().Be(".pptx");
        savePlan.DefaultExtensionWithoutDot.Should().Be("pptx");
        savePlan.FilterIndex.Should().Be(1);
        savePlan.Filter.Should().Be(openPlan.Filter);

        var legacySourcePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan("Legacy.fxp");
        legacySourcePlan.SuggestedFileName.Should().Be("Legacy.pptx");
        legacySourcePlan.FilterIndex.Should().Be(1);

        var macroSourcePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan("MacroDeck.pptm");
        macroSourcePlan.SuggestedFileName.Should().Be("MacroDeck.pptm");
        macroSourcePlan.DefaultExtensionWithDot.Should().Be(".pptm");
        macroSourcePlan.FilterIndex.Should().Be(2);
    }

    [Fact]
    public void PickerPlans_UseTheSamePresentationPolicyForAvaloniaAdapters()
    {
        var openPlan = PresentationFileDialogPlanner.BuildOpenPickerPlan();
        openPlan.FileTypes.Select(fileType => fileType.DisplayName)
            .Should()
            .Equal("All supported presentations", "PowerPoint presentations", "PowerPoint macro-enabled presentations", "PowerPoint templates", "PowerPoint macro-enabled templates", "PowerPoint slide shows", "PowerPoint macro-enabled slide shows", "FreeP legacy presentations");
        openPlan.FileTypes[0].Patterns.Should().Equal("*.pptx", "*.pptm", "*.potx", "*.potm", "*.ppsx", "*.ppsm", "*.fxp");
        openPlan.FileTypes[1].Patterns.Should().Equal("*.pptx");
        openPlan.FileTypes[7].Patterns.Should().Equal("*.fxp");

        var savePlan = PresentationFileDialogPlanner.BuildSavePickerPlan("Legacy.fxp");
        savePlan.SuggestedFileName.Should().Be("Legacy.pptx");
        savePlan.DefaultExtensionWithDot.Should().Be(".pptx");
        savePlan.DefaultExtensionWithoutDot.Should().Be("pptx");
        savePlan.FileTypes.Select(fileType => fileType.DisplayName)
            .Should()
            .Equal("PowerPoint presentations", "PowerPoint macro-enabled presentations", "PowerPoint templates", "PowerPoint macro-enabled templates", "PowerPoint slide shows", "PowerPoint macro-enabled slide shows", "FreeP legacy presentations");
    }

    [Theory]
    [InlineData("deck", "deck.pptx", true)]
    [InlineData("deck.fxp", "deck.fxp", true)]
    [InlineData("deck.PPTX", "deck.PPTX", true)]
    [InlineData("deck.txt", "", false)]
    [InlineData("bad\0deck.pptx", "", false)]
    public void TryResolveSavePickerPath_UsesSupportedExtensionsAndDefault(
        string path,
        string expectedPath,
        bool expectedSuccess)
    {
        var success = PresentationFileDialogPlanner.TryResolveSavePickerPath(path, out var resolvedPath);

        success.Should().Be(expectedSuccess);
        resolvedPath.Should().Be(expectedPath);
    }

    [Theory]
    [InlineData("deck.pptm")]
    [InlineData("deck.potx")]
    [InlineData("deck.potm")]
    [InlineData("deck.ppsx")]
    [InlineData("deck.ppsm")]
    public void TryResolveSavePickerPath_AcceptsOfficePackageFamilies(string path)
    {
        PresentationFileDialogPlanner.TryResolveSavePickerPath(path, out var resolvedPath).Should().BeTrue();
        resolvedPath.Should().Be(path);
    }

    [Theory]
    [InlineData("deck.fxp", true)]
    [InlineData("deck.FXP", true)]
    [InlineData("deck.pptx", false)]
    public void IsLegacyPresentationPath_MatchesLegacyFxpExtensionCaseInsensitively(
        string path,
        bool expected) =>
        PresentationFileDialogPlanner.IsLegacyPresentationPath(path).Should().Be(expected);

    [Fact]
    public void PdfExportPlan_UsesSourceNameBaseAndPdfExtension()
    {
        var plan = PresentationFileDialogPlanner.BuildPdfExportDialogPlan("Quarterly Review.pptx");

        plan.Filter.Should().Be("PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*");
        plan.SuggestedFileName.Should().Be("Quarterly Review.pdf");
        plan.DefaultExtensionWithDot.Should().Be(".pdf");
        plan.DefaultExtensionWithoutDot.Should().Be("pdf");
        plan.FilterIndex.Should().Be(1);

        var pickerPlan = PresentationFileDialogPlanner.BuildPdfExportPickerPlan("Quarterly Review.pptx");
        pickerPlan.SuggestedFileName.Should().Be("Quarterly Review.pdf");
        pickerPlan.DefaultExtensionWithDot.Should().Be(".pdf");
        pickerPlan.DefaultExtensionWithoutDot.Should().Be("pdf");
        pickerPlan.FileTypes.Select(fileType => fileType.DisplayName).Should().Equal("PDF documents");

        var notesPlan = PresentationExportPlanner.BuildNotesPagePdfExportDialogPlan("Quarterly Review.pptx");
        notesPlan.SuggestedFileName.Should().Be("Quarterly Review-notes.pdf");
        notesPlan.DefaultExtensionWithDot.Should().Be(".pdf");
        notesPlan.DefaultExtensionWithoutDot.Should().Be("pdf");

        var notesPickerPlan = PresentationExportPlanner.BuildNotesPagePdfExportPickerPlan("Quarterly Review.pptx");
        notesPickerPlan.SuggestedFileName.Should().Be("Quarterly Review-notes.pdf");
        notesPickerPlan.FileTypes.Select(fileType => fileType.DisplayName).Should().Equal("PDF documents");
    }

    [Fact]
    public void VideoExportDialogPlan_UsesMp4DefaultAndSourceName()
    {
        var plan = PresentationExportPlanner.BuildVideoExportDialogPlan("Quarterly Review.pptx");

        plan.SuggestedFileName.Should().Be("Quarterly Review.mp4");
        plan.DefaultExtensionWithDot.Should().Be(".mp4");
        plan.DefaultExtensionWithoutDot.Should().Be("mp4");
        plan.Filter.Should().Contain("MPEG-4 videos (*.mp4)|*.mp4");
    }

    [Fact]
    public void ExportPlanner_DefinesSharedBackstageAndCommandDescriptors()
    {
        var formats = PresentationExportPlanner.BuildFormatDescriptors();

        formats.Should().ContainSingle(format =>
            format.Format == PresentationExportFormat.Pdf &&
            format.CommandId == PresentationExportPlanner.PdfExportCommandId &&
            format.DefaultExtensionWithDot == ".pdf" &&
            format.IsImplemented);
        formats.Should().ContainSingle(format =>
            format.Format == PresentationExportFormat.NotesPagePdf &&
            format.CommandId == PresentationExportPlanner.NotesPagePdfExportCommandId &&
            format.DefaultExtensionWithDot == ".pdf" &&
            format.IsImplemented);
        formats.Should().Contain(format =>
            format.Format == PresentationExportFormat.ImageSequence &&
            format.DefaultExtensionWithDot == ".png" &&
            format.IsImplemented);
        formats.Should().Contain(format =>
            format.Format == PresentationExportFormat.Video &&
            format.DefaultExtensionWithDot == ".mp4" &&
            !format.IsImplemented);
        formats.Should().Contain(format =>
            format.Format == PresentationExportFormat.Print &&
            format.IsImplemented);

        var backstage = PresentationExportPlanner.BuildBackstageExportPlan();
        backstage.DeferredGroupHeading.Should().Be("Other File Types");
        backstage.FixedLayoutActions.Should().ContainSingle(action =>
            action.Format == PresentationExportFormat.Pdf &&
            action.CommandId == PresentationExportPlanner.PdfExportCommandId &&
            action.IsEnabled);
        backstage.FixedLayoutActions.Should().ContainSingle(action =>
            action.Format == PresentationExportFormat.NotesPagePdf &&
            action.CommandId == PresentationExportPlanner.NotesPagePdfExportCommandId &&
            action.IsEnabled);
        backstage.DeferredActions.Should().ContainSingle(action =>
            action.Format == PresentationExportFormat.ImageSequence &&
            action.CommandId == PresentationExportPlanner.ImageExportCommandId &&
            action.IsEnabled);
        backstage.DeferredActions.Select(action => action.Format)
            .Should()
            .Equal(
                PresentationExportFormat.ImageSequence,
                PresentationExportFormat.Video);
    }

    [Fact]
    public void FileTextResources_ExposeAvaloniaPicturePickerTypeName()
    {
        var presentationText = PresentationFileTextResources.Presentation;

        PresentationFileTextResources.PictureFileTypeName.Should().Be("Images");
        PresentationFileTextResources.VideoExportFailed.Should().Be("Video export failed.");
        PresentationFileTextResources.PrintJobFallbackName.Should().Be("FreeP presentation");
        PresentationFileTextResources.NormalizePrintJobName("  Quarterly review  ")
            .Should().Be("Quarterly review");
        PresentationFileTextResources.NormalizePrintJobName("  ")
            .Should().Be("FreeP presentation");
        presentationText.OpenPickerTitle.Should().Be("Open Presentation");
        presentationText.SavePickerTitle.Should().Be("Save Presentation");
        presentationText.FallbackDisplayName.Should().Be("Presentation");
        presentationText.NewAction.Should().Be("creating a new presentation");
        presentationText.OpenAction.Should().Be("opening another presentation");
        presentationText.OpenCommand.Should().Be("Open");
        presentationText.SaveCommand.Should().Be("Save");
        presentationText.InsertPictureCommand.Should().Be("Insert picture");
        presentationText.InsertPicturePickerTitle.Should().Be("Insert Picture");
        SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(presentationText, presentationText.OpenCommand)
            .Should().Be("Open failed: selected file is not available as a local path.");
        SisterAppFileTextPlanner.FormatSaved(presentationText, "Deck.pptx").Should().Be("Saved Deck.pptx");
    }
}
