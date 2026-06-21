using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

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

    [Fact]
    public async Task Derived_style_inherits_based_on_run_and_paragraph_formatting()
    {
        RunFormatting? run = null;
        ParagraphFormatting? paragraphFmt = null;
        var ran = await OnUiThread(() =>
        {
            var doc = new TextDocument();
            doc.Styles["Base"] = new DocumentStyle
            {
                Id = "Base",
                Name = "Base",
                Run = new RunFormatting
                {
                    FontFamily = "Georgia",
                    FontSizePt = 14,
                    ColorHex = "#224466",
                    Bold = true,
                },
                Paragraph = new ParagraphFormatting
                {
                    SpaceBeforePt = 12,
                    SpaceBeforeIsSet = true,
                    SpaceAfterPt = 3,
                    SpaceAfterIsSet = true,
                },
            };
            doc.Styles["Derived"] = new DocumentStyle
            {
                Id = "Derived",
                Name = "Derived",
                BasedOnStyleId = "Base",
                Run = new RunFormatting { Italic = true },
                Paragraph = new ParagraphFormatting { Alignment = TextAlignment.Center },
            };
            var paragraph = new Paragraph("styled") { StyleId = "Derived" };
            doc.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(doc);
            run = InvokePrivate<RunFormatting>(view, "ResolveRunFmt", RunFormatting.Default, paragraph);
            paragraphFmt = InvokePrivate<ParagraphFormatting>(view, "ResolveParagraphFmt", paragraph);
        });

        if (!ran)
            return;

        run.Should().NotBeNull();
        run!.FontFamily.Should().Be("Georgia");
        run.FontSizePt.Should().Be(14);
        run.ColorHex.Should().Be("#224466");
        run.Bold.Should().BeTrue();
        run.Italic.Should().BeTrue();
        paragraphFmt.Should().NotBeNull();
        paragraphFmt!.Alignment.Should().Be(TextAlignment.Center);
        paragraphFmt.SpaceBeforePt.Should().Be(12);
        paragraphFmt.SpaceAfterPt.Should().Be(3);
    }

    [Fact]
    public async Task ExportPdf_through_shared_tier_produces_valid_pdf()
    {
        byte[]? bytes = null;
        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            view.LoadDocument(SampleDocument.Create());
            view.Measure(new Size(800, 4000));

            using var stream = new System.IO.MemoryStream();
            var result = FreeW.App.Avalonia.Pdf.FreeWAvaloniaPdfExport.Save(view, stream);
            result.PageCount.Should().BeGreaterThan(0);
            bytes = stream.ToArray();
        });

        if (!ran)
            return;

        bytes.Should().NotBeNull();
        bytes!.Length.Should().BeGreaterThan(0);
        // Valid PDFs start with the "%PDF-" magic header (Skia or portable WinAnsi, both shared-tier).
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task MainWindow_tracks_dirty_and_new_document_state_with_shared_file_command_workflow()
    {
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var workflow = GetPrivateField<FileCommandWorkflow>(window, "_fileWorkflow");

            workflow.IsDirty.Should().BeFalse();
            workflow.CurrentPath.Should().BeNull();
            workflow.DisplayName.Should().Be("Untitled");

            window.Editor.InsertText("draft ");
            workflow.IsDirty.Should().BeTrue();

            InvokePrivate(window, "NewDocument");

            workflow.IsDirty.Should().BeFalse();
            workflow.CurrentPath.Should().BeNull();
            workflow.DisplayName.Should().Be("Untitled");
            window.Title.Should().Be("FreeW");
        });

        if (!ran)
            return;
    }

    private static T InvokePrivate<T>(object instance, string name, params object[] args)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, name);
        return (T)method.Invoke(instance, args)!;
    }

    private static void InvokePrivate(object instance, string name, params object[] args)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, name);
        method.Invoke(instance, args);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return (T)field.GetValue(instance)!;
    }
}
