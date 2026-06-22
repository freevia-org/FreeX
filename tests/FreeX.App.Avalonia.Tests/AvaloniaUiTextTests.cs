using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaUiTextTests
{
    [Fact]
    public void UiText_DelegatesSharedLocalizationHelpers()
    {
        UiText.Get("Common_Cancel").Should().Be(FreeX.App.Localization.Loc.Get("Common_Cancel"));
        UiText.GetNeutral("Common_Cancel").Should().Be("Cancel");
        UiText.Format("PivotOptions_Title", "Sales")
            .Should()
            .Be(FreeX.App.Localization.Loc.Format("PivotOptions_Title", "Sales"));
        UiText.GetNeutralResourceKeys().Should().Contain("Common_Cancel");
        UiText.CreateAutomationName("_Cancel").Should().Be("Cancel");
        UiText.CreateMissingText("Missing_Key").Should().Be("[[Missing_Key]]");
    }
}
