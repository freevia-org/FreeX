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
