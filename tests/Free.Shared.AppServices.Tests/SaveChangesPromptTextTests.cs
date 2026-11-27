using System.Globalization;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// The shared dirty-gate prompt (the sentence plus the Save / Don't save / Cancel buttons) resolves
/// through the app's resource catalog instead of carrying hard-coded English in the shared tier.
/// </summary>
public sealed class SaveChangesPromptTextTests
{
    private sealed class RecordingMessageService : IUserMessageService
    {
        public string? LastMessage { get; private set; }

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon)
        {
            LastMessage = message;
            return UserMessageResult.No;
        }
    }

    [Fact]
    public void Resolve_WithoutCatalog_ReturnsTheEnglishDefaults()
    {
        var text = SaveChangesPromptTextResources.Resolve();

        text.FormatMessage("Draft", "closing").Should().Be("Do you want to save changes to Draft before closing?");
        text.FormatQuotedMessage("Draft", "closing").Should().Be("Do you want to save changes to \"Draft\" before closing?");
        text.SaveButton.Should().Be("Save");
        text.DontSaveButton.Should().Be("Don't save");
        text.CancelButton.Should().Be("Cancel");
    }

    [Fact]
    public void Resolve_ReadsEveryStringFromTheCatalog()
    {
        var text = SaveChangesPromptTextResources.Resolve(key => "t:" + key);

        text.MessageFormat.Should().Be("t:SaveChanges_Prompt_Message_Format");
        text.QuotedMessageFormat.Should().Be("t:SaveChanges_Prompt_Message_Quoted_Format");
        text.SaveButton.Should().Be("t:SaveChanges_Prompt_Save_Button");
        text.DontSaveButton.Should().Be("t:SaveChanges_Prompt_DontSave_Button");
        text.CancelButton.Should().Be("t:SaveChanges_Prompt_Cancel_Button");
        SaveChangesPromptTextResources.RequiredResourceKeys.Should().HaveCount(5).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Resolve_MissingCatalogEntryFallsBackToEnglish()
    {
        var text = SaveChangesPromptTextResources.Resolve(key => key);

        text.SaveButton.Should().Be("Save");
        text.MessageFormat.Should().Be(SaveChangesPromptTextResources.DefaultMessageFormat);
    }

    [Fact]
    public void Format_AppliesATranslatedTemplateAndSurvivesAMalformedOne()
    {
        var translated = SaveChangesPromptTextResources.Resolve(key =>
            key == "SaveChanges_Prompt_Message_Format" ? "{1} : enregistrer {0} ?" : null);
        translated.FormatMessage("Doc", "fermeture").Should().Be("fermeture : enregistrer Doc ?");

        var malformed = SaveChangesPromptTextResources.Resolve(key =>
            key == "SaveChanges_Prompt_Message_Format" ? "broken {0 {1}" : null);
        malformed.FormatMessage("Doc", "closing").Should().Be("Do you want to save changes to Doc before closing?");
    }

    [Fact]
    public void PromptSaveChanges_ShowsTheResolvedMessage()
    {
        var service = new RecordingMessageService();
        var text = SaveChangesPromptTextResources.Resolve(key =>
            key == "SaveChanges_Prompt_Message_Format" ? "T[{0}|{1}]" : null);

        var answer = service.PromptSaveChanges("Draft", "closing", "App", text);

        service.LastMessage.Should().Be("T[Draft|closing]");
        answer.Should().Be(SaveChangesPrompt.DontSave);

        service.PromptSaveChanges("Draft", "closing", "App");
        service.LastMessage.Should().Be("Do you want to save changes to Draft before closing?");
    }

    [Fact]
    public void SharedShellSources_DoNotHardCodeTheDirtyGateText()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        string[] files =
        [
            Path.Combine("shared", "Free.Shared.AppServices", "IUserMessageService.cs"),
            Path.Combine("shared", "Free.Shared.Shell.Avalonia", "AvaloniaSaveChangesDialog.cs"),
            Path.Combine("shared", "Free.Shared.Shell.Avalonia", "SisterAvaloniaFileCommandWorkflow.cs"),
            Path.Combine("shared", "Free.Shared.Shell.Wpf", "SisterWpfFileCommandWorkflow.cs"),
        ];

        foreach (var file in files)
        {
            var source = File.ReadAllText(Path.Combine(root, file));
            source.Should().NotContain("Do you want to save changes to", file);
            source.Should().NotContain("\"Don't save\"", file);
            source.Should().NotContain("\"Save\",", file);
            source.Should().NotContain("\"Cancel\"", file);
        }

        File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell.Avalonia", "AvaloniaSaveChangesDialog.cs"))
            .Should().Contain("SaveChangesPromptTextResources.Resolve(getText)");
        File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell.Wpf", "SisterWpfFileCommandWorkflow.cs"))
            .Should().Contain("SaveChangesPromptTextResources.Resolve(_getText)");
        File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell.Avalonia", "SisterAvaloniaFileCommandWorkflow.cs"))
            .Should().Contain("_getText");
    }

    [Fact]
    public void CultureDoesNotChangeEnglishDefaults()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            SaveChangesPromptTextResources.Resolve().FormatMessage("A", "b")
                .Should().Be("Do you want to save changes to A before b?");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
