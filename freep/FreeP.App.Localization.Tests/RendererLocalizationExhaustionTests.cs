using System.Globalization;
using FluentAssertions;
using Xunit;

namespace FreeP.App.Localization.Tests;

public sealed class RendererLocalizationExhaustionTests
{
    private static readonly string[] RequiredKeys =
    [
        "File_Error_PlatformPickerUnavailableFormat",
        "File_Error_InvalidAvaloniaAssetSelection",
        "Print_Status_BackstageNotRun",
        "Print_Status_NativeDetectionPending",
        "Print_Status_WindowsQueueUnavailableFormat",
        "Print_Host_Wpf",
        "Print_Host_AvaloniaWindows",
        "Print_Host_AvaloniaLinux",
        "Print_Host_UnavailableFormat",
        "Print_Host_WpfWindowsOnly",
        "Renderer_InlineOleObjectFallback",
    ];

    [Fact]
    public void ExhaustionTail_HasDistinctNeutralAndFrenchText()
    {
        Loc.GetNeutralResourceKeys().Should().Contain(RequiredKeys);

        foreach (var key in RequiredKeys)
        {
            var neutral = Loc.GetNeutral(key);
            var french = WithUiCulture("fr-FR", () => Loc.Get(key));

            neutral.Should().NotStartWith("[[");
            french.Should().NotStartWith("[[");
            french.Should().NotBe(neutral, $"{key} has a product-owned French translation");
        }
    }

    [Fact]
    public void ExhaustionTail_FormattingPreservesRuntimeValues()
    {
        WithUiCulture(
                "fr-FR",
                () => Loc.Format("File_Error_PlatformPickerUnavailableFormat", "Ins\u00e9rer une image"))
            .Should().Contain("Ins\u00e9rer une image");
        WithUiCulture(
                "fr-FR",
                () => Loc.Format("Print_Status_WindowsQueueUnavailableFormat", "Office Printer"))
            .Should().Contain("Office Printer");
        WithUiCulture(
                "fr-FR",
                () => Loc.Format("Print_Host_UnavailableFormat", "Avalonia Linux"))
            .Should().Contain("Avalonia Linux");
        WithUiCulture("fr-FR", () => Loc.Get("Renderer_InlineOleObjectFallback"))
            .Should().Be("Objet OLE");
    }

    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
