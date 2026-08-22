using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TranslateDialogSourceTests
{
    [Fact]
    public void TranslateDialog_UsesPortableLanguagePlannerAndExposesAccessibleManualEntryControls()
    {
        var source = DialogSourceTestSupport.ReadHostSources("TranslateDialog.cs");

        source.Should().Contain("public sealed class TranslateDialog : DialogWindow");
        source.Should().Contain("TranslateDialogPlanner.Languages");
        source.Should().Contain("TranslateDialogPlanner.SuggestTargetReference(source)");
        source.Should().Contain("WfTranslateFromLanguage");
        source.Should().Contain("WfTranslateToLanguage");
        source.Should().Contain("WfTranslateTranslationBox");
        source.Should().Contain("WfTranslateTargetBox");
        source.Should().Contain("WfTranslateInsertButton");
        source.Should().Contain("WfTranslateCloseButton");
        source.Should().NotContain("No offline translation engine is available");
    }
}
