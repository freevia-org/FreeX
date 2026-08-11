using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using System.IO;
using Free.Shared.AppServices;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// R128 (Avalonia counterpart of FreeP.App.Host.Tests/OptionsDialogTests.cs — BOTH-SHELLS rule): FreeP's
/// Backstage "Options" pane had no "Edit options…" link on either shell. The Avalonia BuildOptionsPane was
/// hand-rolled (no shared BackstageOptionsPaneSpec.Edit/EditText plumbing at all) and never wired an edit
/// action, so there was no in-app way to reach the new OptionsDialog even after adding the WPF one. This
/// suite proves the Avalonia Backstage pane now exposes the edit link and that the Avalonia OptionsDialog
/// itself parses/normalizes correctly.
/// </summary>
public sealed class OptionsDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static OptionsDialogTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    [Fact]
    public async Task R128_BackstageOptionsPane_ExposesEditOptionsLink()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow(Array.Empty<string>(), loadRecentFilesStore: null, options: new FreePOptions());
            window.ActivateBackstageEntryForTests("Options").Should().BeTrue();

            var content = window.CurrentBackstagePaneContentForTests;
            content.Should().NotBeNull();

            var buttons = content!.GetLogicalDescendants().OfType<Button>().ToArray();
            buttons.Should().Contain(button => Equals(
                button.Content,
                FreePBackstagePaneTextCatalog.Descriptor.OptionsEditText!.FallbackText));
        }, CancellationToken.None);
    }

    [Fact]
    public void BackstageOptionsPane_UsesCatalogLabelWithoutHostFallback()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "freep", "FreeP.App.Avalonia", "Backstage", "BackstageView.cs"));

        source.Should().Contain("Panes.BuildOptionsPane(PanePlans.BuildOptionsPane(");
        source.Should().NotContain("Edit options");
    }

    [Fact]
    public async Task R128_BackstageOptionsPane_StillRendersReadOnlySummaryFields()
    {
        // No-regression sibling: the read-only field summary the Avalonia pane already rendered before
        // this fix must still be present alongside the new edit link, not replaced by it.
        await Session.Dispatch(() =>
        {
            var options = new FreePOptions { RecentFilesCap = 12 };
            var window = new MainWindow(Array.Empty<string>(), loadRecentFilesStore: null, options: options);
            window.ActivateBackstageEntryForTests("Options").Should().BeTrue();

            var content = window.CurrentBackstagePaneContentForTests;
            var textBlocks = content!.GetLogicalDescendants().OfType<TextBlock>().ToArray();
            textBlocks.Should().Contain(tb => tb.Text != null && tb.Text.Contains("12"));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task R128_OptionsDialog_Accept_NormalizesAndReturnsResult()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreePOptions { RecentFilesCap = 5 });
            dialog.RecentFilesCapForTest.Text = "20";
            dialog.UiLanguageForTest.Text = "de-DE";

            dialog.AcceptForTest();

            dialog.Result.Should().NotBeNull();
            dialog.Result!.RecentFilesCap.Should().Be(20);
            dialog.Result.UiLanguage.Should().Be("de-DE");
            dialog.Result.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task R128_OptionsDialog_Accept_RejectsOutOfRangeCapAndShowsStatus()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreePOptions());
            dialog.RecentFilesCapForTest.Text = "not-a-number";

            dialog.AcceptForTest();

            dialog.Result.Should().BeNull();
            dialog.StatusForTest.IsVisible.Should().BeTrue();
        }, CancellationToken.None);
    }
}
