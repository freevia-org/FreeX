namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-88 regression test for finding R88-app-status-bar-aggregates-5-1 (MED): the Avalonia status
/// bar's "Customize Status Bar" chooser toggles (Minimum, Maximum, Sum, etc.) never persisted across
/// restarts -- <c>_statusBarOptionVisibility</c> was seeded purely from
/// <c>AvaloniaStatusBarSource.CreateDefaultOptionVisibility()</c> (the hardcoded Excel defaults) with
/// no read from the on-disk options file, and <c>OnStatusBarCustomizeToggled</c> only updated the
/// in-memory dictionary, never calling into <c>AppOptionsStore</c>/<c>StatusBarOptionVisibilityStore</c>
/// the way the WPF host's <c>StatusBarCustomizeMenuItem_Click</c> already does. Fixed by seeding from
/// the persisted <c>AppOptions.StatusBarShow*</c> toggles and saving on every toggle.
/// </summary>
public sealed class R88_StatusBarCustomizePersistenceSourceTests
{
    private static string ReadStatusBarSource() =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("src", "FreeX.App.Avalonia", "MainWindow.StatusBar.cs") +
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("src", "FreeX.App.Avalonia", "MainWindow.cs");

    [Fact]
    public void OptionVisibilityField_SeedsFromPersistedAppOptions_NotHardcodedDefaults()
    {
        var source = ReadStatusBarSource();

        // This is the exact regression: before the fix, the field initializer never consulted the
        // on-disk options file at all.
        source.Should().Contain(
            ".ToVisibility(_optionsRuntimeSession.LiveOptions)",
            "the status-bar customize toggles must be seeded from the persisted AppOptions.StatusBarShow* " +
            "values (mirroring the WPF host's FreeXOptions-backed store) instead of always resetting to " +
            "the hardcoded Excel defaults on every relaunch");
    }

    [Fact]
    public void CustomizeToggle_PersistsThroughAppOptionsStore()
    {
        var source = ReadStatusBarSource();
        var methodStart = source.IndexOf("private void OnStatusBarCustomizeToggled(", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0, "the customize-toggle handler must still exist");
        var methodEnd = source.IndexOf("// ── Accessibility", methodStart, StringComparison.Ordinal);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("StatusBarOptionUpdateWorkflow.ApplyToRuntimeSession(");
        method.Should().Contain("_optionsRuntimeSession,");
        method.Should().NotContain("StatusBarOptionVisibilityStore.TrySetOption(");
        method.Should().NotContain("AppOptionsStore.Save(");
    }

    [Fact]
    public void CustomizeToggle_StillUpdatesInMemoryMapAndReRendersStatusBar_NoRegression()
    {
        // No-regression sibling: persisting the toggle must not have displaced the pre-existing
        // in-memory update / re-render, or the status bar would stop reflecting the change live.
        var source = ReadStatusBarSource();
        var methodStart = source.IndexOf("private void OnStatusBarCustomizeToggled(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("// ── Accessibility", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("_statusBarOptionVisibility.Clear();");
        method.Should().Contain("_statusBarOptionVisibility[tag] = isVisible;");
        method.Should().Contain("ApplyStatusBarModel(_statusText.Text ?? AvaloniaStatusBarSource.ReadyText());");
    }
}
