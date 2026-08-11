using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class WatermarkDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void WatermarkDialog_UsesSharedPlannerForTextAndPicturePolicy()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "DesignDialogs.cs"));

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("WatermarkOptionsDialogPlanner.BuildInitialState(current, CultureInfo.CurrentCulture)");
        source.Should().Contain("WatermarkOptionsDialogPlanner.TryBuildTextResult(");
        source.Should().Contain("new WatermarkTextDialogInput(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.TryBuildPictureResult(");
        source.Should().Contain("new WatermarkPictureDialogInput(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.BuildImageImportPlan(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.FormatImageReadFailure(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.SelectWatermarkImageTitle");
        source.Should().NotContain("new WatermarkOptions(text)");
        source.Should().NotContain("$\"Could not read image file:");
    }

    [Fact]
    public async Task WatermarkDialog_uses_shared_default_and_cancel_action_semantics()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new WatermarkDialog(null);
            var buttons = dialog.GetLogicalDescendants().OfType<Button>()
                .Where(button => button.Content is string content &&
                    content is "OK" or "Remove Watermark" or "Cancel")
                .ToArray();

            buttons.Select(button => button.Content?.ToString()).Should().Equal(
                "OK", "Remove Watermark", "Cancel");
            buttons.Should().ContainSingle(button => button.IsDefault && button.Content as string == "OK");
            buttons.Should().ContainSingle(button => button.IsCancel && button.Content as string == "Cancel");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WatermarkDialog_AcceptsAndAppliesPictureWatermarkOptions()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };

        await Session.Dispatch(() =>
        {
            var dialog = new WatermarkDialog(null);
            dialog.SelectPictureWatermarkForTests(
                imageBytes,
                "logo.png",
                scaleText: "125",
                isHorizontal: true,
                isWashout: false);

            dialog.AcceptForTests().Should().BeTrue();

            var result = dialog.Result;
            result.Should().NotBeNull();
            result!.IsPicture.Should().BeTrue();
            result.ImageBytes.Should().BeSameAs(imageBytes);
            result.ScalePct.Should().Be(125);
            result.Layout.Should().Be(WatermarkLayout.Horizontal);
            result.Opacity.Should().Be(1.0);

            var view = new DocumentView();
            view.LoadDocument(MakeDoc());
            view.SetWatermark(result);

            var applied = view.Document.Page.WatermarkOptions;
            applied.Should().NotBeNull();
            applied!.ImageBytes.Should().BeEquivalentTo(imageBytes);
            applied.ScalePct.Should().Be(125);
            applied.Layout.Should().Be(WatermarkLayout.Horizontal);
            applied.Opacity.Should().Be(1.0);
        }, CancellationToken.None);
    }

    private static TextDocument MakeDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Picture watermark"));
        return doc;
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
