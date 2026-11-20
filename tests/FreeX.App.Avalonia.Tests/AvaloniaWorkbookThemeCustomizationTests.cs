using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaWorkbookThemeCustomizationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(FreeXRibbonCommandIds.PageLayoutThemeColorsCustomize)]
    [InlineData(FreeXRibbonCommandIds.PageLayoutThemeFontsCustomize)]
    [InlineData(FreeXRibbonCommandIds.PageLayoutThemeEffectsCustomize)]
    public async Task CustomizeRibbonCommandEditsItsThemeFieldsAndRoundTripsXlsx(string commandId)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                var initialTheme = CustomTheme();
                workbook.Theme = initialTheme;
                window.Show();

                ExecuteRibbonCommand(window, commandId);
                var dialog = window.OwnedWindows.OfType<Window>().Should().ContainSingle().Which;
                AutomationProperties.GetAutomationId(dialog).Should().Be("WorkbookThemeDialog");

                WorkbookTheme expected;
                switch (commandId)
                {
                    case FreeXRibbonCommandIds.PageLayoutThemeColorsCustomize:
                        Find<ComboBox>(dialog, "HeadingFontBox").Should().BeNull();
                        Find<ComboBox>(dialog, "EffectsBox").Should().BeNull();
                        Click(Find<Button>(dialog, "WorkbookThemeAccent1ColorPickerButton")!);
                        var colorPicker = dialog.OwnedWindows.OfType<Window>().Should().ContainSingle().Which;
                        colorPicker.Close();
                        var accent = new CellColor(12, 34, 56);
                        Find<TextBox>(dialog, "WorkbookThemeAccent1ColorBox")!.Text = "#0C2238";
                        expected = initialTheme.WithColor(WorkbookThemeColorSlot.Accent1, accent);
                        break;

                    case FreeXRibbonCommandIds.PageLayoutThemeFontsCustomize:
                        // The Windows Customize Fonts command opens the complete theme editor.
                        Find<TextBox>(dialog, "ThemeNameBox").Should().NotBeNull();
                        Find<ComboBox>(dialog, "HeadingFontBox")!.Text = "Georgia";
                        Find<ComboBox>(dialog, "BodyFontBox")!.Text = "Verdana";
                        expected = initialTheme.WithFonts("Georgia", "Verdana");
                        break;

                    default:
                        Find<TextBox>(dialog, "WorkbookThemeAccent1ColorBox").Should().BeNull();
                        Find<ComboBox>(dialog, "HeadingFontBox").Should().BeNull();
                        var effects = Find<ComboBox>(dialog, "EffectsBox")!;
                        effects.IsDropDownOpen = false;
                        effects.Text = "Custom workbook effects";
                        expected = initialTheme.WithEffects("Custom workbook effects");
                        break;
                }

                Click(Find<Button>(dialog, "SaveButton")!);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                AssertTheme(workbook.Theme, expected);
                window.OwnedWindows.Should().BeEmpty();

                using var package = new MemoryStream();
                var adapter = new XlsxFileAdapter();
                adapter.Save(workbook, package);
                package.Position = 0;
                AssertTheme(adapter.Load(package).Theme, expected);

                window.Session.UndoLastEdit().Success.Should().BeTrue();
                workbook.Theme.Should().BeSameAs(initialTheme);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CancelDiscardsScopedChangesAndRibbonPresetAppliesDirectly()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                var initialTheme = CustomTheme();
                workbook.Theme = initialTheme;
                window.Show();

                ExecuteRibbonCommand(window, FreeXRibbonCommandIds.PageLayoutThemeEffectsCustomize);
                var dialog = window.OwnedWindows.OfType<Window>().Should().ContainSingle().Which;
                Find<ComboBox>(dialog, "EffectsBox")!.IsDropDownOpen = false;
                Click(Find<Button>(dialog, "RefinedPresetButton")!);
                Find<ComboBox>(dialog, "EffectsBox")!.Text.Should().Be("Refined");
                Click(Find<Button>(dialog, "WorkbookThemeCancelButton")!);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                workbook.Theme.Should().BeSameAs(initialTheme);
                window.Session.CanUndo.Should().BeFalse();
                ExecuteRibbonCommand(window, FreeXRibbonCommandIds.PageLayoutThemeFontsArial);
                window.OwnedWindows.Should().BeEmpty();
                AssertTheme(workbook.Theme, initialTheme.WithFonts("Arial", "Arial"));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static WorkbookTheme CustomTheme() =>
        WorkbookTheme.Office
            .WithName("Imported theme")
            .WithFonts("Calibri", "Cambria")
            .WithEffects("Imported effects")
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(18, 35, 61));

    private static void AssertTheme(WorkbookTheme actual, WorkbookTheme expected)
    {
        actual.Name.Should().Be(expected.Name);
        actual.MajorFontName.Should().Be(expected.MajorFontName);
        actual.MinorFontName.Should().Be(expected.MinorFontName);
        actual.EffectsName.Should().Be(expected.EffectsName);
        actual.Colors.Should().BeEquivalentTo(expected.Colors);
    }

    private static void ExecuteRibbonCommand(MainWindow window, string commandId)
    {
        var canonicalId = FreeXRibbonCommandCatalog.GetRequired(commandId);
        window.RibbonCommandRegistryForTest!.TryGet(canonicalId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static T? Find<T>(Window window, string automationId) where T : Control =>
        window.GetVisualDescendants().OfType<T>()
            .SingleOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
