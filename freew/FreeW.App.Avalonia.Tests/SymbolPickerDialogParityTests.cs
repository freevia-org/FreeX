using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests;

public sealed class SymbolPickerDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void Dialog_UsesSharedGridMetricsAndRestoresTilesAfterSharedChrome()
    {
        var source = File.ReadAllText(RepoFile("freew", "FreeW.App.Avalonia", "SymbolPickerDialog.cs"));

        source.Should().Contain("FreeWSymbolPickerDialogPlanner.Glyphs");
        source.Should().Contain("ApplyGlyphButtonChrome(grid)");
        source.Should().Contain("button.Height = FreeWSymbolPickerDialogPlanner.ButtonSize;");
        source.Should().Contain("button.MaxHeight = FreeWSymbolPickerDialogPlanner.ButtonSize;");
        source.Should().Contain("GlyphButtonTemplate");
        source.Should().Contain("Class(\":pointerover\")");
        source.Should().Contain("Class(\":focus\")");
        source.Should().Contain("Class(\":pressed\")");
        source.Should().Contain("IsCancel = true");
        source.Should().NotContain("_glyphButtons[0].Focus()");
        source.Should().NotContain("var Glyphs =");
    }

    [Fact]
    public async Task Dialog_ExposesTheSharedCatalogAndStableAutomationContract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new SymbolPickerDialog();

            dialog.GlyphButtonsForTest.Should().HaveCount(FreeWSymbolPickerDialogPlanner.Glyphs.Count);
            dialog.GlyphButtonsForTest.Select(button => (string)button.Content!)
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs);
            dialog.GlyphButtonsForTest.Should().OnlyContain(button =>
                button.MinWidth == FreeWSymbolPickerDialogPlanner.ButtonSize
                && button.Height == FreeWSymbolPickerDialogPlanner.ButtonSize
                && button.Margin == new global::Avalonia.Thickness(FreeWSymbolPickerDialogPlanner.ButtonMargin)
                && button.HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Stretch
                && button.VerticalAlignment == global::Avalonia.Layout.VerticalAlignment.Stretch
                && button.FontSize == FreeWSymbolPickerDialogPlanner.ButtonFontSize);
            dialog.GlyphButtonsForTest.Select(global::Avalonia.Automation.AutomationProperties.GetAutomationId)
                .Should().OnlyContain(id => id.StartsWith("SymbolPicker", StringComparison.Ordinal));
            dialog.GlyphButtonsForTest
                .Select(global::Avalonia.Automation.AutomationProperties.GetName)
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs);
            dialog.SelectGlyphForTest("\u03a9").Should().Be("\u03a9");
        }, CancellationToken.None);
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(RepoFile);
}
