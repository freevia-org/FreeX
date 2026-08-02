using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using SkiaSharp;

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

    [Fact]
    public async Task PencilSketch_MatchesWpfWhitePaperBlendAndSaturationBoost()
    {
        await Session.Dispatch(() =>
        {
            const int width = 7;
            const int height = 7;
            var source = ColoredRaster(width, height);
            var expected = WpfPencilSketchExpected(source, width, height, width * 4);
            var actual = AvaloniaImageAdjustHelper.ApplyArtisticPixels(
                source, width, height, width * 4, ImageArtisticEffect.PencilSketch);

            actual.Should().Equal(expected,
                "PencilSketch must retain WPF's white-paper blend followed by saturation 1.6");
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

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(0, 12.0)]
    public async Task OpaquePictureEffectsExpandRasterWithoutMovingSourceContent(
        int shadowPreset,
        double glowSizePt)
    {
        await Session.Dispatch(() =>
        {
            const int width = 16;
            const int height = 12;
            var source = OpaqueRaster(width, height);
            var image = new InlineImage(OnePixelPng(), 160, 120)
            {
                ShadowPreset = shadowPreset,
                GlowSizePt = glowSizePt,
                GlowColorHex = "FF0000",
            };

            var raster = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, image);

            raster.Width.Should().BeGreaterThan(width);
            raster.Height.Should().BeGreaterThan(height);
            raster.SourcePixelRect.Should().Be(new PixelRect(
                raster.SourcePixelRect.X,
                raster.SourcePixelRect.Y,
                width,
                height));

            var outsideAlpha = 0;
            for (var y = 0; y < raster.Height; y++)
            for (var x = 0; x < raster.Width; x++)
            {
                var inSource = x >= raster.SourcePixelRect.X &&
                               x < raster.SourcePixelRect.X + width &&
                               y >= raster.SourcePixelRect.Y &&
                               y < raster.SourcePixelRect.Y + height;
                if (!inSource && raster.Pixels[(y * raster.Stride) + x * 4 + 3] > 0)
                    outsideAlpha++;
            }
            outsideAlpha.Should().BeGreaterThan(0,
                "an opaque source must still expose shadow/glow pixels outside its source bounds");

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = y * width * 4 + x * 4;
                var outputOffset = (y + raster.SourcePixelRect.Y) * raster.Stride +
                                   (x + raster.SourcePixelRect.X) * 4;
                raster.Pixels[outputOffset].Should().Be(source[sourceOffset]);
                raster.Pixels[outputOffset + 1].Should().Be(source[sourceOffset + 1]);
                raster.Pixels[outputOffset + 2].Should().Be(source[sourceOffset + 2]);
                raster.Pixels[outputOffset + 3].Should().Be(source[sourceOffset + 3]);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImportedGlowAlpha_ControlsExpandedHaloAndPresetFallback()
    {
        await Session.Dispatch(() =>
        {
            const int width = 16;
            const int height = 12;
            var source = OpaqueRaster(width, height);

            static InlineImage GlowImage(int? importedAlpha)
            {
                return new InlineImage(OnePixelPng(), 160, 120)
                {
                    GlowSizePt = 12,
                    GlowColorHex = "FF0000",
                    ImportedEffects = importedAlpha is int alpha
                        ? new ShapeEffectLst { HasGlow = true, GlowAlpha = alpha }
                        : null,
                };
            }

            var transparent = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, GlowImage(0));
            var quarter = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, GlowImage(25000));
            var opaque = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, GlowImage(100000));
            var preset = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, GlowImage(null));
            var importedDefault = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, GlowImage(60000));

            static int OutsideAlpha(AvaloniaPictureEffectRaster raster)
            {
                var alpha = 0;
                for (var y = 0; y < raster.Height; y++)
                for (var x = 0; x < raster.Width; x++)
                {
                    var inSource = x >= raster.SourcePixelRect.X &&
                                   x < raster.SourcePixelRect.Right &&
                                   y >= raster.SourcePixelRect.Y &&
                                   y < raster.SourcePixelRect.Bottom;
                    if (!inSource)
                        alpha += raster.Pixels[y * raster.Stride + x * 4 + 3];
                }

                return alpha;
            }

            OutsideAlpha(transparent).Should().Be(0);
            OutsideAlpha(quarter).Should().BeGreaterThan(0).And.BeLessThan(OutsideAlpha(opaque));
            preset.Pixels.Should().Equal(importedDefault.Pixels);
            preset.SourcePixelRect.Should().Be(importedDefault.SourcePixelRect);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DecodeBitmap_UsesResolvedLinkedPreviewWhenEmbeddedBytesAreAbsent()
    {
        await Session.Dispatch(() =>
        {
            var image = new InlineImage([], 24, 24)
            {
                LinkedImageTarget = "linked.png",
                ResolvedLinkedImageBytes = OnePixelPng()
            };
            var view = new DocumentView();
            view.LoadDocument(TextDocument.CreateEmpty());

            var decoded = view.DecodeBitmap(image);

            decoded.Should().NotBeNull();
            decoded!.PixelSize.Should().Be(new PixelSize(1, 1));
            image.Bytes.Should().BeEmpty();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImportedShadowAlpha_ControlsExpandedHaloAndPresetFallback()
    {
        await Session.Dispatch(() =>
        {
            const int width = 16;
            const int height = 12;
            var source = OpaqueRaster(width, height);

            static InlineImage ShadowImage(int? importedAlpha)
            {
                return new InlineImage(OnePixelPng(), 160, 120)
                {
                    ShadowPreset = 1,
                    ImportedEffects = importedAlpha is int alpha
                        ? new ShapeEffectLst { HasShadow = true, ShadowAlpha = alpha }
                        : null,
                };
            }

            var transparent = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage(0));
            var quarter = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage(25000));
            var opaque = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage(100000));
            var preset = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage(null));
            static int OutsideAlpha(AvaloniaPictureEffectRaster raster)
            {
                var alpha = 0;
                for (var y = 0; y < raster.Height; y++)
                for (var x = 0; x < raster.Width; x++)
                {
                    var inSource = x >= raster.SourcePixelRect.X &&
                                   x < raster.SourcePixelRect.Right &&
                                   y >= raster.SourcePixelRect.Y &&
                                   y < raster.SourcePixelRect.Bottom;
                    if (!inSource)
                        alpha += raster.Pixels[y * raster.Stride + x * 4 + 3];
                }

                return alpha;
            }

            OutsideAlpha(transparent).Should().Be(0);
            OutsideAlpha(quarter).Should().BeGreaterThan(0).And.BeLessThan(OutsideAlpha(opaque));
            OutsideAlpha(preset).Should().BeGreaterThan(0);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImportedShadowColor_ControlsExpandedHaloAndPresetFallback()
    {
        await Session.Dispatch(() =>
        {
            const int width = 16;
            const int height = 12;
            var source = OpaqueRaster(width, height);

            static InlineImage ShadowImage(string? importedColor)
            {
                return new InlineImage(OnePixelPng(), 160, 120)
                {
                    ShadowPreset = 1,
                    ImportedEffects = importedColor is not null
                        ? new ShapeEffectLst
                        {
                            HasShadow = true,
                            ShadowAlpha = 100000,
                            ShadowColorHex = importedColor,
                        }
                        : null,
                };
            }

            var red = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage("FF0000"));
            var preset = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage(null));

            static IEnumerable<(byte B, byte G, byte R, byte A)> OutsidePixels(
                AvaloniaPictureEffectRaster raster)
            {
                for (var y = 0; y < raster.Height; y++)
                for (var x = 0; x < raster.Width; x++)
                {
                    var inSource = x >= raster.SourcePixelRect.X &&
                                   x < raster.SourcePixelRect.Right &&
                                   y >= raster.SourcePixelRect.Y &&
                                   y < raster.SourcePixelRect.Bottom;
                    if (inSource)
                        continue;

                    var offset = y * raster.Stride + x * 4;
                    yield return (
                        raster.Pixels[offset],
                        raster.Pixels[offset + 1],
                        raster.Pixels[offset + 2],
                        raster.Pixels[offset + 3]);
                }
            }

            OutsidePixels(red).Should().Contain(pixel =>
                pixel.R > 0 && pixel.G == 0 && pixel.B == 0 && pixel.A > 0);
            OutsidePixels(preset).Where(pixel => pixel.A > 0)
                .Should().OnlyContain(pixel => pixel.R == 0 && pixel.G == 0 && pixel.B == 0);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImportedShadowDirection_ControlsExpandedRasterRegistration()
    {
        await Session.Dispatch(() =>
        {
            const int width = 16;
            const int height = 12;
            var source = OpaqueRaster(width, height);

            static InlineImage ShadowImage(int direction)
            {
                return new InlineImage(OnePixelPng(), 16, 12)
                {
                    ShadowPreset = 1,
                    ImportedEffects = new ShapeEffectLst
                    {
                        HasShadow = true,
                        ShadowBlurRad = 0,
                        ShadowDist = 5 * 12700,
                        ShadowDir = direction * 60000,
                        ShadowAlpha = 100000,
                    },
                };
            }

            var east = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage(0));
            var west = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage(180));
            var north = AvaloniaImageAdjustHelper.ApplyPictureEffectRaster(
                source, width, height, width * 4, ShadowImage(90));

            west.SourcePixelRect.X.Should().BeGreaterThan(east.SourcePixelRect.X);
            north.SourcePixelRect.Y.Should().BeGreaterThan(east.SourcePixelRect.Y);
            east.SourcePixelRect.Width.Should().Be(width);
            east.SourcePixelRect.Height.Should().Be(height);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExpandedEffectCacheFeedsInlineAndFloatingDrawRectsWithoutChangingSourceGeometry()
    {
        await Session.Dispatch(() =>
        {
            var inlineImage = new InlineImage(OpaquePng(), 120, 80)
            {
                Wrapping = ImageWrapping.Inline,
                ShadowPreset = 1,
            };
            var inlineDoc = TextDocument.CreateEmpty();
            inlineDoc.Blocks.Clear();
            var inlineParagraph = new Paragraph();
            inlineParagraph.Runs.Add(Run.FromImage(inlineImage));
            inlineDoc.Blocks.Add(inlineParagraph);

            var inlineView = new DocumentView();
            inlineView.LoadDocument(inlineDoc);
            inlineView.Measure(new Size(800, 1200));
            var inlineRendered = inlineView.DecodeRenderedImage(inlineImage);
            inlineRendered.Should().NotBeNull();
            inlineView.DecodeRenderedImage(inlineImage).Should().BeSameAs(inlineRendered);
            AssertVisualRectPreservesSource(inlineView.InlineImageVisualRects.Single(), inlineRendered!);

            var floatingImage = new InlineImage(OpaquePng(), 120, 80)
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalOffsetPt = 12,
                VerticalOffsetPt = 12,
                ShadowPreset = 1,
            };
            var floatingDoc = TextDocument.CreateEmpty();
            floatingDoc.Blocks.Clear();
            var floatingParagraph = new Paragraph();
            floatingParagraph.Runs.Add(Run.FromImage(floatingImage));
            floatingDoc.Blocks.Add(floatingParagraph);

            var floatingView = new DocumentView();
            floatingView.LoadDocument(floatingDoc);
            floatingView.Measure(new Size(800, 1200));
            var floatingRendered = floatingView.DecodeRenderedImage(floatingImage);
            floatingRendered.Should().NotBeNull();
            AssertVisualRectPreservesSource(floatingView.FloatingImageVisualRects.Single(), floatingRendered!);
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

    private static byte[] OpaqueRaster(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            pixels[offset] = (byte)(20 + x);
            pixels[offset + 1] = (byte)(80 + y);
            pixels[offset + 2] = (byte)(180 + (x + y) % 40);
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private static byte[] ColoredRaster(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            pixels[offset] = (byte)(30 + x * 8);
            pixels[offset + 1] = (byte)(40 + y * 9);
            pixels[offset + 2] = (byte)(90 + x * 5 + y * 3);
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private static void AssertVisualRectPreservesSource(
        (Rect SourceRect, Rect VisualRect) pair,
        AvaloniaRenderedImage rendered)
    {
        pair.VisualRect.Width.Should().BeGreaterThan(pair.SourceRect.Width);
        pair.VisualRect.Height.Should().BeGreaterThan(pair.SourceRect.Height);
        var scaleX = pair.VisualRect.Width / rendered.Bitmap.PixelSize.Width;
        var scaleY = pair.VisualRect.Height / rendered.Bitmap.PixelSize.Height;
        (pair.VisualRect.X + rendered.SourcePixelRect.X * scaleX)
            .Should().BeApproximately(pair.SourceRect.X, 0.001);
        (pair.VisualRect.Y + rendered.SourcePixelRect.Y * scaleY)
            .Should().BeApproximately(pair.SourceRect.Y, 0.001);
        (rendered.SourcePixelRect.Width * scaleX)
            .Should().BeApproximately(pair.SourceRect.Width, 0.001);
        (rendered.SourcePixelRect.Height * scaleY)
            .Should().BeApproximately(pair.SourceRect.Height, 0.001);
    }

    private static byte[] OpaquePng()
    {
        using var bitmap = new SKBitmap(8, 6, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(new SKColor(230, 120, 40, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] WpfPencilSketchExpected(byte[] pixels, int width, int height, int stride)
    {
        var edges = ReferenceSobel(pixels, width, height, stride);
        var result = new byte[pixels.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            var t = 1.0 - edges[i / 4] / 255.0;
            var b = pixels[i] / 255.0;
            var g = pixels[i + 1] / 255.0;
            var r = pixels[i + 2] / 255.0;
            var br = Math.Clamp(t + b * (1 - t), 0, 1);
            var gr = Math.Clamp(t + g * (1 - t), 0, 1);
            var rr = Math.Clamp(t + r * (1 - t), 0, 1);
            var lum = 0.2126 * rr + 0.7152 * gr + 0.0722 * br;
            result[i] = ToByte(lum + (br - lum) * 1.6);
            result[i + 1] = ToByte(lum + (gr - lum) * 1.6);
            result[i + 2] = ToByte(lum + (rr - lum) * 1.6);
            result[i + 3] = pixels[i + 3];
        }
        return result;
    }

    private static byte[] ReferenceSobel(byte[] pixels, int width, int height, int stride)
    {
        var grey = new byte[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = y * stride + x * 4;
            grey[y * width + x] = (byte)(0.2126 * pixels[i + 2] + 0.7152 * pixels[i + 1] +
                                         0.0722 * pixels[i] + 0.5);
        }

        var edges = new byte[width * height];
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            int P(int dx, int dy) => grey[(y + dy) * width + x + dx];
            var gx = -P(-1, -1) - 2 * P(0, -1) - P(1, -1) + P(-1, 1) + 2 * P(0, 1) + P(1, 1);
            var gy = -P(-1, -1) - 2 * P(-1, 0) - P(-1, 1) + P(1, -1) + 2 * P(1, 0) + P(1, 1);
            edges[y * width + x] = (byte)Math.Min(255, Math.Sqrt(gx * (long)gx + gy * (long)gy));
        }
        return edges;
    }

    private static byte ToByte(double value) => (byte)(Math.Clamp(value, 0, 1) * 255 + 0.5);

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
