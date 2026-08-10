namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class BackstageFrameOwnershipSourceTests
{
    [Fact]
    public void Native_frames_delegate_identity_lookup_selection_and_activation_to_portable_session()
    {
        var root = FindRepositoryRoot();
        var wpf = Read(root, "shared", "Free.Shared.Shell.Wpf", "BackstageFrame.cs");
        var avalonia = Read(root, "shared", "Free.Shared.Shell.Avalonia", "AvaloniaBackstageFrame.cs");

        wpf.Should().Contain("BackstageFrameSession<UIElement>")
            .And.Contain("_session.Show(")
            .And.Contain("_session.FindEntry(")
            .And.Contain("_session.Activate(")
            .And.Contain("activation.Dispatch(");
        avalonia.Should().Contain("BackstageFrameSession<Control>")
            .And.Contain("_session.Show(")
            .And.Contain("_session.FindEntry(")
            .And.Contain("_session.Activate(")
            .And.Contain("activation.Dispatch(");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().NotContain("_defaultPaneLabel")
                .And.NotContain("private void SelectPane(")
                .And.NotContain("private (SisterBackstageEntryPlan<Control>? Entry, Button? Button) FindEntry(");
        }

        avalonia.Should().Contain("BackstageFrameEntryIdentity.From(entry).ResolveAutomationId()")
            .And.NotContain("\"BackstageNav_\" + AutomationIdToken.KeepLettersAndDigits");
    }

    [Fact]
    public void Native_frames_delegate_rail_key_semantics_to_portable_planner()
    {
        var root = FindRepositoryRoot();
        var wpf = Read(root, "shared", "Free.Shared.Shell.Wpf", "BackstageFrame.cs");
        var avalonia = Read(root, "shared", "Free.Shared.Shell.Avalonia", "AvaloniaBackstageFrame.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("BackstageRailNavigationPlanner.Plan(")
                .And.Contain("ToNavigationKey(")
                .And.Contain("plan.TargetIndex")
                .And.NotContain("Math.Max(0, current - 1)")
                .And.NotContain("Math.Min(buttons.Length - 1, current + 1)");
        }

        wpf.Should().NotContain("MoveFocus(new TraversalRequest")
            .And.NotContain("private Button? LastRailButton()");
        avalonia.Should().Contain("HandleKey(e.Key, e.KeyModifiers)");
    }

    [Fact]
    public void Backstage_action_renderers_preserve_shared_automation_identity()
    {
        var root = FindRepositoryRoot();
        var session = Read(root, "freew", "FreeW.App.Presentation", "Backstage", "FreeWBackstageSession.cs");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Backstage", "BackstageView.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Backstage", "BackstageView.cs");
        var composer = Read(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaBackstagePaneComposer.cs");

        session.Should().Contain("AutomationId = action.AutomationId");
        wpf.Should().Contain("action.ResolveAutomationId(\"BackstageAction_\")")
            .And.NotContain("action.Label.Replace(' ', '_')");
        avalonia.Should().Contain("action.ResolveAutomationId(\"BackstageAction_\")")
            .And.NotContain("action.Label.Replace(' ', '_')");
        composer.Should().Contain("action.ResolveAutomationId(automationPrefix + \"_\")")
            .And.NotContain("action.AutomationId ?? automationPrefix");
    }

    [Fact]
    public void Wpf_projection_preserves_every_portable_entry_field()
    {
        var root = FindRepositoryRoot();
        var projection = Read(
            root,
            "shared",
            "Free.Shared.Shell.Wpf",
            "WpfBackstageEntryProjection.cs");
        var builder = Read(
            root,
            "shared",
            "Free.Shared.Shell.Wpf",
            "SisterBackstageEntryBuilder.cs");

        foreach (var property in new[]
        {
            "StableId",
            "KeyTip",
            "AutomationId",
            "AutomationName",
            "AutomationHelpText",
            "TooltipTitle",
            "TooltipDescription",
            "DismissOnActivate",
        })
        {
            projection.Should().Contain($"{property} = plan.{property}")
                .And.Contain($"{property} = entry.{property}");
        }

        builder.Should().Contain("plans.Select(WpfBackstageEntryProjection.FromPlan)")
            .And.NotContain("private static BackstageEntry ToWpfEntry");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
