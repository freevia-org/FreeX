namespace FreeW.Core.Model.Tests;

public sealed class GutterAtTopModelTests
{
    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    [Fact]
    public void SettingDefaultsOffAndSurvivesPageSnapshots()
    {
        var page = new PageSettings { GutterPt = 36 };

        page.GutterAtTop.Should().BeFalse();

        page.GutterAtTop = true;
        var clone = page.Clone();

        clone.GutterAtTop.Should().BeTrue();
        clone.GutterPt.Should().Be(36);
    }

    [Fact]
    public void SetPageSettingsCommand_AppliesAndRestoresSetting()
    {
        var document = TextDocument.CreateEmpty();
        var changed = document.Page.Clone();
        changed.GutterAtTop = true;
        var command = new SetPageSettingsCommand(changed);
        var context = new CommandContext(document);

        command.Apply(context);

        document.Page.GutterAtTop.Should().BeTrue();

        command.Revert(context);

        document.Page.GutterAtTop.Should().BeFalse();
    }
}
