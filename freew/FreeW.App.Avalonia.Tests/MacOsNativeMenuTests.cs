using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia.Tests;

public sealed class MacOsNativeMenuTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(Key.N, KeyModifiers.Meta, FreeWKeyboardCommand.NewDocument)]
    [InlineData(Key.O, KeyModifiers.Meta, FreeWKeyboardCommand.OpenDocument)]
    [InlineData(Key.S, KeyModifiers.Meta, FreeWKeyboardCommand.SaveDocument)]
    [InlineData(Key.S, KeyModifiers.Meta | KeyModifiers.Shift, FreeWKeyboardCommand.SaveDocumentAs)]
    [InlineData(Key.P, KeyModifiers.Meta, FreeWKeyboardCommand.PrintDocument)]
    public async Task NativeFileCommandsUseTheExistingApplicationRoutes(
        Key key, KeyModifiers modifiers, FreeWKeyboardCommand expected)
    {
        await Session.Dispatch(() =>
        {
            FreeWKeyboardCommand? executed = null;
            var closed = false;
            var menu = MainWindow.CreateMacOsMenu(
                command => executed = command, () => closed = true, _ => true);

            Find(menu, key, modifiers).Command!.Execute(null);

            executed.Should().Be(expected);
            closed.Should().BeFalse();
            Find(menu, Key.W, KeyModifiers.Meta).Command!.Execute(null);
            closed.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeReplaceUsesControlHAndPreservesCommandHForHide()
    {
        await Session.Dispatch(() =>
        {
            FreeWKeyboardCommand? executed = null;
            var menu = MainWindow.CreateMacOsMenu(
                command => executed = command, () => { }, _ => true);

            Find(menu, Key.H, KeyModifiers.Control).Command!.Execute(null);

            executed.Should().Be(FreeWKeyboardCommand.Replace);
            menu.Items.OfType<NativeMenuItem>()
                .SelectMany(item => item.Menu!.Items.OfType<NativeMenuItem>())
                .Should().NotContain(item => item.Gesture != null && item.Gesture.Key == Key.H
                    && item.Gesture.KeyModifiers == KeyModifiers.Meta);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeUndoAndRedoUseDocumentHistoryAndCommandShiftZ()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindow();
            try
            {
                var menu = window.BuildMacOsMenu();
                var undo = Find(menu, Key.Z, KeyModifiers.Meta).Command!;
                var redo = Find(menu, Key.Z, KeyModifiers.Meta | KeyModifiers.Shift).Command!;
                undo.CanExecute(null).Should().BeFalse();
                redo.CanExecute(null).Should().BeFalse();
                var original = window.Editor.Document.PlainText;
                window.Editor.InsertText("native menu edit");
                var edited = window.Editor.Document.PlainText;

                undo.CanExecute(null).Should().BeTrue();
                undo.Execute(null);
                window.Editor.Document.PlainText.Should().Be(original);
                redo.CanExecute(null).Should().BeTrue();
                redo.Execute(null);
                window.Editor.Document.PlainText.Should().Be(edited);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeEditingCommandsRespectTheFocusedTextField()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindow();
            try
            {
                var field = new TextBox { Text = "Find this phrase" };
                window.Content = field;
                window.Show();
                field.Focus().Should().BeTrue();
                var documentText = window.Editor.Document.PlainText;

                Find(window.BuildMacOsMenu(), Key.A, KeyModifiers.Meta).Command!.Execute(null);

                field.SelectedText.Should().Be("Find this phrase");
                window.Editor.SelectedText.Should().BeEmpty();
                window.Editor.Document.PlainText.Should().Be(documentText);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeMenuCaptionsFollowTheCurrentUiLanguage()
    {
        await Session.Dispatch(() =>
        {
            var original = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
                var menu = MainWindow.CreateMacOsMenu(_ => { }, () => { }, _ => true);

                menu.Items.OfType<NativeMenuItem>().Select(item => item.Header)
                    .Should().Equal("Fichier", "Édition");
                Find(menu, Key.Z, KeyModifiers.Meta | KeyModifiers.Shift).Header
                    .Should().Be("Rétablir");
            }
            finally
            {
                CultureInfo.CurrentUICulture = original;
            }
        }, CancellationToken.None);
    }

    private static MainWindow CreateWindow() => new(
        [], new FreeWOptions(), new InMemoryApplicationOptionsStore<FreeWOptions>(),
        promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave),
        suppressStartupRecoveryOffer: true);

    private static NativeMenuItem Find(NativeMenu menu, Key key, KeyModifiers modifiers) =>
        menu.Items.OfType<NativeMenuItem>()
            .SelectMany(item => item.Menu!.Items.OfType<NativeMenuItem>())
            .Single(item => item.Gesture is { } gesture && gesture.Key == key
                && gesture.KeyModifiers == modifiers);
}
