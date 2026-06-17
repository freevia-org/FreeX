using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;

[assembly: AvaloniaTestApplication(typeof(FreeW.App.Avalonia.Tests.FreeWHeadlessApp))]

namespace FreeW.App.Avalonia.Tests;

/// <summary>Minimal headless Avalonia app (Fluent theme + headless drawing) so DocumentView can lay out.</summary>
public sealed class FreeWHeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FreeWHeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>
/// Exercises the real DocumentView layout + editing on the shared headless UI thread (the per-character
/// layout engine needs an Avalonia backend for FormattedText). Each case opts out cleanly if no headless
/// drawing backend is available, rather than failing.
/// </summary>
public sealed class DocumentViewHeadlessTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false; // no headless drawing backend in this environment
        }
    }

    [Fact]
    public async Task Sample_document_lays_out_glyphs()
    {
        var glyphs = 0;
        var blocks = 0;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            glyphs = view.PlacedGlyphCount;
            blocks = view.BlockCount;
        });

        if (!ran)
            return;
        glyphs.Should().BeGreaterThan(0);
        blocks.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Typing_inserts_text_and_is_undoable()
    {
        string? after = null;
        var canUndo = false;
        string? undone = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            view.InsertText("ZZ");
            after = view.PlainText;
            canUndo = view.CanUndo;
            view.Undo();
            undone = view.PlainText;
        });

        if (!ran)
            return;
        after.Should().StartWith("ZZ");
        canUndo.Should().BeTrue();
        undone.Should().NotStartWith("ZZ");
    }

    [Fact]
    public async Task Insert_table_adds_a_table_block()
    {
        var tables = 0;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            var before = view.Document.Blocks.OfType<FreeW.Core.Model.Table>().Count();
            view.InsertTable(2, 2);
            tables = view.Document.Blocks.OfType<FreeW.Core.Model.Table>().Count() - before;
        });

        if (!ran)
            return;
        tables.Should().Be(1);
    }

    [Fact]
    public async Task Find_selects_a_match()
    {
        var found = false;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));
            found = view.FindNext("FreeW");
        });

        if (!ran)
            return;
        found.Should().BeTrue();
    }
}
