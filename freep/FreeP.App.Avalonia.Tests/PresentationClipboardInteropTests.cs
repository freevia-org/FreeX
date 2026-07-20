using Avalonia.Headless;
using Avalonia.Input;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Free.Shared.Drawing;
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

        foreach (var format in new[]
                 {
                     AvaloniaPresentationSystemClipboard.SelectionPlatformFormat,
                     AvaloniaPresentationSystemClipboard.SelectionFormat,
                 })
        {
            ReadAvaloniaWin32HGlobal(transfer, format)
                .Should().Equal(content.SelectionBytes!);
        }

        var ownerBytes = Encoding.Unicode.GetBytes("avalonia-owner\0");
        foreach (var format in new[]
                 {
                     AvaloniaPresentationSystemClipboard.OwnerTokenPlatformFormat,
                     AvaloniaPresentationSystemClipboard.OwnerTokenFormat,
                 })
        {
            ReadAvaloniaWin32HGlobal(transfer, format).Should().Equal(ownerBytes);
        }
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
    public async Task Avalonia_data_transfer_maps_custom_bitmap_and_text_formats()
    {
        await Session.Dispatch(async () =>
        {
            var content = new PresentationClipboardContent(
                SelectionBytes: [4, 5, 6],
                PngBytes: Png,
                Text: "portable text",
                OwnerToken: "owner-token");
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
            item.SetText("WPF fallback");
            using var transfer = new DataTransfer();
            transfer.Add(item);

            var content = await AvaloniaPresentationSystemClipboard.ReadDataTransferAsync(transfer);

            content.SelectionBytes.Should().Equal(9, 8, 7);
            content.OwnerToken.Should().Be("wpf-owner");
            content.Text.Should().Be("WPF fallback");
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

    private sealed class FakeSystemClipboard : IPresentationSystemClipboard
    {
        public PresentationClipboardContent Content { get; set; } = new();
        public PresentationClipboardContent? LastWritten { get; private set; }
        public int WriteCount { get; private set; }
        public bool ThrowOnWrite { get; set; }
        public bool ThrowOnRead { get; set; }
        public Action? BeforeWrite { get; set; }

        public Task WriteAsync(PresentationClipboardContent content)
        {
            if (ThrowOnWrite)
                throw new InvalidOperationException("clipboard locked");
            BeforeWrite?.Invoke();
            LastWritten = content;
            Content = content;
            WriteCount++;
            return Task.CompletedTask;
        }

        public Task<PresentationClipboardContent> ReadAsync()
        {
            if (ThrowOnRead)
                throw new InvalidOperationException("clipboard unavailable");
            return Task.FromResult(Content);
        }
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
