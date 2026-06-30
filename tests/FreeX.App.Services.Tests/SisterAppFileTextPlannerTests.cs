using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class SisterAppFileTextPlannerTests
{
    [Fact]
    public void StatusFormatters_UseAppProvidedTemplates()
    {
        var text = BuildTextSpec();

        SisterAppFileTextPlanner.FormatCommandUnavailable(text, text.OpenCommand)
            .Should().Be("CMD Open blocked");
        SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(text, text.SaveCommand)
            .Should().Be("PATH Save missing");
        SisterAppFileTextPlanner.FormatUnsupportedFileType(text, text.OpenCommand, ".zip")
            .Should().Be("TYPE Open rejected .zip");
        SisterAppFileTextPlanner.FormatUnsupportedExtension(text, ".docm")
            .Should().Be("EXT .docm rejected");
        SisterAppFileTextPlanner.FormatCommandFailed(text, text.InsertPictureCommand, "No decoder")
            .Should().Be("FAIL Picture insert No decoder");
        SisterAppFileTextPlanner.FormatOpened(text, "Deck.pptx").Should().Be("OPENED Deck.pptx");
        SisterAppFileTextPlanner.FormatSaved(text, "Draft.docx").Should().Be("SAVED Draft.docx");
        SisterAppFileTextPlanner.FormatInserted(text, "Photo.png").Should().Be("INSERTED Photo.png");
        SisterAppFileTextPlanner.FormatSaveAsTitle(text, "Word document").Should().Be("SAVE_AS Word document");
    }

    [Fact]
    public void SharedPlannerSource_DoesNotOwnSisterAppFileText()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "SisterAppFileTextPlanner.cs"));

        source.Should().NotContain("Open document");
        source.Should().NotContain("Save document");
        source.Should().NotContain("Open Presentation");
        source.Should().NotContain("Save Presentation");
        source.Should().NotContain("unsupported file type");
        source.Should().NotContain("selected file is not available as a local path");
        source.Should().NotContain("Insert Picture");
        source.Should().NotContain("PDF export");
    }

    private static SisterAppFileTextSpec BuildTextSpec() =>
        new(
            OpenPickerTitle: "OPEN_PICKER",
            SavePickerTitle: "SAVE_PICKER",
            FallbackDisplayName: "FALLBACK",
            NewAction: "NEW_ACTION",
            OpenAction: "OPEN_ACTION",
            OpenCommand: "Open",
            SaveCommand: "Save",
            InsertPictureCommand: "Picture insert",
            InsertPicturePickerTitle: "PICTURE_PICKER",
            Status: new SisterAppFileStatusTextSpec(
                CommandUnavailableFormat: "CMD {0} blocked",
                SelectedFileNotLocalPathFormat: "PATH {0} missing",
                UnsupportedFileTypeFormat: "TYPE {0} rejected {1}",
                UnsupportedExtensionFormat: "EXT {0} rejected",
                CommandFailedFormat: "FAIL {0} {1}",
                OpenedFormat: "OPENED {0}",
                SavedFormat: "SAVED {0}",
                InsertedFormat: "INSERTED {0}",
                SaveAsTitleFormat: "SAVE_AS {0}"));
}
