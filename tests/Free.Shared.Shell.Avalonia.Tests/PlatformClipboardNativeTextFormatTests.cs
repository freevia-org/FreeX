using Avalonia.Input;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class PlatformClipboardNativeTextFormatTests
{
    [Theory]
    [InlineData("text/html", "public.html", "<table><tr><td>café</td></tr></table>")]
    [InlineData("text/csv", "public.comma-separated-values-text", "\"a,b\",café")]
    [InlineData("text/rtf", "public.rtf", @"{\rtf1 example}")]
    [InlineData("TEXT/HTML", "public.html", "<b>mixed-case MIME</b>")]
    [InlineData("application/rtf", "public.rtf", @"{\rtf1 application MIME}")]
    public async Task Read_RecognizesExternalNativeTextUnderTheRequestedMimeName(string mime, string macType, string payload)
    {
        // This is the format the native backend exposes for an external app's pasteboard item.
        var nativeFormat = DataFormat.CreateStringPlatformFormat(OperatingSystem.IsMacOS() ? macType : mime);
        using var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(nativeFormat, payload));

        var result = await AvaloniaPlatformClipboard.ReadDataTransferAsync(transfer,
            new PlatformClipboardReadRequest(CustomFormats: [new(mime, PlatformClipboardDataKind.Text)]));

        result.Status.Should().Be(PlatformClipboardReadStatus.Success);
        result.Value!.GetText(mime).Should().Be(payload);
        result.Value.CustomData.Single().Format.Scope.Should().Be(PlatformClipboardFormatScope.Platform);
    }

    [Theory]
    [InlineData("text/html", "public.html")]
    [InlineData("text/csv", "public.comma-separated-values-text")]
    [InlineData("text/rtf", "public.rtf")]
    [InlineData("TEXT/HTML", "public.html")]
    [InlineData("application/rtf", "public.rtf")]
    public async Task Write_UsesNativeTextTypeAndKeepsApplicationFlavorsDistinct(string mime, string macType)
    {
        const string publicPayload = "formatted public text";
        const string privatePayload = "application-only text";
        const string applicationFormat = "FreeX.Clipboard.NativeTextPayload";
        var transfer = AvaloniaPlatformClipboard.BuildDataTransfer(new PlatformClipboardContent(CustomData:
        [
            PlatformClipboardData.FromText(mime, publicPayload),
            PlatformClipboardData.FromText(applicationFormat, privatePayload, PlatformClipboardFormatScope.Application)
        ]), out var bitmap);
        using (transfer)
        {
            bitmap.Should().BeNull();
            var asyncTransfer = (IAsyncDataTransfer)transfer;
            var nativeFormat = DataFormat.CreateStringPlatformFormat(OperatingSystem.IsMacOS() ? macType : mime);
            (await asyncTransfer.TryGetValueAsync(nativeFormat)).Should().Be(publicPayload);
            (await asyncTransfer.TryGetValueAsync(DataFormat.CreateStringApplicationFormat(applicationFormat)))
                .Should().Be(privatePayload);

            var result = await AvaloniaPlatformClipboard.ReadDataTransferAsync(transfer,
                new PlatformClipboardReadRequest(CustomFormats:
                [
                    new(mime, PlatformClipboardDataKind.Text),
                    new(applicationFormat, PlatformClipboardDataKind.Text, PlatformClipboardFormatScope.Application)
                ]));
            result.Value!.GetText(mime, PlatformClipboardFormatScope.Platform).Should().Be(publicPayload);
            result.Value.GetText(applicationFormat, PlatformClipboardFormatScope.Application).Should().Be(privatePayload);
        }
    }

    [Fact]
    public async Task HtmlFragmentAndWindowsCfHtml_RemainDistinctPayloads()
    {
        const string fragment = "<table><tr><td>café</td></tr></table>";
        const string cfHtml = "Version:1.0\r\nStartHTML:0000000100\r\n" + fragment;
        var transfer = AvaloniaPlatformClipboard.BuildDataTransfer(new PlatformClipboardContent(CustomData:
        [
            PlatformClipboardData.FromText("text/html", fragment),
            PlatformClipboardData.FromText("HTML Format", cfHtml)
        ]), out var bitmap);
        using (transfer)
        {
            bitmap.Should().BeNull();
            var asyncTransfer = (IAsyncDataTransfer)transfer;
            var nativeFormat = DataFormat.CreateStringPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "text/html");
            (await asyncTransfer.TryGetValueAsync(nativeFormat)).Should().Be(fragment);
            (await asyncTransfer.TryGetValueAsync(DataFormat.CreateStringPlatformFormat("HTML Format"))).Should().Be(cfHtml);
        }
    }
}
