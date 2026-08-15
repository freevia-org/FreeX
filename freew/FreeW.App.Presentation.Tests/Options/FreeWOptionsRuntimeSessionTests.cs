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

    [Fact]
    public void ApplyAndPersistReloadsFreshFromDiskAndPreservesFieldsThisSessionNeverTouched()
    {
        // Simulates another FreeW window/process that persisted a RecentFilesCap change to disk while
        // this session was still holding its own stale open-time snapshot (RecentFilesCap = 15).
        var diskState = new FreeWOptions
        {
            RecentFilesCap = 20,
            DefaultSaveFormat = FreeWOptions.DocxDefaultFormat,
            UiLanguage = "",
            AutoCorrectEnabled = true,
            AutoFormat = AutoFormatOptions.Default,
            AutoCorrect = AutoCorrectOptions.Default,
        };
        var live = new FreeWOptions
        {
            RecentFilesCap = 15,
            DefaultSaveFormat = FreeWOptions.DocxDefaultFormat,
            UiLanguage = "",
            AutoCorrectEnabled = true,
            AutoFormat = AutoFormatOptions.Default,
            AutoCorrect = AutoCorrectOptions.Default,
        };
        var session = new FreeWOptionsRuntimeSession(live);

        // This dialog session only changes UiLanguage; RecentFilesCap in the edited result is whatever
        // this stale session opened with (15), not the other window's fresher 20.
        var edited = new FreeWOptions
        {
            RecentFilesCap = 15,
            DefaultSaveFormat = FreeWOptions.DocxDefaultFormat,
            UiLanguage = "fr-FR",
            AutoCorrectEnabled = true,
            AutoFormat = AutoFormatOptions.Default,
            AutoCorrect = AutoCorrectOptions.Default,
        };

        FreeWOptions? persistedOptions = null;
        var outcome = session.ApplyAndPersist(
            edited,
            options =>
            {
                persistedOptions = options;
                return true;
            },
            () => diskState);

        outcome.Persisted.Should().BeTrue();
        persistedOptions.Should().NotBeNull();
        persistedOptions!.RecentFilesCap.Should().Be(
            20,
            "the other window's fresher on-disk value for a field this session never touched must not be reverted");
        persistedOptions.UiLanguage.Should().Be("fr-FR", "this session's own edit must still be applied");
        session.LiveOptions.RecentFilesCap.Should().Be(
            20,
            "the live in-memory snapshot should pick up the merged/persisted value, not stay stuck on the stale open-time cap");
    }

    [Fact]
    public void ApplyAndPersistAppliesThisSessionsOwnEditEvenWhenDiskDivergedOnTheSameField()
    {
        // Regression guard for the opposite failure mode: a merge that always preferred the freshest
        // on-disk value would silently drop the user's own in-dialog edit whenever another window had
        // touched the same field.
        var diskState = new FreeWOptions
        {
            RecentFilesCap = 20,
            DefaultSaveFormat = FreeWOptions.DocxDefaultFormat,
            UiLanguage = "",
            AutoCorrectEnabled = true,
            AutoFormat = AutoFormatOptions.Default,
            AutoCorrect = AutoCorrectOptions.Default,
        };
        var live = new FreeWOptions
        {
            RecentFilesCap = 15,
            DefaultSaveFormat = FreeWOptions.DocxDefaultFormat,
            UiLanguage = "",
            AutoCorrectEnabled = true,
            AutoFormat = AutoFormatOptions.Default,
            AutoCorrect = AutoCorrectOptions.Default,
        };
        var session = new FreeWOptionsRuntimeSession(live);
        var edited = new FreeWOptions
        {
            RecentFilesCap = 8,
            DefaultSaveFormat = FreeWOptions.DocxDefaultFormat,
            UiLanguage = "",
            AutoCorrectEnabled = true,
            AutoFormat = AutoFormatOptions.Default,
            AutoCorrect = AutoCorrectOptions.Default,
        };

        FreeWOptions? persistedOptions = null;
        session.ApplyAndPersist(
            edited,
            options =>
            {
                persistedOptions = options;
                return true;
            },
            () => diskState);

        persistedOptions!.RecentFilesCap.Should().Be(8, "this session's explicit edit must win over a stale-conflicting disk value");
    }
}
