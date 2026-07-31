namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonCollapsedGroupPresentationPlannerTests
{
    [Fact]
    public void DeriveGroupKeyTip_UsesHeaderLettersBeforeGenericFallbacks()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip("Page Setup", used)
            .Should().Be("PA");
        RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip("PivotTable Analyze", used)
            .Should().Be("PI");
        RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip("Pivots", used)
            .Should().Be("PV");
        RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip("###", used)
            .Should().Be("G");
    }

    [Fact]
    public void CreatePresentation_CombinesKeyTipRepresentativeIconAndOverflowProjection()
    {
        var menu = new RibbonMenu(new[]
        {
            new RibbonMenuItem("Keep", "keep"),
        });
        var group = CreateGroup(
            "Clipboard",
            new RibbonRowBreak(),
            new RibbonSeparator(),
            new RibbonButton("paste", "Paste") with
            {
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Paste, RibbonCommandIconAccent.Green),
                KeyTip = "V",
            },
            new RibbonDropdown("paste-options", "Paste Options", menu) with
            {
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Paste),
            });

        var presentation = RibbonCollapsedGroupPresentationPlanner.CreatePresentation(
            group,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            includeOverflowSeparators: true);

        presentation.GroupId.Should().Be("clipboard");
        presentation.Header.Should().Be("Clipboard");
        presentation.KeyTip.Should().Be("CL");
        presentation.RepresentativeIcon.Icon.Should().Be(new RibbonCommandIcon(
            RibbonCommandIconKind.Paste,
            RibbonCommandIconAccent.Green));
        presentation.RepresentativeIcon.CommandName.Should().Be("paste");
        presentation.OverflowControls
            .Select(control => control.GetType())
            .Should()
            .Equal(typeof(RibbonSeparator), typeof(RibbonButton), typeof(RibbonDropdown));
    }

    [Fact]
    public void GetOverflowControls_SkipsStructuralRowsAndCanOmitSeparators()
    {
        var group = CreateGroup(
            "Font",
            new RibbonButton("bold", "Bold"),
            new RibbonRowBreak(),
            new RibbonSeparator(),
            new RibbonButton("italic", "Italic"),
            new RibbonLabel("empty", ""));

        RibbonCollapsedGroupPresentationPlanner.GetOverflowControls(group)
            .Select(control => control.Label)
            .Should()
            .Equal("Bold", "Italic");

        RibbonCollapsedGroupPresentationPlanner.GetOverflowControls(group, includeSeparators: true)
            .Select(control => control.GetType())
            .Should()
            .Equal(typeof(RibbonButton), typeof(RibbonSeparator), typeof(RibbonButton));
    }

    [Fact]
    public void GetRepresentativeIcon_FallsBackToGenericWhenGroupHasNoCommandIcon()
    {
        var group = CreateGroup(
            "Empty",
            new RibbonSeparator(),
            new RibbonButton("plain", "Plain"));

        RibbonCollapsedGroupPresentationPlanner.GetRepresentativeIcon(group)
            .Should()
            .Be(new RibbonCollapsedGroupRepresentativeIcon(
                new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                CommandName: null));
    }

    [Fact]
    public void PopupInteractionPlanner_SkipsDisabledAndNonFocusableItemsWithWraparound()
    {
        var items = new[]
        {
            new RibbonPopupFocusItem(IsFocusable: true, IsEnabled: false),
            new RibbonPopupFocusItem(IsFocusable: false, IsEnabled: true),
            new RibbonPopupFocusItem(IsFocusable: true, IsEnabled: true),
            new RibbonPopupFocusItem(IsFocusable: true, IsEnabled: true),
        };

        RibbonPopupInteractionContract.CollapsedGroup.Placement.Should().Be(RibbonPopupPlacement.BelowAnchor);
        RibbonPopupInteractionContract.CollapsedGroup.DismissOnEscape.Should().BeTrue();
        RibbonPopupInteractionPlanner.FindFirstFocusableItem(items).Should().Be(2);
        RibbonPopupInteractionPlanner.FindLastFocusableItem(items).Should().Be(3);
        RibbonPopupInteractionPlanner.FindAdjacentFocusableItem(items, 3, 1).Should().Be(2);
        RibbonPopupInteractionPlanner.FindAdjacentFocusableItem(items, 2, -1).Should().Be(3);
    }

    [Fact]
    public void PopupChrome_UsesOneSharedRendererNeutralMetricSet()
    {
        var chrome = RibbonVisualMetrics.PopupChrome;

        chrome.MinWidth.Should().Be(220);
        chrome.MaxWidth.Should().Be(360);
        chrome.ItemMinHeight.Should().Be(28);
        chrome.PopupPadding.Should().Be(new RibbonPopupInsets(4, 4, 4, 4));
        chrome.ItemPadding.Should().Be(new RibbonPopupInsets(10, 5, 10, 5));
        chrome.BorderThickness.Should().Be(1);
        chrome.ShadowDepth.Should().Be(2);
        chrome.ShadowBlurRadius.Should().Be(8);
        chrome.ShadowOpacity.Should().Be(0.22);
    }

    [Fact]
    public void PopupPlacementPlanner_FlipsAboveAndClampsHorizontallyAtScreenEdges()
    {
        var result = RibbonPopupPlacementPlanner.Plan(
            new RibbonPopupRect(790, 570, 24, 24),
            new RibbonPopupRect(0, 0, 220, 120),
            new RibbonPopupRect(0, 0, 800, 600));

        result.Placement.Should().Be(RibbonPopupPlacement.AboveAnchor);
        result.X.Should().Be(580);
        result.Y.Should().Be(449);
    }

    private static RibbonGroup CreateGroup(string header, params RibbonControl[] controls) =>
        new(
            header.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant(),
            header,
            KeyTip: null,
            Priority: 0,
            controls,
            RibbonGroupSizing.Default);
}
