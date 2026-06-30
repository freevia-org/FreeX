using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class SisterAppFileTextPlannerTests
{
    [Fact]
    public void DocumentAndPresentationSpecs_PreserveSisterAppPickerText()
    {
        SisterAppFileTextPlanner.Document.OpenPickerTitle.Should().Be("Open document");
        SisterAppFileTextPlanner.Document.SavePickerTitle.Should().Be("Save document");
        SisterAppFileTextPlanner.Document.FallbackDisplayName.Should().Be("Document");
        SisterAppFileTextPlanner.Document.OpenAction.Should().Be("opening another document");

        SisterAppFileTextPlanner.Presentation.OpenPickerTitle.Should().Be("Open Presentation");
        SisterAppFileTextPlanner.Presentation.SavePickerTitle.Should().Be("Save Presentation");
        SisterAppFileTextPlanner.Presentation.FallbackDisplayName.Should().Be("Presentation");
        SisterAppFileTextPlanner.Presentation.NewAction.Should().Be("creating a new presentation");
    }

    [Fact]
    public void StatusFormatters_MatchAvaloniaSisterAppMessages()
    {
        SisterAppFileTextPlanner.FormatCommandUnavailable("Open").Should().Be("Open unavailable.");
        SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath("Save")
            .Should().Be("Save failed: selected file is not available as a local path.");
        SisterAppFileTextPlanner.FormatUnsupportedFileType("Open", ".zip")
            .Should().Be("Open failed: unsupported file type \".zip\".");
        SisterAppFileTextPlanner.FormatUnsupportedExtension(".docm")
            .Should().Be("Save failed: unsupported extension \".docm\".");
        SisterAppFileTextPlanner.FormatCommandFailed("Insert picture", "No decoder")
            .Should().Be("Insert picture failed: No decoder");
        SisterAppFileTextPlanner.FormatOpened("Deck.pptx").Should().Be("Opened Deck.pptx");
        SisterAppFileTextPlanner.FormatSaved("Draft.docx").Should().Be("Saved Draft.docx");
        SisterAppFileTextPlanner.FormatInserted("Photo.png").Should().Be("Inserted Photo.png");
        SisterAppFileTextPlanner.FormatSaveAsTitle("Word document").Should().Be("Save as Word document");
    }
}
