using FluentAssertions;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class FreeXRibbonKeyTipRoutePlannerTests
{
    [Fact]
    public void Build_PreservesTabsCommandsNestedMenusBackstageAndQatRoutes()
    {
        var catalog = FreeXRibbonKeyTipRoutePlanner.Build(BuildDefinition());

        catalog.Routes.Select(route => route.Input)
            .Should().BeInAscendingOrder(StringComparer.Ordinal);

        catalog.TryResolveExact("F", out var backstage).Should().BeTrue();
        backstage.Kind.Should().Be(FreeXRibbonKeyTipRouteKind.Backstage);

        catalog.TryResolveExact("FH", out var backstageHome).Should().BeTrue();
        backstageHome.BackstagePane.Should().Be(FreeXBackstagePaneId.Home);

        catalog.TryResolveExact("1", out var firstQat).Should().BeTrue();
        firstQat.Kind.Should().Be(FreeXRibbonKeyTipRouteKind.QuickAccessToolbar);
        firstQat.QuickAccessIndex.Should().Be(0);
        catalog.TryResolveExact("3", out var thirdQat).Should().BeTrue();
        thirdQat.QuickAccessIndex.Should().Be(2);
        catalog.TryResolveExact("4", out _).Should().BeFalse();

        catalog.TryResolveExact("H", out var home).Should().BeTrue();
        home.RouteName.Should().Be("tab:HomeTab");
        catalog.TryResolveExact("HX", out var command).Should().BeTrue();
        command.CommandId.Should().Be(new RibbonCommandId("Direct Command"));
        catalog.TryResolveExact("HBS", out var menuScope).Should().BeTrue();
        menuScope.Kind.Should().Be(FreeXRibbonKeyTipRouteKind.Scope);
        catalog.TryResolveExact("HBSD", out var menuCommand).Should().BeTrue();
        menuCommand.CommandId.Should().Be(new RibbonCommandId("Nested Command"));
    }

    [Fact]
    public void Build_UsesAuthoredContextualKeyTipAndPreservesNativeOnlyScopes()
    {
        var catalog = FreeXRibbonKeyTipRoutePlanner.Build(BuildDefinition());

        catalog.TryResolveExact("QZ", out var contextual).Should().BeTrue();
        contextual.RouteName.Should().Be("tab:ContextTab");
        contextual.TabKeyTip.Should().Be("QZ");

        catalog.TryResolveExact("NCH", out var chartScope).Should().BeTrue();
        chartScope.RouteName.Should().Be("group:InsertChartsGroup");
        catalog.TryResolveExact("NSHR", out var shape).Should().BeTrue();
        shape.CommandId.Should().Be(new RibbonCommandId("insert.shape.Rectangle"));
    }

    [Fact]
    public void Match_PreservesExactRouteWhileReportingLongerContinuation()
    {
        var catalog = FreeXRibbonKeyTipRoutePlanner.Build(BuildDefinition());

        var home = catalog.Match(" h ");
        home.ExactRoute.Should().NotBeNull();
        home.ExactRoute!.RouteName.Should().Be("tab:HomeTab");
        home.HasLongerRoute.Should().BeTrue();

        var nestedPrefix = catalog.Match("hbs");
        nestedPrefix.ExactRoute.Should().NotBeNull();
        nestedPrefix.HasLongerRoute.Should().BeTrue();

        catalog.Match("unknown").IsMatch.Should().BeFalse();
    }

    [Fact]
    public void TopLevelRouting_PreservesFileDataAliasVisibilityAndPrefixRules()
    {
        RibbonTopLevelKeyTipEntry[] visible =
        [
            new("File", "F"),
            new("Home", "H"),
            new("Data", "A"),
            new("Draw", "J"),
        ];

        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("F", visible)!.Value.Kind
            .Should().Be(RibbonTopLevelKeyTipActionKind.BackstageFile);
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel("D", visible)!.Value.RibbonTabHeader
            .Should().Be("Data");
        FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel(
                "D",
                [new RibbonTopLevelKeyTipEntry("Draw", "J")])
            .Should().BeNull();
        FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix("J", ["J", "JS"])
            .Should().BeTrue();
        FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix("H", ["F", "H", "N"])
            .Should().BeFalse();
    }

    [Fact]
    public void NativeShells_DelegateRoutePolicyToPresentationPlanner()
    {
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaRibbonHost.cs");
        var avaloniaInput = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.LegacyShortcutSequences.cs");
        var wpf = ReadSource("src", "FreeX.App.Host", "MainWindow.Editing.cs")
            + ReadSource("src", "FreeX.App.Host", "MainWindow.KeyTips.cs");

        avalonia.Should().Contain("FreeXRibbonKeyTipRoutePlanner.Build");
        avalonia.Should().Contain("FreeXRibbonKeyTipMatch Match");
        avalonia.Should().NotContain("AvaloniaRibbonKeyTipRouteKind");
        avalonia.Should().NotContain("AvaloniaRibbonKeyTipRoute(");
        avalonia.Should().NotContain("AvaloniaRibbonKeyTipMatch(");
        avalonia.Should().NotContain("ContextualTabInputs");
        avalonia.Should().NotContain("new(\"NCH\"");
        wpf.Should().Contain("FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel");
        wpf.Should().Contain("FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix");
        wpf.Should().NotContain("string.Equals(normalizedKeyTip, \"D\"");
        wpf.Should().Contain("_ribbonKeyTipSession.HandleToken(token)");
        avaloniaInput.Should().Contain("_ribbonKeyTipSession.HandleToken(token)");
        avaloniaInput.Should().NotContain("LegacyDataFilterSequenceState");
        avaloniaInput.Should().NotContain("LegacyEditPasteSpecialSequenceState");
        avaloniaInput.Should().NotContain("_ribbonKeyTipInput");
        avaloniaInput.Should().NotContain("_quickAccessKeyTipInput");
        File.Exists(Path.Combine(
                RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host"),
                "RibbonTopLevelKeyTipRouter.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(
                RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host"),
                "RibbonKeyTipMode.cs"))
            .Should().BeFalse();
    }

    private static RibbonDefinition BuildDefinition()
    {
        var nestedMenu = new RibbonMenu(
        [
            new RibbonMenuItem(
                "Submenu",
                KeyTip: "S",
                Children:
                [
                    new RibbonMenuItem(
                        "Nested",
                        new RibbonCommandId("Nested Command"),
                        "D")
                ])
        ]);
        var group = new RibbonGroup(
            "HomeGroup",
            "Home Group",
            null,
            100,
            [
                new RibbonButton("Direct Command", "Direct") { KeyTip = "X" },
                new RibbonSplitButton("Menu Command", "Menu", nestedMenu) { KeyTip = "B" },
            ],
            RibbonGroupSizing.Default);

        return new RibbonDefinition(
        [
            new RibbonTab("HomeTab", "Home", "H", null, [group]),
            new RibbonTab(
                "ContextTab",
                "Context",
                "QZ",
                new RibbonTabContext("context.active", "Context", RibbonContextColor.Teal, "QZ"),
                []),
        ]);
    }

    private static string ReadSource(params string[] parts) =>
        TestWorkspaceFileLocator.ReadAllText(parts);
}
