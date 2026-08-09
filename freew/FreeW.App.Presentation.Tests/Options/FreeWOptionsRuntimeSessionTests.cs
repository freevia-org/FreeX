using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Options;

public sealed class FreeWOptionsRuntimeSessionTests
{
    [Fact]
    public void ConstructorNormalizesLiveOptionsAndProjectsEditorTypingState()
    {
        var live = new FreeWOptions
        {
            RecentFilesCap = 999,
            DefaultSaveFormat = " ",
            AutoCorrectEnabled = false,
            AutoFormat = AutoFormatOptions.Default with { SmartQuotes = false },
            AutoCorrect = new AutoCorrectOptions { ReplaceText = false },
        };

        var session = new FreeWOptionsRuntimeSession(live);

        session.LiveOptions.Should().BeSameAs(live);
        live.RecentFilesCap.Should().Be(FreeWOptions.MaxRecentFilesCap);
        live.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        session.EditorTypingOptions.Should().Be(new FreeWEditorTypingOptionsPlan(
            AutoCorrectEnabled: false,
            AutoFormat: live.AutoFormat,
            AutoCorrect: live.AutoCorrect));
    }

    [Fact]
    public void ApplyMutatesTheExistingLiveInstanceAndReturnsTheNewEditorProjection()
    {
        var live = new FreeWOptions();
        var session = new FreeWOptionsRuntimeSession(live);
        var edited = new FreeWOptions
        {
            RecentFilesCap = 4,
            DefaultSaveFormat = ".docx",
            UiLanguage = "  uk-UA  ",
            AutoCorrectEnabled = false,
            AutoFormat = AutoFormatOptions.Default with { Hyperlinks = false },
            AutoCorrect = new AutoCorrectOptions { ReplaceText = false },
        };

        var plan = session.Apply(edited);

        session.LiveOptions.Should().BeSameAs(live);
        live.Should().NotBeSameAs(edited);
        live.RecentFilesCap.Should().Be(4);
        live.UiLanguage.Should().Be("uk-UA");
        plan.Should().Be(new FreeWEditorTypingOptionsPlan(
            AutoCorrectEnabled: false,
            AutoFormat: edited.AutoFormat,
            AutoCorrect: edited.AutoCorrect));
    }
}
