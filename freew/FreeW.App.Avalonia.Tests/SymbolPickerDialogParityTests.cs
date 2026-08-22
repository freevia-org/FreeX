using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
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
        source.Should().Contain("new ImmutableSolidColorBrush(Colors.White)");
        source.Should().Contain("Class(\":pointerover\")");
        source.Should().Contain("Class(\":focus\")");
        source.Should().Contain("Class(\":pressed\")");
        source.Should().Contain("Focusable = true");
        source.Should().Contain("Focus();");
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
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs
                    .Select(glyph => FreeWSymbolPickerDialogPlanner.BuildSemantic(glyph).AutomationId));
            dialog.GlyphButtonsForTest
                .Select(global::Avalonia.Automation.AutomationProperties.GetName)
                .Should().Equal(FreeWSymbolPickerDialogPlanner.Glyphs);
            global::Avalonia.Automation.AutomationProperties.GetAutomationId(dialog)
                .Should().Be(FreeWSymbolPickerDialogPlanner.DialogAutomationId);
            var cancel = dialog.GetLogicalDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == FreeWSymbolPickerDialogPlanner.CancelText);
            global::Avalonia.Automation.AutomationProperties.GetAutomationId(cancel)
                .Should().Be(FreeWSymbolPickerDialogPlanner.CancelAutomationId);
            dialog.SelectGlyphForTest("\u03a9").Should().Be("\u03a9");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dialog_Realizes_WpfInitialFocusAndTileChrome()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new SymbolPickerDialog
            {
                Width = 560,
                Height = 600,
            };

            dialog.Show();
            try
            {
                dialog.Measure(new global::Avalonia.Size(560, 600));
                dialog.Arrange(new global::Avalonia.Rect(0, 0, 560, 600));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                dialog.IsFocused.Should().BeTrue("WPF focuses the dialog surface for the initial picker state");
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(dialog);

                dialog.GlyphButtonsForTest.All(button =>
                {
                    var background = button.Background as ISolidColorBrush;
                    var border = button.BorderBrush as ISolidColorBrush;
                    return background?.Color == Colors.White
                        && border?.Color == Color.FromRgb(200, 200, 200)
                        && button.BorderThickness == new global::Avalonia.Thickness(1)
                        && button.MinWidth == FreeWSymbolPickerDialogPlanner.ButtonSize
                        && button.Height == FreeWSymbolPickerDialogPlanner.ButtonSize;
                }).Should().BeTrue();

                var cancel = dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(button => button.Content?.ToString() == FreeWSymbolPickerDialogPlanner.CancelText);
                cancel.IsCancel.Should().BeTrue();
                cancel.IsDefault.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
