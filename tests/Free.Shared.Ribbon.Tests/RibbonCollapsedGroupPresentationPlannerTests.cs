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

    private static RibbonGroup CreateGroup(string header, params RibbonControl[] controls) =>
        new(
            header.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant(),
            header,
            KeyTip: null,
            Priority: 0,
            controls,
            RibbonGroupSizing.Default);
}
