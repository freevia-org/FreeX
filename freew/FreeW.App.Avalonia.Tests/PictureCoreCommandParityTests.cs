using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class PictureCoreCommandParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void AvaloniaRibbon_ExposesCorePictureCommandsInWpfEquivalentGroups()
    {
        var definition = FreeWRibbon.BuildDefinition();

        definition.FindTab("picture-format")!.FindGroup("picture-adjust")!.Controls
            .Select(CommandId)
            .Should().Contain([
                "freew.image-reset",
                "freew.image-border",
            ]);
        definition.FindTab("picture-format")!.FindGroup("picture-size")!.Controls
            .Select(CommandId)
            .Should().Contain([
                "freew.image-size",
                "freew.image-alt-text",
            ]);
    }

    [Fact]
    public async Task ImageAltTextRegistryRoute_MatchesSelectionMutationCancelAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var (editor, image) = SelectedImage();
            var callbacks = NoopCallbacks() with
            {
                OpenImageAltTextDialog = () => editor.SetSelectedFloatingAltText("  Updated description  "),
            };
            var command = Stateful(FreeWRibbon.BuildRegistry(editor, callbacks), "freew.image-alt-text");

            command.GetState().IsEnabled.Should().BeTrue();
            command.Execute(RibbonCommandContext.Empty);
            image.AltText.Should().Be("Updated description");
            editor.Undo();
            image.AltText.Should().Be("Original description");

            var cancelCommand = Stateful(
                FreeWRibbon.BuildRegistry(editor, NoopCallbacks() with { OpenImageAltTextDialog = () => { } }),
                "freew.image-alt-text");
            cancelCommand.Execute(RibbonCommandContext.Empty);
            image.AltText.Should().Be("Original description");
            editor.CanUndo.Should().BeFalse("cancel leaves the document unchanged");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImageBorderRegistryRoute_MatchesSelectionMutationCancelAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var (editor, image) = SelectedImage();
            var callbacks = NoopCallbacks() with
            {
                OpenImageBorderDialog = () => editor.SetSelectedImageBorder("AABBCC", 2.25, "dot"),
            };
            var command = Stateful(FreeWRibbon.BuildRegistry(editor, callbacks), "freew.image-border");

            command.GetState().IsEnabled.Should().BeTrue();
            command.Execute(RibbonCommandContext.Empty);
            (image.BorderColorHex, image.BorderWidthPt, image.BorderDash)
                .Should().Be(("AABBCC", 2.25, "dot"));
            editor.Undo();
            (image.BorderColorHex, image.BorderWidthPt, image.BorderDash)
                .Should().Be(("112233", 0.75, "dash"));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImageSizeRegistryRoute_MatchesSelectionMutationCancelAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var (editor, image) = SelectedImage();
            var callbacks = NoopCallbacks() with
            {
                OpenImageSizeDialog = () => editor.SetSelectedImageSize(210, 105),
            };
            var command = Stateful(FreeWRibbon.BuildRegistry(editor, callbacks), "freew.image-size");

            command.GetState().IsEnabled.Should().BeTrue();
            command.Execute(RibbonCommandContext.Empty);
            (image.WidthPt, image.HeightPt).Should().Be((210, 105));
            editor.Undo();
            (image.WidthPt, image.HeightPt).Should().Be((240, 120));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImageResetRegistryRoute_MatchesNaturalSizeMutationAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var (editor, image) = SelectedImage();
            var command = Stateful(FreeWRibbon.BuildRegistry(editor, NoopCallbacks()), "freew.image-reset");

            command.GetState().IsEnabled.Should().BeTrue();
            command.Execute(RibbonCommandContext.Empty);
            (image.WidthPt, image.HeightPt).Should().Be((150, 75));
            image.RotationAngle.Should().Be(0);
            image.FlipH.Should().BeFalse();
            image.HasCrop.Should().BeFalse();
            image.BrightnessPct.Should().Be(0);

            editor.Undo();
            (image.WidthPt, image.HeightPt).Should().Be((240, 120));
            image.RotationAngle.Should().Be(45);
            image.FlipH.Should().BeTrue();
            image.CropLeft.Should().Be(0.1);
            image.BrightnessPct.Should().Be(20);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImageAdjustRegistryRoute_MatchesWpfPresetsMutationAndUndo()
    {
        await Session.Dispatch(() =>
        {
            var (editor, image) = SelectedImage();
            var registry = FreeWRibbon.BuildRegistry(editor, NoopCallbacks());

            var brightness = Stateful(registry, "freew.image-brightness-plus40");
            brightness.GetState().IsEnabled.Should().BeTrue();
            brightness.Execute(RibbonCommandContext.Empty);
            image.BrightnessPct.Should().Be(40);
            image.ContrastPct.Should().Be(0);
            image.SaturationPct.Should().Be(100);
            editor.Undo();
            image.BrightnessPct.Should().Be(20);

            Stateful(registry, "freew.image-recolor-sepia").Execute(RibbonCommandContext.Empty);
            image.RecolorMode.Should().Be(ImageRecolorMode.Sepia);
            editor.Undo();
            image.RecolorMode.Should().Be(ImageRecolorMode.None);

            Stateful(registry, "freew.image-colortemp-warm").Execute(RibbonCommandContext.Empty);
            image.ColorTemperature.Should().Be(60);
            editor.Undo();
            image.ColorTemperature.Should().Be(0);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImageAdjustRegistryRoute_RegistersAllWpfPicturePresetIds()
    {
        await Session.Dispatch(() =>
        {
            var (editor, _) = SelectedImage();
            var registry = FreeWRibbon.BuildRegistry(editor, NoopCallbacks());
            var ids = new[]
            {
                "freew.image-brightness-plus20", "freew.image-brightness-plus40",
                "freew.image-brightness-minus20", "freew.image-brightness-minus40",
                "freew.image-contrast-plus20", "freew.image-contrast-minus20",
                "freew.image-saturation-0", "freew.image-saturation-50", "freew.image-saturation-200",
                "freew.image-transparency-25", "freew.image-transparency-50", "freew.image-transparency-75",
                "freew.image-recolor-grayscale", "freew.image-recolor-sepia",
                "freew.image-recolor-washout", "freew.image-recolor-blackwhite", "freew.image-recolor-none",
                "freew.image-colortemp-warm", "freew.image-colortemp-cool", "freew.image-colortemp-neutral",
                "freew.image-shadow-none", "freew.image-shadow-1", "freew.image-shadow-5",
                "freew.image-reflection-none", "freew.image-reflection-1", "freew.image-reflection-5",
                "freew.image-glow-none", "freew.image-glow-5", "freew.image-glow-18",
                "freew.image-softedge-none", "freew.image-softedge-1", "freew.image-softedge-2pt5",
                "freew.image-bevel-none", "freew.image-bevel-1", "freew.image-bevel-4",
                "freew.image-artistic-none", "freew.image-artistic-blur", "freew.image-artistic-mosaic",
            };

            foreach (var id in ids)
            {
                Stateful(registry, id).GetState().IsEnabled.Should().BeTrue(id);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaImageAdjustPipeline_RendersAdjustedBitmapWithoutMutatingSourceBytes()
    {
        await Session.Dispatch(() =>
        {
            using var source = new Bitmap(new MemoryStream(OnePixelPng()));
            var image = new InlineImage(OnePixelPng(), 24, 24) { BrightnessPct = 40 };
            var originalBytes = image.PngBytes.ToArray();

            var adjusted = AvaloniaImageAdjustHelper.Apply(source, image);

            adjusted.Should().NotBeSameAs(source);
            image.PngBytes.Should().Equal(originalBytes);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DecodeBitmap_NeutralImageRemainsUsableAfterReturningToCache()
    {
        await Session.Dispatch(() =>
        {
            var image = new InlineImage(OnePixelPng(), 24, 24);
            var view = new DocumentView();
            view.LoadDocument(TextDocument.CreateEmpty());

            var first = view.DecodeBitmap(image);
            var second = view.DecodeBitmap(image);

            first.Should().NotBeNull();
            first!.PixelSize.Should().Be(new PixelSize(1, 1));
            second.Should().BeSameAs(first);
            second!.PixelSize.Should().Be(new PixelSize(1, 1));

            using var target = new WriteableBitmap(
                first.PixelSize,
                first.Dpi,
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using var framebuffer = target.Lock();
            first.CopyPixels(framebuffer);
            framebuffer.Size.Should().Be(first.PixelSize);

            view.LoadDocument(TextDocument.CreateEmpty());
            var replacement = view.DecodeBitmap(image);
            replacement.Should().NotBeNull();
            replacement.Should().NotBeSameAs(first);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaImageAdjustPipeline_UsesWpfPremultipliedTransparencyAndOutput()
    {
        await Session.Dispatch(() =>
        {
            // 200 red at 50% alpha is stored as 100 in Pbgra32/BGRA premultiplied bytes.
            var pixels = new byte[] { 0, 0, 100, 128 };
            AvaloniaImageAdjustHelper.ApplyPixels(
                pixels,
                brightnessPct: 0,
                contrastPct: 0,
                saturationPct: 100,
                transparencyPct: 50,
                ImageRecolorMode.None,
                colorTemperature: 0);

            pixels[2].Should().BeInRange((byte)49, (byte)51);
            pixels[3].Should().BeInRange((byte)63, (byte)65);

            using var source = new Bitmap(new MemoryStream(OnePixelPng()));
            using var adjusted = (WriteableBitmap)AvaloniaImageAdjustHelper.ApplyCore(
                source,
                brightnessPct: 0,
                contrastPct: 0,
                saturationPct: 100,
                transparencyPct: 50);
            using var framebuffer = adjusted.Lock();
            framebuffer.AlphaFormat.Should().Be(AlphaFormat.Premul);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(ImageArtisticEffect.Blur)]
    [InlineData(ImageArtisticEffect.GlowDiffused)]
    [InlineData(ImageArtisticEffect.GlowEdges)]
    [InlineData(ImageArtisticEffect.PencilGrayscale)]
    [InlineData(ImageArtisticEffect.PencilSketch)]
    [InlineData(ImageArtisticEffect.LineDrawing)]
    [InlineData(ImageArtisticEffect.Paintbrush)]
    [InlineData(ImageArtisticEffect.PaintStrokes)]
    [InlineData(ImageArtisticEffect.Photocopy)]
    [InlineData(ImageArtisticEffect.Posterize)]
    [InlineData(ImageArtisticEffect.Pastels)]
    [InlineData(ImageArtisticEffect.Watercolor)]
    [InlineData(ImageArtisticEffect.FilmGrain)]
    [InlineData(ImageArtisticEffect.Mosaic)]
    public async Task AvaloniaArtisticEffect_RendersDifferentRasterPixels(ImageArtisticEffect effect)
    {
        await Session.Dispatch(() =>
        {
            var sourcePixels = RasterSource();
            var image = new InlineImage(OnePixelPng(), 40, 40) { ArtisticEffect = effect };

            var adjustedPixels = AvaloniaImageAdjustHelper.ApplyArtisticPixels(
                sourcePixels, 40, 40, 160, effect);
            var changed = sourcePixels.Where((value, index) => value != adjustedPixels[index]).Count();
            changed.Should().BeGreaterThan(0,
                $"Avalonia artistic effect {effect} must change rendered pixels (source max={sourcePixels.Max()}, adjusted max={adjustedPixels.Max()})");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 5, 0, 0)]
    [InlineData(0, 0, 5, 0)]
    [InlineData(0, 0, 0, 1)]
    public async Task AvaloniaPictureEffect_RendersActualRasterPixels(
        int shadowPreset,
        double glowSizePt,
        double softEdgePt,
        int bevelPreset)
    {
        await Session.Dispatch(() =>
        {
            var sourcePixels = RasterSource();
            var image = new InlineImage(OnePixelPng(), 40, 40)
            {
                ShadowPreset = shadowPreset,
                GlowSizePt = glowSizePt,
                GlowColorHex = "FF0000",
                SoftEdgePt = softEdgePt,
                BevelPreset = bevelPreset,
            };

            var adjustedPixels = AvaloniaImageAdjustHelper.ApplyPictureEffectPixels(
                sourcePixels, 40, 40, 160, image);
            var changed = sourcePixels.Where((value, index) => value != adjustedPixels[index]).Count();
            changed.Should().BeGreaterThan(0,
                "picture effects must render pixels, not only mutate InlineImage model state");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PictureEffectCommands_InvalidateRasterCacheAndUndoForEveryEffectFamily()
    {
        await Session.Dispatch(() =>
        {
            var (editor, image) = SelectedImage();
            var registry = FreeWRibbon.BuildRegistry(editor, NoopCallbacks());

            foreach (var commandId in new[]
                     {
                         "freew.image-shadow-1",
                         "freew.image-reflection-5",
                         "freew.image-glow-18",
                         "freew.image-softedge-2pt5",
                         "freew.image-bevel-4",
                         "freew.image-artistic-mosaic",
                     })
            {
                var before = editor.DecodeBitmap(image);
                Stateful(registry, commandId).Execute(RibbonCommandContext.Empty);
                editor.DecodeBitmap(image).Should().NotBeSameAs(before,
                    $"{commandId} must invalidate the decoded image cache");

                editor.Undo();
                editor.DecodeBitmap(image).Should().NotBeSameAs(before,
                    $"undo after {commandId} must rebuild the cache");
                image.ShadowPreset.Should().Be(0);
                image.ReflectionPreset.Should().Be(0);
                image.GlowSizePt.Should().Be(0);
                image.SoftEdgePt.Should().Be(0);
                image.BevelPreset.Should().Be(0);
                image.ArtisticEffect.Should().Be(ImageArtisticEffect.None);
            }

            editor.LoadDocument(TextDocument.CreateEmpty());
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CorePictureCommands_AreDisabledWithoutPictureSelection()
    {
        await Session.Dispatch(() =>
        {
            var editor = new DocumentView();
            editor.LoadDocument(TextDocument.CreateEmpty());
            var callbacks = NoopCallbacks() with
            {
                OpenImageAltTextDialog = () => { },
                OpenImageBorderDialog = () => { },
                OpenImageSizeDialog = () => { },
            };
            var registry = FreeWRibbon.BuildRegistry(editor, callbacks);

            foreach (var id in new[]
                     {
                         "freew.image-alt-text",
                         "freew.image-border",
                         "freew.image-reset",
                         "freew.image-size",
                     })
            {
                Stateful(registry, id).GetState().IsEnabled.Should().BeFalse(id);
            }
        }, CancellationToken.None);
    }

    private static (DocumentView Editor, InlineImage Image) SelectedImage()
    {
        var image = new InlineImage(OnePixelPng(), 240, 120)
        {
            Wrapping = ImageWrapping.Square,
            AltText = "Original description",
            BorderColorHex = "112233",
            BorderWidthPt = 0.75,
            BorderDash = "dash",
            RotationAngle = 45,
            FlipH = true,
            CropLeft = 0.1,
            BrightnessPct = 20,
            OriginalPixelWidth = 200,
            OriginalPixelHeight = 100,
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
        registry.TryGet(new RibbonCommandId(commandId), out var command)
            .Should().BeTrue($"missing Avalonia command route: {commandId}");
        return command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
    }

    private static string CommandId(RibbonControl control) => control switch
    {
        RibbonButton button => button.CommandId.Value,
        _ => string.Empty,
    };

    private static byte[] RasterSource()
    {
        const int size = 40;
        var pixels = new byte[size * size * 4];
        for (var y = 8; y < 32; y++)
        for (var x = 8; x < 32; x++)
        {
            var offset = (y * size + x) * 4;
            pixels[offset] = (byte)(40 + x);
            pixels[offset + 1] = (byte)(80 + y);
            pixels[offset + 2] = 220;
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static RibbonHostCallbacks NoopCallbacks() =>
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
