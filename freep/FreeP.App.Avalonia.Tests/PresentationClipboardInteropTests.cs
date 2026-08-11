using Avalonia.Headless;
using Avalonia.Input;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Free.Shared.Drawing;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class PresentationClipboardInteropTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");

    [Fact]
    public void Avalonia12Win32Backend_LegacyApplicationPrefixIsProvenButNotRequired()
    {
        var assembly = Assembly.Load("Avalonia.Win32");
        var productVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;
        var registryType = assembly.GetType(
            "Avalonia.Win32.ClipboardFormatRegistry",
            throwOnError: true)!;
        var prefix = registryType
            .GetField("AppPrefix", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetRawConstantValue();

        productVersion.Should().StartWith("12.0.4");
        prefix.Should().Be("avn-app-fmt:");
        AvaloniaPresentationSystemClipboard.SelectionFormat
            .ToSystemName((string)prefix!)
            .Should().Be("avn-app-fmt:" + PresentationClipboardFormats.Selection);
        AvaloniaPresentationSystemClipboard.OwnerTokenFormat
            .ToSystemName((string)prefix!)
            .Should().Be("avn-app-fmt:" + PresentationClipboardFormats.OwnerToken);
        AvaloniaPresentationSystemClipboard.SelectionPlatformFormat
            .ToSystemName("ignored-prefix:")
            .Should().Be(PresentationClipboardFormats.Selection);
        AvaloniaPresentationSystemClipboard.OwnerTokenPlatformFormat
            .ToSystemName("ignored-prefix:")
            .Should().Be(PresentationClipboardFormats.OwnerToken);
    }

    [Fact]
    public async Task RichEffectPayload_SurvivesAvaloniaApplicationAndPlatformFormats()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run
                    {
                        Text = "glow",
                        TextGlow = new RunTextGlow
                        {
                            Color = new ThemeAwareColor(SrgbColor.FromRgb(0xF0C000)),
                            Alpha = 0x80,
                            RadiusPt = 4.5,
                        },
                    },
                },
            });
            var payload = InCanvasRichClipboardPlanner.Capture(
                body,
                new InCanvasEditorTextSelection(0, 4));
            using var transfer = AvaloniaPresentationSystemClipboard.BuildDataTransfer(
                new PresentationClipboardContent(
                    RichTextBytes: InCanvasRichClipboardPlanner.Serialize(payload)),
                out var bitmap);

            bitmap.Should().BeNull();
            var content = await AvaloniaPresentationSystemClipboard.ReadDataTransferAsync(transfer);
            var decoded = InCanvasRichClipboardPlanner.Deserialize(content.RichTextBytes);

            decoded.Should().NotBeNull();
            decoded!.Body.Paragraphs.Single().Runs.Single().TextGlow!.RadiusPt
                .Should().BeApproximately(4.5, 0.0001);
        }, CancellationToken.None);
    }

    [Fact]
    public void Avalonia12Win32Backend_WritesWpfCompatibleNativePayloads()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var content = new PresentationClipboardContent(
            SelectionBytes: [0x50, 0x4B, 0x03, 0x04, 0x44, 0x55],
            OwnerToken: "avalonia-owner");
        using var transfer = AvaloniaPresentationSystemClipboard.BuildDataTransfer(
            content,
            out var bitmap);
        bitmap.Should().BeNull();

        ReadAvaloniaWin32HGlobal(
                transfer,
                AvaloniaPresentationSystemClipboard.SelectionPlatformFormat)
            .Should().Equal(content.SelectionBytes!);

        var ownerBytes = Encoding.Unicode.GetBytes("avalonia-owner\0");
        ReadAvaloniaWin32HGlobal(
                transfer,
                AvaloniaPresentationSystemClipboard.OwnerTokenPlatformFormat)
            .Should().Equal(ownerBytes);
    }

    [Fact]
    public async Task Copy_exports_native_image_text_and_keeps_internal_fidelity()
    {
        var clipboard = new FakeSystemClipboard();
        var editor = CreateEditorWithSelectedShape(out var source);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var written = await service.CopyAsync(editor);

        written.Should().BeTrue();
        editor.CanPaste.Should().BeTrue();
        clipboard.LastWritten.Should().NotBeNull();
        clipboard.LastWritten!.HasSelection.Should().BeTrue();
        clipboard.LastWritten.HasImage.Should().BeTrue();
        clipboard.LastWritten.Text.Should().Be("Clipboard text");
        clipboard.LastWritten.OwnerToken.Should().NotBeNullOrWhiteSpace();

        var decoded = PresentationClipboardSelectionCodec.Deserialize(
            clipboard.LastWritten.SelectionBytes!);
        decoded.Should().ContainSingle();
        decoded[0].Kind.Should().Be(source.Kind);
        decoded[0].Fill.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x336699));
        decoded[0].TextBody!.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
    }

    [Fact]
    public void Copy_groupedChild_ExportsTheSelectedDescendant()
    {
        var editor = CreateEmptyEditor();
        var child = new SlideShape
        {
            Id = 72,
            Name = "Grouped clipboard child",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            TextBody = BuildTextBody("Grouped child"),
        };
        var group = new SlideShape { Id = 71, Kind = SlideShapeKind.Group };
        group.Children.Add(child);
        editor.CurrentSlide!.Shapes.Add(group);
        editor.Select(child.Id);

        var content = PresentationClipboardContentFactory.CreateSelection(
            editor,
            static (_, _, _) => [],
            "test-owner");

        content.Should().NotBeNull();
        var decoded = PresentationClipboardSelectionCodec.Deserialize(content!.SelectionBytes!);
        decoded.Should().ContainSingle();
        decoded[0].Id.Should().Be(child.Id);
        decoded[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Grouped child");
    }

    [Fact]
    public async Task Avalonia_data_transfer_maps_custom_bitmap_and_text_formats()
    {
        await Session.Dispatch(async () =>
        {
            var content = new PresentationClipboardContent(
                SelectionBytes: [4, 5, 6],
                PngBytes: Png,
                Text: "portable text",
                OwnerToken: "owner-token",
                RichTextBytes: [10, 11, 12]);
            var transfer = AvaloniaPresentationSystemClipboard.BuildDataTransfer(
                content,
                out var bitmap);
            try
            {
                var expectedSelectionFormat = OperatingSystem.IsWindows()
                    ? AvaloniaPresentationSystemClipboard.SelectionPlatformFormat
                    : AvaloniaPresentationSystemClipboard.SelectionFormat;
                var expectedOwnerTokenFormat = OperatingSystem.IsWindows()
                    ? AvaloniaPresentationSystemClipboard.OwnerTokenPlatformFormat
                    : AvaloniaPresentationSystemClipboard.OwnerTokenFormat;
                transfer.Formats.Should().Contain(expectedSelectionFormat);
                transfer.Formats.Should().Contain(expectedOwnerTokenFormat);
                transfer.Formats.Should().Contain(
                    OperatingSystem.IsWindows()
                        ? AvaloniaPresentationSystemClipboard.RichTextPlatformFormat
                        : AvaloniaPresentationSystemClipboard.RichTextFormat);
                AvaloniaPresentationSystemClipboard.SelectionFormat
                    .ToSystemName("avn-app-fmt:")
                    .Should().Be("avn-app-fmt:" + PresentationClipboardFormats.Selection);
                AvaloniaPresentationSystemClipboard.OwnerTokenFormat
                    .ToSystemName("avn-app-fmt:")
                    .Should().Be("avn-app-fmt:" + PresentationClipboardFormats.OwnerToken);
                transfer.Formats.Should().Contain(DataFormat.Bitmap);
                transfer.Formats.Should().Contain(DataFormat.Text);

                var roundTrip = await AvaloniaPresentationSystemClipboard.ReadDataTransferAsync(transfer);
                roundTrip.SelectionBytes.Should().Equal(4, 5, 6);
                roundTrip.PngBytes.Should().NotBeNullOrEmpty();
                roundTrip.Text.Should().Be("portable text");
                roundTrip.OwnerToken.Should().Be("owner-token");
                roundTrip.RichTextBytes.Should().Equal(10, 11, 12);
            }
            finally
            {
                bitmap?.Dispose();
                ((IDisposable)transfer).Dispose();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_data_transfer_reads_public_platform_formats_without_private_prefix()
    {
        await Session.Dispatch(async () =>
        {
            var item = new DataTransferItem();
            item.Set(AvaloniaPresentationSystemClipboard.SelectionPlatformFormat, [9, 8, 7]);
            item.Set(AvaloniaPresentationSystemClipboard.OwnerTokenPlatformFormat, "wpf-owner");
            item.Set(AvaloniaPresentationSystemClipboard.RichTextPlatformFormat, [1, 2, 3]);
            item.SetText("WPF fallback");
            using var transfer = new DataTransfer();
            transfer.Add(item);

            var content = await AvaloniaPresentationSystemClipboard.ReadDataTransferAsync(transfer);

            content.SelectionBytes.Should().Equal(9, 8, 7);
            content.OwnerToken.Should().Be("wpf-owner");
            content.Text.Should().Be("WPF fallback");
            content.RichTextBytes.Should().Equal(1, 2, 3);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_data_transfer_reads_external_rtf_platform_format()
    {
        await Session.Dispatch(async () =>
        {
            var rtf = Encoding.ASCII.GetBytes(@"{\rtf1\ansi Native RTF}");
            var item = new DataTransferItem();
            item.Set(
                OperatingSystem.IsWindows()
                    ? AvaloniaPresentationSystemClipboard.ExternalRtfWindowsFormat
                    : AvaloniaPresentationSystemClipboard.ExternalRtfLinuxFormat,
                rtf);
            using var transfer = new DataTransfer();
            transfer.Add(item);

            var content = await AvaloniaPresentationSystemClipboard.ReadDataTransferAsync(transfer);

            content.RtfBytes.Should().Equal(rtf);
            content.HasRichText.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_data_transfer_round_trips_wpf_xamlpackage_platform_format()
    {
        await Session.Dispatch(async () =>
        {
            var xamlPackage = Encoding.UTF8.GetBytes("wpf-xamlpackage");
            var transfer = AvaloniaPresentationSystemClipboard.BuildDataTransfer(
                new PresentationClipboardContent(XamlPackageBytes: xamlPackage),
                out var bitmap);
            try
            {
                bitmap.Should().BeNull();
                transfer.Formats.Should().Contain(
                    OperatingSystem.IsWindows()
                        ? AvaloniaPresentationSystemClipboard.ExternalXamlPackageWindowsFormat
                        : AvaloniaPresentationSystemClipboard.ExternalXamlPackageLinuxFormat);

                var content = await AvaloniaPresentationSystemClipboard.ReadDataTransferAsync(transfer);

                content.XamlPackageBytes.Should().Equal(xamlPackage);
                content.HasXamlPackage.Should().BeTrue();
            }
            finally
            {
                ((IDisposable)transfer).Dispose();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_data_transfer_falls_back_when_public_platform_format_cannot_be_read()
    {
        using var transfer = new ThrowingPlatformAliasTransfer();

        var content = await AvaloniaPresentationSystemClipboard.ReadDataTransferAsync(transfer);

        content.SelectionBytes.Should().Equal(6, 5, 4);
        content.OwnerToken.Should().Be("legacy-owner");
    }

    [Fact]
    public async Task Avalonia_data_transfer_keeps_other_formats_when_one_format_fails()
    {
        await Session.Dispatch(async () =>
        {
            var content = new PresentationClipboardContent(
                SelectionBytes: [7, 8],
                PngBytes: [1, 2, 3],
                Text: "surviving text",
                OwnerToken: "surviving-owner");
            var transfer = AvaloniaPresentationSystemClipboard.BuildDataTransfer(
                content,
                out var bitmap);
            try
            {
                bitmap.Should().BeNull();
                transfer.Formats.Should().NotContain(DataFormat.Bitmap);
                transfer.Formats.Should().Contain(DataFormat.Text);
                transfer.Formats.Should().Contain(
                    OperatingSystem.IsWindows()
                        ? AvaloniaPresentationSystemClipboard.SelectionPlatformFormat
                        : AvaloniaPresentationSystemClipboard.SelectionFormat);

                var roundTrip = await AvaloniaPresentationSystemClipboard.ReadDataTransferAsync(transfer);
                roundTrip.SelectionBytes.Should().Equal(7, 8);
                roundTrip.Text.Should().Be("surviving text");
                roundTrip.OwnerToken.Should().Be("surviving-owner");
            }
            finally
            {
                ((IDisposable)transfer).Dispose();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Cut_exports_before_deleting_and_leaves_internal_shape_pasteable()
    {
        var clipboard = new FakeSystemClipboard();
        var editor = CreateEditorWithSelectedShape(out var source);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());
        var sourceExistedDuringWrite = false;
        clipboard.BeforeWrite = () =>
        {
            sourceExistedDuringWrite = editor.CurrentSlide!.Shapes.Any(shape => shape.Id == source.Id);
            editor.SelectedShapeIds.Should().Contain(source.Id);
        };

        var written = await service.CutAsync(editor);

        written.Should().BeTrue();
        sourceExistedDuringWrite.Should().BeTrue();
        editor.CurrentSlide!.Shapes.Should().NotContain(shape => shape.Id == source.Id);
        editor.CanPaste.Should().BeTrue();
        clipboard.LastWritten!.Text.Should().Be("Clipboard text");
        PresentationClipboardSelectionCodec.Deserialize(clipboard.LastWritten.SelectionBytes!)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task External_native_selection_precedes_image_and_text()
    {
        var sourceEditor = CreateEditorWithSelectedShape(out var source);
        var selected = new[] { source };
        var native = PresentationClipboardSelectionCodec.Serialize(
            sourceEditor.Presentation,
            sourceEditor.CurrentSlide!,
            selected);
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(native, Png, "fallback text", "external"),
        };
        var destination = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var result = await service.PasteAsync(destination);

        result.Should().Be(PresentationClipboardPasteSource.NativeSelection);
        destination.CurrentSlide!.Shapes.Should().ContainSingle();
        destination.CurrentSlide.Shapes[0].Kind.Should().Be(SlideShapeKind.AutoShape);
        destination.CurrentSlide.Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("Clipboard text");
    }

    [Fact]
    public async Task Invalid_native_selection_falls_back_to_image_before_text()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent([1, 2, 3], Png, "fallback text", "external"),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.Image);
        editor.CurrentSlide!.Shapes.Should().ContainSingle();
        editor.CurrentSlide.Shapes[0].Kind.Should().Be(SlideShapeKind.Picture);
    }

    [Fact]
    public async Task Text_is_used_when_native_and_image_are_unavailable()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(Text: "external text"),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.Text);
        editor.CurrentSlide!.Shapes.Should().ContainSingle();
        editor.CurrentSlide.Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("external text");
    }

    [Fact]
    public async Task RichText_is_used_before_xaml_package_and_plain_text()
    {
        var body = BuildTextBody("Rich Avalonia paste");
        body.Paragraphs.Single().Runs.Single().Bold = true;
        body.Paragraphs.Single().Runs.Single().BoldSet = true;
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                Text: "plain fallback",
                RichTextBytes: InCanvasRichClipboardPlanner.Serialize(
                    new InCanvasRichClipboardPayload(body, "Rich Avalonia paste")),
                XamlPackageBytes: CreateXamlPackage(
                    "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph>ignored</Paragraph></FlowDocument>")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.RichText);
        var run = editor.CurrentSlide!.Shapes.Single().TextBody!.Paragraphs.Single().Runs.Single();
        run.Text.Should().Be("Rich Avalonia paste");
        run.Bold.Should().BeTrue();
    }

    [Fact]
    public async Task External_Rtf_is_pasted_as_formatted_text_box()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                RtfBytes: Encoding.ASCII.GetBytes(
                    @"{\rtf1\ansi{\fonttbl{\f0 Calibri;}}\pard\f0\fs24 Before \b bold\b0\par After}")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.RichText);
        var body = editor.CurrentSlide!.Shapes.Single().TextBody!;
        body.Paragraphs.Should().HaveCount(2);
        body.Paragraphs[0].Runs.Single(run => run.Text == "bold").Bold.Should().BeTrue();
        body.Paragraphs[1].Runs.Single().Text.Should().Be("After");
    }

    [Fact]
    public async Task External_Rtf_picture_is_pasted_as_picture_and_text_box()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                RtfBytes: Encoding.ASCII.GetBytes(
                    @"{\rtf1\ansi Caption {\pict\pngblip " + Convert.ToHexString(Png) + @"} After}")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.RichText);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);
        editor.CurrentSlide.Shapes[0].Kind.Should().Be(SlideShapeKind.Picture);
        editor.CurrentSlide.Shapes[0].Picture!.Bytes.Should().Equal(Png);
        editor.CurrentSlide.Shapes[0].Picture!.ContentType.Should().Be("image/png");
        editor.CurrentSlide.Shapes[1].TextBody!.Paragraphs.Single().Runs
            .Should().Contain(run => run.Text.Contains("Caption ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task External_Rtf_picture_preserves_display_dimensions()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                RtfBytes: Encoding.ASCII.GetBytes(
                    @"{\rtf1\ansi{\pict\pngblip\picwgoal1440\pichgoal720 "
                    + Convert.ToHexString(Png) + "}}")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.RichText);
        var picture = editor.CurrentSlide!.Shapes.Single();
        picture.ExtentCxEmu.Should().Be(914_400);
        picture.ExtentCyEmu.Should().Be(457_200);
    }

    [Fact]
    public async Task External_Rtf_multiple_pictures_are_all_pasted_as_editable_shapes()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                RtfBytes: Encoding.ASCII.GetBytes(
                    @"{\rtf1\ansi Before {\pict\pngblip " + Convert.ToHexString(Png)
                    + @"} middle {\pict\jpegblip " + Convert.ToHexString(jpeg) + @"} After}")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.RichText);
        editor.CurrentSlide!.Shapes.Should().HaveCount(3);
        editor.CurrentSlide.Shapes[0].Picture!.Bytes.Should().Equal(Png);
        editor.CurrentSlide.Shapes[1].Picture!.Bytes.Should().Equal(jpeg);
        editor.CurrentSlide.Shapes[1].Picture!.ContentType.Should().Be("image/jpeg");
        editor.CurrentSlide.Shapes[2].TextBody!.Paragraphs.Single().Runs
            .Select(run => run.Text)
            .Should().Contain(text => text.Contains("Before ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task External_Rtf_object_is_pasted_as_editable_ole_shape_with_result_text()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                RtfBytes: Encoding.ASCII.GetBytes(
                    @"{\rtf1\ansi Before {\object{\*\objclass Word.Document.12}{\*\objdata 010203}{\objresult Embedded result}} After}")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.RichText);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);
        var objectShape = editor.CurrentSlide.Shapes[0];
        objectShape.Kind.Should().Be(SlideShapeKind.Ole);
        objectShape.OleObject!.EmbeddedBytes.Should().Equal(0x01, 0x02, 0x03);
        objectShape.OleObject.EmbeddedExtension.Should().Be("docx");
        objectShape.OleObject.ProgId.Should().Be("Word.Document.12");
        editor.CurrentSlide.Shapes[1].TextBody!.Paragraphs.Single().Runs
            .Select(run => run.Text)
            .Should().ContainSingle()
            .Which.Should().Be("Before Embedded result After");
    }

    [Fact]
    public async Task XamlPackage_table_is_pasted_as_native_editable_table()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                Text: "plain fallback",
                XamlPackageBytes: CreateXamlPackage("""
                    <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                      <Table><TableRowGroup><TableRow>
                        <TableCell Background="#FFF2F2F2" Padding="4,2,6,8"
                                   BorderBrush="#FF1F4E79" BorderThickness="1,2,3,4"
                                   VerticalContentAlignment="Center"><Paragraph><Hyperlink NavigateUri="https://example.test/q1"><Italic>Q1</Italic></Hyperlink></Paragraph></TableCell>
                        <TableCell><Paragraph>42</Paragraph></TableCell>
                      </TableRow></TableRowGroup></Table>
                    </FlowDocument>
                    """)),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.XamlPackage);
        var shape = editor.CurrentSlide!.Shapes.Single();
        shape.Kind.Should().Be(SlideShapeKind.Table);
        shape.Table.Should().NotBeNull();
        shape.Table!.Rows.Should().ContainSingle();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs.Single().Runs
            .Single().Text.Should().Be("Q1");
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs.Single().Runs
            .Single().Hyperlink!.Url.Should().Be("https://example.test/q1");
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs.Single().Runs
            .Single().Italic.Should().BeTrue();
        var firstCell = shape.Table.Rows[0].Cells[0];
        firstCell.Fill.Should().BeOfType<ShapeFill.Solid>().Which.Color.Resolved
            .Should().Be(SrgbColor.FromRgb(0xF2F2F2));
        firstCell.Anchor.Should().Be(TableCellAnchor.Middle);
        firstCell.InsetLeftPt.Should().Be(3);
        firstCell.InsetBottomPt.Should().Be(6);
        firstCell.Borders!.Left.Should().BeOfType<ShapeOutline.Visible>().Which.WidthPt.Should().Be(0.75);
        firstCell.Borders.Bottom.Should().BeOfType<ShapeOutline.Visible>().Which.WidthPt.Should().Be(3);
        shape.Table.Rows[0].Cells[1].TextBody!.Paragraphs.Single().Runs
            .Single().Text.Should().Be("42");
    }

    [Fact]
    public async Task XamlPackage_list_preserves_numbering_style()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                XamlPackageBytes: CreateXamlPackage("""
                    <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                  xmlns:sys="clr-namespace:System;assembly=mscorlib">
                      <FlowDocument.Resources>
                        <ResourceDictionary>
                          <SolidColorBrush x:Key="Accent" Color="#FF2F5597" />
                          <FontFamily x:Key="BodyFont">Aptos</FontFamily>
                          <sys:Double x:Key="BodySize">18</sys:Double>
                          <Style x:Key="ListBase">
                            <Setter Property="Foreground" Value="{StaticResource Accent}" />
                            <Setter Property="FontFamily" Value="{DynamicResource BodyFont}" />
                            <Setter Property="FontSize" Value="{StaticResource BodySize}" />
                          </Style>
                          <Style x:Key="ListText" BasedOn="{StaticResource ListBase}">
                            <Setter Property="FontWeight" Value="Bold" />
                          </Style>
                        </ResourceDictionary>
                      </FlowDocument.Resources>
                      <List MarkerStyle="UpperLatin" StartIndex="4">
                        <ListItem><Paragraph Style="{StaticResource ListText}">Four</Paragraph></ListItem>
                        <ListItem><Paragraph>Five</Paragraph></ListItem>
                      </List>
                    </FlowDocument>
                    """)),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.XamlPackage);
        var body = editor.CurrentSlide!.Shapes.Single().TextBody!;
        body.Paragraphs.Should().HaveCount(2);
        body.Paragraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        body.Paragraphs[0].AutoNumType.Should().Be(AutoNumType.AlphaUcPeriod);
        body.Paragraphs[0].AutoNumStartAt.Should().Be(4);
        body.Paragraphs[0].Runs.Single().Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5597));
        body.Paragraphs[0].Runs.Single().FontFamily.Should().Be("Aptos");
        body.Paragraphs[0].Runs.Single().FontSizePt.Should().Be(13.5);
        body.Paragraphs[0].Runs.Single().Bold.Should().BeTrue();
        body.Paragraphs[1].AutoNumStartAtSpecified.Should().BeFalse();
    }

    [Fact]
    public async Task XamlPackage_preserves_baseline_alignment()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                XamlPackageBytes: CreateXamlPackage("""
                    <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                      <Paragraph>
                        <Run Text="base" />
                        <Run BaselineAlignment="Superscript" Text="up" />
                        <Run BaselineAlignment="Subscript" Text="down" />
                      </Paragraph>
                    </FlowDocument>
                    """)),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.XamlPackage);

        editor.CurrentSlide!.Shapes.Single().TextBody!.Paragraphs.Single().Runs
            .Select(run => run.BaselineOffset)
            .Should().Equal(null, 10_000, -10_000);
    }

    [Fact]
    public async Task XamlPackage_preserves_flow_direction()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                XamlPackageBytes: CreateXamlPackage(
                    "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" FlowDirection=\"RightToLeft\"><Paragraph><Run Text=\"אבג\"/><Run FlowDirection=\"LeftToRight\" Text=\"LTR\"/></Paragraph></FlowDocument>")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.XamlPackage);

        var paragraph = editor.CurrentSlide!.Shapes.Single().TextBody!.Paragraphs.Single();
        paragraph.RightToLeft.Should().BeTrue();
        paragraph.Runs.Select(run => run.RightToLeft).Should().Equal(true, false);
    }

    [Fact]
    public async Task XamlPackage_preserves_text_alignment()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                XamlPackageBytes: CreateXamlPackage(
                    "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextAlignment=\"Center\"><Paragraph>centered</Paragraph><Paragraph TextAlignment=\"Right\">right</Paragraph></FlowDocument>")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.XamlPackage);

        editor.CurrentSlide!.Shapes.Single().TextBody!.Paragraphs.Select(paragraph => paragraph.Align)
            .Should().Equal(TextAlign.Center, TextAlign.Right);
    }

    [Fact]
    public async Task External_Rtf_table_preserves_solid_cell_style()
    {
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                RtfBytes: Encoding.ASCII.GetBytes(
                    @"{\rtf1\ansi
{\colortbl;\red255\green255\blue0;\red31\green78\blue121;}
\trowd\clcbpat1\clvertalc\clpadl120\clpadr240\clpadt60\clpadb180\clbrdrl\brdrs\brdrw10\brdrcf2\cellx1440\cellx2880
Header\cell Value\cell\row}")),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.RichText);
        var cell = editor.CurrentSlide!.Shapes.Single().Table!.Rows.Single().Cells[0];
        ((ShapeFill.Solid)cell.Fill!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xFFFF00));
        ((ShapeOutline.Visible)cell.Borders!.Left!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        cell.Anchor.Should().Be(TableCellAnchor.Middle);
        cell.InsetLeftPt.Should().Be(6);
        cell.InsetRightPt.Should().Be(12);
    }

    [Fact]
    public async Task XamlPackage_image_is_pasted_as_picture()
    {
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                XamlPackageBytes: CreateXamlPackage(
                    """
                    <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                      <BlockUIContainer><Image Source="Images/pasted.png" Width="96" Height="48" /></BlockUIContainer>
                    </FlowDocument>
                    """,
                    ("Images/pasted.png", imageBytes))),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.XamlPackage);
        var picture = editor.CurrentSlide!.Shapes.Single();
        picture.Kind.Should().Be(SlideShapeKind.Picture);
        picture.Picture!.ContentType.Should().Be("image/png");
        picture.Picture.Bytes.Should().Equal(imageBytes);
        picture.ExtentCxEmu.Should().Be(914400);
        picture.ExtentCyEmu.Should().Be(457200);
    }

    [Fact]
    public async Task XamlPackage_images_are_pasted_in_document_order()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <BlockUIContainer><Image Source="Images/first.png" /></BlockUIContainer>
              <BlockUIContainer><Image Source="Images/second.jpg" /></BlockUIContainer>
            </FlowDocument>
            """;
        var first = new byte[] { 0x01, 0x02 };
        var second = new byte[] { 0x03, 0x04, 0x05 };
        var clipboard = new FakeSystemClipboard
        {
            Content = new PresentationClipboardContent(
                XamlPackageBytes: CreateXamlPackage(
                    xaml,
                    ("Images/first.png", first),
                    ("Images/second.jpg", second))),
        };
        var editor = CreateEmptyEditor();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.XamlPackage);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);
        editor.CurrentSlide.Shapes[0].Picture!.Bytes.Should().Equal(first);
        editor.CurrentSlide.Shapes[0].Picture!.ContentType.Should().Be("image/png");
        editor.CurrentSlide.Shapes[1].Picture!.Bytes.Should().Equal(second);
        editor.CurrentSlide.Shapes[1].Picture!.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Own_copy_prefers_internal_editable_shape_over_exported_fallbacks()
    {
        var clipboard = new FakeSystemClipboard();
        var editor = CreateEditorWithSelectedShape(out var source);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());
        await service.CopyAsync(editor);

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.Internal);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);
        editor.CurrentSlide.Shapes.Last().Kind.Should().Be(source.Kind);
        editor.CurrentSlide.Shapes.Last().Kind.Should().NotBe(SlideShapeKind.Picture);
    }

    [Fact]
    public async Task Reused_owner_token_with_changed_native_content_does_not_use_internal_fallback()
    {
        var clipboard = new FakeSystemClipboard();
        var editor = CreateEditorWithSelectedShape(out _);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        await service.CopyAsync(editor);
        var ownerToken = clipboard.LastWritten!.OwnerToken;
        clipboard.Content = new PresentationClipboardContent(
            PngBytes: Png,
            Text: "changed native content",
            OwnerToken: ownerToken);

        var result = await service.PasteAsync(editor);

        result.Should().Be(PresentationClipboardPasteSource.Image);
        editor.CurrentSlide!.Shapes.Last().Kind.Should().Be(SlideShapeKind.Picture);
    }

    [Fact]
    public async Task Failed_write_invalidates_reused_owner_token_currentness()
    {
        var clipboard = new FakeSystemClipboard();
        var editor = CreateEditorWithSelectedShape(out _);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        await service.CopyAsync(editor);
        clipboard.Content = new PresentationClipboardContent(
            Text: "external after failed write",
            OwnerToken: clipboard.LastWritten!.OwnerToken);
        clipboard.ThrowOnWrite = true;

        (await service.CopyAsync(editor)).Should().BeFalse();
        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.Text);
    }

    [Fact]
    public async Task Failed_write_records_the_failure_message_for_callers_to_surface()
    {
        var clipboard = new FakeSystemClipboard { ThrowOnWrite = true };
        var editor = CreateEditorWithSelectedShape(out _);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.CopyAsync(editor)).Should().BeFalse();

        service.LastWriteFailureMessage.Should().Be("clipboard locked");
    }

    [Fact]
    public async Task Successful_write_clears_any_previously_recorded_failure_message()
    {
        var clipboard = new FakeSystemClipboard { ThrowOnWrite = true };
        var editor = CreateEditorWithSelectedShape(out _);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());
        (await service.CopyAsync(editor)).Should().BeFalse();
        service.LastWriteFailureMessage.Should().NotBeNull();

        clipboard.ThrowOnWrite = false;
        (await service.CopyAsync(editor)).Should().BeTrue();

        service.LastWriteFailureMessage.Should().BeNull();
    }

    [Fact]
    public async Task Copy_with_nothing_selected_does_not_resurface_a_stale_write_failure()
    {
        // Sibling no-regression for the null-Content early-return path: a prior failed write must not
        // leak its error message onto an unrelated later copy that has nothing selected.
        var clipboard = new FakeSystemClipboard { ThrowOnWrite = true };
        var editor = CreateEditorWithSelectedShape(out _);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());
        (await service.CopyAsync(editor)).Should().BeFalse();
        service.LastWriteFailureMessage.Should().NotBeNull();

        editor.ClearSelection();
        (await service.CopyAsync(editor)).Should().BeFalse();

        service.LastWriteFailureMessage.Should().BeNull();
    }

    [Fact]
    public async Task Ribbon_copy_write_failure_reaches_the_status_bar()
    {
        // End-to-end reproduction of the silent-failure finding: Copy used to swallow the OS-clipboard
        // write exception entirely, so the user saw nothing and believed the copy succeeded.
        //
        // NOTE: assertions must NOT run inside a Session.Dispatch(Func<Task>, ...) delegate — an
        // exception raised there (after the delegate's first await) is not observed by the awaiting
        // test method, so a broken assertion would silently report the test as passing. Only the
        // synchronous Action overload propagates reliably, so UI-thread work here is split into two
        // synchronous dispatches with the real async wait (window.ClipboardOperationForTests) awaited
        // directly in the test method's own async context, where FluentAssertions failures do surface.
        var clipboard = new FakeSystemClipboard { ThrowOnWrite = true };
        MainWindow? window = null;
        var clipboardOp = Task.CompletedTask;

        await Session.Dispatch(() =>
        {
            window = new MainWindow(
                [],
                loadRecentFilesStore: null,
                systemClipboard: clipboard,
                clipboardRenderer: new StubRenderer());
            var shape = window.Editor.InsertDefaultRectangle();
            shape.TextBody = BuildTextBody("Ribbon text");
            window.Editor.Select(shape.Id);
            var registry = window.BuildCommandRegistry();

            registry.TryGet("freep.copy", out var copy).Should().BeTrue();
            copy!.Execute(RibbonCommandContext.Empty);
            clipboardOp = window.ClipboardOperationForTests;
        }, CancellationToken.None);

        await clipboardOp;

        string statusText = "";
        await Session.Dispatch(() =>
        {
            statusText = window!.StatusTextForTests;
            window.Close();
        }, CancellationToken.None);

        statusText.Should().Contain("Copy");
        statusText.Should().Contain("clipboard locked");
    }

    [Fact]
    public async Task Ribbon_copy_success_does_not_touch_the_status_bar_with_an_error()
    {
        // Sibling no-regression: a successful copy must not report a failure. See the note on
        // Ribbon_copy_write_failure_reaches_the_status_bar for why assertions run outside the
        // Session.Dispatch delegate.
        var clipboard = new FakeSystemClipboard();
        MainWindow? window = null;
        var clipboardOp = Task.CompletedTask;

        await Session.Dispatch(() =>
        {
            window = new MainWindow(
                [],
                loadRecentFilesStore: null,
                systemClipboard: clipboard,
                clipboardRenderer: new StubRenderer());
            var shape = window.Editor.InsertDefaultRectangle();
            shape.TextBody = BuildTextBody("Ribbon text");
            window.Editor.Select(shape.Id);
            var registry = window.BuildCommandRegistry();

            registry.TryGet("freep.copy", out var copy).Should().BeTrue();
            copy!.Execute(RibbonCommandContext.Empty);
            clipboardOp = window.ClipboardOperationForTests;
        }, CancellationToken.None);

        await clipboardOp;

        string statusText = "";
        await Session.Dispatch(() =>
        {
            statusText = window!.StatusTextForTests;
            window.Close();
        }, CancellationToken.None);

        clipboard.WriteCount.Should().Be(1);
        statusText.Should().NotContain("Copy");
    }

    [Fact]
    public async Task Empty_or_unsupported_clipboard_falls_back_to_internal_then_nothing()
    {
        var clipboard = new FakeSystemClipboard();
        var editor = CreateEditorWithSelectedShape(out _);
        editor.CopySelectedShapes();
        editor.ClearSelection();
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.Internal);
        editor.CurrentSlide!.Shapes.Should().HaveCount(2);

        var emptyEditor = CreateEmptyEditor();
        (await service.PasteAsync(emptyEditor)).Should().Be(PresentationClipboardPasteSource.Nothing);
        emptyEditor.CurrentSlide!.Shapes.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_and_write_failures_degrade_without_losing_internal_cut_data()
    {
        var clipboard = new FakeSystemClipboard { ThrowOnWrite = true, ThrowOnRead = true };
        var editor = CreateEditorWithSelectedShape(out _);
        var service = new AvaloniaPresentationClipboardService(clipboard, new StubRenderer());

        (await service.CutAsync(editor)).Should().BeFalse();
        editor.CurrentSlide!.Shapes.Should().BeEmpty();
        editor.CanPaste.Should().BeTrue();

        (await service.PasteAsync(editor)).Should().Be(PresentationClipboardPasteSource.Internal);
        editor.CurrentSlide.Shapes.Should().ContainSingle();
    }

    [Fact]
    public async Task Ribbon_and_keyboard_share_the_interoperable_clipboard_path()
    {
        await Session.Dispatch(async () =>
        {
            var clipboard = new FakeSystemClipboard();
            var window = new MainWindow(
                [],
                loadRecentFilesStore: null,
                systemClipboard: clipboard,
                clipboardRenderer: new StubRenderer());
            try
            {
                var shape = window.Editor.InsertDefaultRectangle();
                shape.TextBody = BuildTextBody("Ribbon text");
                window.Editor.Select(shape.Id);
                var registry = window.BuildCommandRegistry();

                registry.TryGet("freep.copy", out var copy).Should().BeTrue();
                copy!.Execute(RibbonCommandContext.Empty);
                await window.ClipboardOperationForTests;
                clipboard.WriteCount.Should().Be(1);

                var beforePaste = window.Editor.CurrentSlide!.Shapes.Count;
                registry.TryGet("freep.paste", out var paste).Should().BeTrue();
                paste!.Execute(RibbonCommandContext.Empty);
                await window.ClipboardOperationForTests;
                window.Editor.CurrentSlide.Shapes.Should().HaveCount(beforePaste + 1);

                var keyboardShape = window.Editor.CurrentSlide.Shapes.Last();
                window.Editor.Select(keyboardShape.Id);
                var cut = new KeyEventArgs { Key = Key.X, KeyModifiers = KeyModifiers.Control };
                window.RaiseKeyDownForTests(cut);
                await window.ClipboardOperationForTests;
                cut.Handled.Should().BeTrue();
                clipboard.WriteCount.Should().Be(2);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Queued_clipboard_commands_use_invocation_selection_in_order()
    {
        await Session.Dispatch(async () =>
        {
            var firstWriteStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstWriteGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var clipboard = new FakeSystemClipboard
            {
                WriteStarted = firstWriteStarted,
                WriteGate = firstWriteGate,
            };
            var window = new MainWindow(
                [],
                loadRecentFilesStore: null,
                systemClipboard: clipboard,
                clipboardRenderer: new StubRenderer());
            try
            {
                var first = window.Editor.InsertDefaultRectangle();
                first.Name = "First";
                var middle = window.Editor.InsertDefaultRectangle();
                middle.Name = "Middle";
                var later = window.Editor.InsertDefaultRectangle();
                later.Name = "Later";

                window.Editor.Select(first.Id);
                RaiseClipboardKey(window, Key.C);
                await firstWriteStarted.Task;

                window.Editor.Select(middle.Id);
                RaiseClipboardKey(window, Key.X);
                window.Editor.Select(later.Id);
                RaiseClipboardKey(window, Key.V);

                firstWriteGate.TrySetResult(true);
                await window.ClipboardOperationForTests;

                window.Editor.CurrentSlide!.Shapes.Should().Contain(shape => shape.Id == first.Id);
                window.Editor.CurrentSlide.Shapes.Should().Contain(shape => shape.Id == later.Id);
                window.Editor.CurrentSlide.Shapes.Should().NotContain(shape => shape.Id == middle.Id);
                window.Editor.CurrentSlide.Shapes
                    .Count(shape => shape.Name == middle.Name)
                    .Should().Be(1);
                clipboard.WriteCount.Should().Be(2);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static KeyEventArgs RaiseClipboardKey(MainWindow window, Key key)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = KeyModifiers.Control };
        window.RaiseKeyDownForTests(args);
        args.Handled.Should().BeTrue();
        return args;
    }

    private static EditingSession CreateEditorWithSelectedShape(out SlideShape shape)
    {
        var editor = CreateEmptyEditor();
        shape = new SlideShape
        {
            Id = 17,
            Name = "Clipboard shape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 2_743_200,
            ExtentCyEmu = 1_828_800,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x336699))),
            TextBody = BuildTextBody("Clipboard text"),
        };
        shape.TextBody.Paragraphs[0].Runs[0].Bold = true;
        editor.CurrentSlide!.Shapes.Add(shape);
        editor.Select(shape.Id);
        return editor;
    }

    private static EditingSession CreateEmptyEditor()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static TextBody BuildTextBody(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static byte[] CreateXamlPackage(
        string xaml,
        params (string Name, byte[] Bytes)[] resources)
    {
        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var resource in resources)
            {
                var entry = package.CreateEntry(resource.Name);
                using var stream = entry.Open();
                stream.Write(resource.Bytes);
            }

            var xamlEntry = package.CreateEntry("Xaml/Document.xaml");
            using var writer = new StreamWriter(xamlEntry.Open(), Encoding.UTF8);
            writer.Write(xaml);
        }
        return output.ToArray();
    }

    private sealed class FakeSystemClipboard : IPresentationSystemClipboard
    {
        public PresentationClipboardContent Content { get; set; } = new();
        public PresentationClipboardContent? LastWritten { get; private set; }
        public int WriteCount { get; private set; }
        public bool ThrowOnWrite { get; set; }
        public bool ThrowOnRead { get; set; }
        public Action? BeforeWrite { get; set; }
        public TaskCompletionSource<bool>? WriteStarted { get; set; }
        public TaskCompletionSource<bool>? WriteGate { get; set; }

        public async ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent platformContent,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrite)
                return PlatformClipboardWriteResult.Failed("clipboard locked");
            BeforeWrite?.Invoke();
            WriteStarted?.TrySetResult(true);
            if (WriteGate is not null)
                await WriteGate.Task;
            var content = PresentationClipboardPlatformMapper.FromPlatformContent(platformContent);
            LastWritten = content;
            Content = content;
            WriteCount++;
            return PlatformClipboardWriteResult.Success();
        }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnRead)
                return ValueTask.FromResult(
                    PlatformClipboardReadResult<PlatformClipboardContent>.Failed(
                        "clipboard unavailable"));
            return ValueTask.FromResult(
                PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                    PresentationClipboardPlatformMapper.ToPlatformContent(
                        Content,
                        PresentationClipboardPlatformMapper.ResolveNativeScope(),
                        PresentationClipboardPlatformMapper.ResolveNativeXamlPackageFormat(),
                        PresentationClipboardPlatformMapper.ResolveNativeRtfFormat())));
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }

    private sealed class StubRenderer : IPresentationClipboardShapeRenderer
    {
        public byte[] RenderSelection(
            Presentation presentation,
            Slide slide,
            IReadOnlyList<SlideShape> shapes) => Png;
    }

    private sealed class ThrowingPlatformAliasTransfer : IAsyncDataTransfer, IAsyncDataTransferItem
    {
        public IReadOnlyList<DataFormat> Formats { get; } =
        [
            AvaloniaPresentationSystemClipboard.SelectionPlatformFormat,
            AvaloniaPresentationSystemClipboard.OwnerTokenPlatformFormat,
            AvaloniaPresentationSystemClipboard.SelectionFormat,
            AvaloniaPresentationSystemClipboard.OwnerTokenFormat,
        ];

        public IReadOnlyList<IAsyncDataTransferItem> Items => [this];

        public Task<object?> TryGetRawAsync(DataFormat format)
        {
            if (format == AvaloniaPresentationSystemClipboard.SelectionPlatformFormat
                || format == AvaloniaPresentationSystemClipboard.OwnerTokenPlatformFormat)
            {
                throw new InvalidOperationException("public alias unavailable");
            }

            if (format == AvaloniaPresentationSystemClipboard.SelectionFormat)
                return Task.FromResult<object?>(new byte[] { 6, 5, 4 });
            if (format == AvaloniaPresentationSystemClipboard.OwnerTokenFormat)
                return Task.FromResult<object?>("legacy-owner");
            return Task.FromResult<object?>(null);
        }

        public void Dispose()
        {
        }
    }

    private static byte[] ReadAvaloniaWin32HGlobal(IDataTransfer transfer, DataFormat format)
    {
        var helperType = Assembly.Load("Avalonia.Win32").GetType(
            "Avalonia.Win32.OleDataObjectHelper",
            throwOnError: true)!;
        var writeMethod = helperType.GetMethod(
            "WriteDataToHGlobal",
            BindingFlags.Public | BindingFlags.Static)!;
        object?[] arguments = [transfer, format, IntPtr.Zero];

        ((uint)writeMethod.Invoke(null, arguments)!).Should().Be(0);
        var memory = (IntPtr)arguments[2]!;
        memory.Should().NotBe(IntPtr.Zero);
        try
        {
            var size = checked((int)NativeClipboardMemory.GlobalSize(memory));
            var source = NativeClipboardMemory.GlobalLock(memory);
            source.Should().NotBe(IntPtr.Zero);
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(source, bytes, 0, size);
                return bytes;
            }
            finally
            {
                NativeClipboardMemory.GlobalUnlock(memory);
            }
        }
        finally
        {
            NativeClipboardMemory.GlobalFree(memory);
        }
    }

    private static class NativeClipboardMemory
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nuint GlobalSize(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalFree(IntPtr memory);
    }
}
