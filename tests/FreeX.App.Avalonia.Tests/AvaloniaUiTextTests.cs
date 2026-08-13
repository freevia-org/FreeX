using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaUiTextTests
{
    [Fact]
    public void UiText_DelegatesSharedLocalizationHelpers()
    {
        UiText.Get("Common_Cancel").Should().Be(FreeX.App.Localization.Loc.Get("Common_Cancel"));
        UiText.GetNeutral("Common_Cancel").Should().Be("_Cancel"); // canonical value includes WPF access-key prefix; Avalonia strips it at render time
        UiText.Format("PivotOptions_Title", "Sales")
            .Should()
            .Be(FreeX.App.Localization.Loc.Format("PivotOptions_Title", "Sales"));
        UiText.GetNeutralResourceKeys().Should().Contain("Common_Cancel");
        UiText.GetNeutralResourceKeys().Should().Contain("Progress_OpeningWorkbook");
        UiText.Get("Progress_LoadingFileReadingWorksheets").Should().Be("Loading file (reading worksheets)");
        UiText.Get("Progress_ExportingFileRendering").Should().Be("Exporting file (rendering pages)");
        UiText.GetNeutralResourceKeys().Should().Contain("StatusBar_AverageFormat");
        UiText.Get("MainWindow_Text_Ready").Should().Be("Ready");
        UiText.Get("StatusBar_EditMode").Should().Be("Edit");
        UiText.Get("StatusBar_CustomizeStatusBar").Should().Be("Customize Status Bar");
        UiText.CreateAutomationName("_Cancel").Should().Be("Cancel");
        UiText.CreateMissingText("Missing_Key").Should().Be("[[Missing_Key]]");
    }

    [Fact]
    public void UiText_ContainsEverySharedStatusAndProgressResourceKey()
    {
        var requiredKeys = WorkbookProgressPresentationPlanner.RequiredResourceKeys
            .Concat(StatusBarTextResourceKeys.RequiredKeys)
            .Concat(StatusBarCustomizeResourceKeys.RequiredKeys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        requiredKeys.Should().NotBeEmpty();
        foreach (var key in requiredKeys)
        {
            var avaloniaResourceKey = key == StatusBarCustomizeResourceKeys.Zoom
                ? "MainWindow_Text_Zoom"
                : key;
            UiText.GetNeutralResourceKeys().Should().Contain(avaloniaResourceKey);
            UiText.Get(avaloniaResourceKey).Should().NotBe(UiText.CreateMissingText(avaloniaResourceKey));
        }
    }
}
