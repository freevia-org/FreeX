using Free.Shared.Theme;

namespace FreeP.App.Presentation.Tests;

public sealed class FreePVisualBrushCatalogTests
{
    [Fact]
    public void Catalog_ProjectsThemeAndPresentationRolesThroughAdapter()
    {
        TestBrushCatalog.Accent.Should().StartWith("theme:FreePAccentBrush:");
        TestBrushCatalog.PaneHeadingText.Should().Be("color:#333333");
        TestBrushCatalog.PresenterMutedText.Should().Be("color:#AAB2C2");
    }

    private sealed class TestBrushCatalog : FreePVisualBrushCatalog<string, TestBrushAdapter>
    {
    }

    private readonly struct TestBrushAdapter : IFreePVisualBrushAdapter<string>
    {
        public static string ResolveTheme(ThemeResourceDescriptor resource, ThemeColor fallback) =>
            $"theme:{resource.PrimaryKey}:{fallback.ToHex()}";

        public static string Create(ThemeColor color) => $"color:{color.ToHex()}";
    }
}
