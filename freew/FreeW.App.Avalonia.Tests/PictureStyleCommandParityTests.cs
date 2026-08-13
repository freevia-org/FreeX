using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class PictureStyleCommandParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void AvaloniaRibbon_UsesWpfPictureGroupOrderAndSharedStyleCatalog()
    {
        var pictureTab = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia).FindTab("picture-format")!;
        pictureTab.Groups.Select(group => group.Id)
            .Should().Equal("picture-arrange", "picture-styles", "picture-adjust", "picture-size");

        var controls = pictureTab.FindGroup("picture-styles")!.Controls.Cast<RibbonButton>().ToArray();
        controls.Select(control => control.CommandId.Value)
            .Should().Equal(PictureStyleCatalog.Catalog.Select(preset => $"freew.image-style-{preset.Id}"));
        controls.Select(control => control.Label)
            .Should().Equal(PictureStyleCatalog.Catalog.Select(preset => preset.Name));
        controls.Should().OnlyContain(control =>
            control.Icon != null && control.Icon.Kind == RibbonCommandIconKind.Border);
    }

    [Fact]
    public async Task PictureStyleRegistryRoutes_ApplySharedCatalogPresetAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var (editor, image) = SelectedImage();
            var registry = FreeWAvaloniaRibbonCommands.Build(editor, NoopCallbacks());

            foreach (var preset in PictureStyleCatalog.Catalog)
            {
                var command = Stateful(registry, $"freew.image-style-{preset.Id}");
                command.GetState().IsEnabled.Should().BeTrue();

                command.Execute(RibbonCommandContext.Empty);

                PictureStyle(image).Should().Be(PictureStyle(preset));
                editor.SelectedFloatingImage().Should().BeSameAs(image);
                editor.Undo();
                PictureStyle(image).Should().Be((99, "112233", 0.75, "dash", 5, 2, 1.25));
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PictureStyleCommands_AreDisabledWithoutPictureSelection()
    {
        await Session.Dispatch(() =>
        {
            var editor = new DocumentView();
            editor.LoadDocument(TextDocument.CreateEmpty());
            var registry = FreeWAvaloniaRibbonCommands.Build(editor, NoopCallbacks());

            foreach (var preset in PictureStyleCatalog.Catalog)
                Stateful(registry, $"freew.image-style-{preset.Id}")
                    .GetState().IsEnabled.Should().BeFalse();

            editor.CanUndo.Should().BeFalse();
        }, CancellationToken.None);
    }

    private static (DocumentView Editor, InlineImage Image) SelectedImage()
    {
        var image = new InlineImage(OnePixelPng(), 240, 120)
        {
            Wrapping = ImageWrapping.Square,
            BorderColorHex = "112233",
            BorderWidthPt = 0.75,
            BorderDash = "dash",
            ShadowPreset = 5,
            ReflectionPreset = 2,
            SoftEdgePt = 1.25,
            PictureStylePreset = 99,
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var editor = new DocumentView();
        editor.LoadDocument(document);
        editor.Measure(new Size(800, 1200));
        editor.SelectFloating(0, 0);
        return (editor, image);
    }

    private static IRibbonStatefulCommand Stateful(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }

    private static (int Id, string? Border, double Width, string? Dash, int Shadow, int Reflection, double SoftEdge)
        PictureStyle(InlineImage image) =>
        (image.PictureStylePreset, image.BorderColorHex, image.BorderWidthPt, image.BorderDash,
            image.ShadowPreset, image.ReflectionPreset, image.SoftEdgePt);

    private static (int Id, string? Border, double Width, string? Dash, int Shadow, int Reflection, double SoftEdge)
        PictureStyle(PictureStylePreset preset) =>
        (preset.Id, preset.BorderColorHex, preset.BorderWidthPt, preset.BorderDash,
            preset.ShadowPreset, preset.ReflectionPreset, preset.SoftEdgePt);

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
        new(
            Open: () => { },
            Save: () => { },
            Cut: () => { },
            Copy: () => { },
            Paste: () => { },
            Backstage: () => { },
            NewDocument: () => { },
            ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { },
            ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { },
            SetWebLayout: () => { },
            SetDraftView: () => { },
            OpenFontDialog: () => { },
            OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { },
            ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { },
            InsertPicture: () => { },
            OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });
}
