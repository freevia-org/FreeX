namespace Free.Shared.Theme.Tests;

public sealed class ThemeResourceLookupTests
{
    [Fact]
    public void ProductProfiles_MapCanonicalRendererKeysAndProductBadgeRoles()
    {
        ProductThemeResourceProfiles.FreeX.TitleBarBrush.PrimaryKey.Should().Be("FreeXTitleBarBrush");
        ProductThemeResourceProfiles.FreeW.StatusSurfaceBrush.PrimaryKey.Should().Be("FreeWStatusSurfaceBrush");
        ProductThemeResourceProfiles.FreeP.StatusBarHeight.PrimaryKey.Should().Be("FreePStatusBarHeight");

        ProductThemeResourceProfiles.FreeW.BadgeBrush.PrimaryKey.Should().Be("FreeWAccentBrush");
        ProductThemeResourceProfiles.FreeP.BadgeBrush.PrimaryKey.Should().Be("FreePAccentDarkBrush");
    }

    [Fact]
    public void TryResolve_UsesTheFirstOrderedKeyWithTheRequestedType()
    {
        var descriptor = new ThemeResourceDescriptor("ProductBrush", "SharedBrush", "LegacyBrush");
        var resources = new Dictionary<string, object?>
        {
            ["ProductBrush"] = 42,
            ["SharedBrush"] = "shared-value",
            ["LegacyBrush"] = "legacy-value",
        };

        var resolved = ThemeResourceLookup.TryResolve(
            descriptor,
            key => resources.GetValueOrDefault(key),
            out string value);

        resolved.Should().BeTrue();
        value.Should().Be("shared-value");
    }

    [Fact]
    public void ResolveOr_PreservesTheRendererFallbackWhenNoTypedResourceExists()
    {
        var descriptor = new ThemeResourceDescriptor("Missing", "WrongType");

        var value = ThemeResourceLookup.ResolveOr(
            descriptor,
            key => key == "WrongType" ? 12 : null,
            "renderer-fallback");

        value.Should().Be("renderer-fallback");
    }

    [Fact]
    public void ResolveProjectedOr_ProjectsOnlyAResolvedNativeResource()
    {
        var descriptor = new ThemeResourceDescriptor("Brush");

        var resolved = ThemeResourceLookup.ResolveProjectedOr<FakeNativeBrush, string>(
            descriptor,
            _ => new FakeNativeBrush("#17324D"),
            brush => brush.Color,
            "#000000");
        var fallback = ThemeResourceLookup.ResolveProjectedOr<FakeNativeBrush, string>(
            descriptor,
            _ => null,
            brush => brush.Color,
            "#000000");

        resolved.Should().Be("#17324D");
        fallback.Should().Be("#000000");
    }

    [Fact]
    public void ThemeResourcePlan_UsesTheSameCanonicalKeyProfileAsConsumers()
    {
        var profile = ProductThemeResourceProfiles.FreeP;
        var plan = ThemeResourcePlan.Create(BrandThemes.FreeP, profile.KeyPrefix);

        plan.Colors.Single(resource => resource.Role == "TitleBar").BrushKey
            .Should().Be(profile.TitleBarBrush.PrimaryKey);
        plan.Colors.Single(resource => resource.Role == "StatusSurface").BrushKey
            .Should().Be(profile.StatusSurfaceBrush.PrimaryKey);
        plan.Metrics.Single(resource => resource.Role == "StatusBarHeight").Key
            .Should().Be(profile.StatusBarHeight.PrimaryKey);
        plan.Typography.Single(resource => resource.Role == "StatusBarText").FontSizeKey
            .Should().Be(profile.StatusBarTextFontSize.PrimaryKey);
    }

    private sealed record FakeNativeBrush(string Color);
}
