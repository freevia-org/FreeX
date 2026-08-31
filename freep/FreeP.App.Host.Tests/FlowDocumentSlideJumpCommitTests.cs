using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// WPF's own Ctrl+V runs whenever the clipboard adapter declines the key -- a failed clipboard
/// read leaves the event unhandled -- and it pastes the XamlPackage straight into the document,
/// bypassing the clipboard planner entirely. That package carries internal slide jumps as
/// <c>freep-slide:</c> NavigateUris, so the commit that reads the document back into the model
/// is the last place to notice a target belonging to whichever deck it was copied from.
/// </summary>
public sealed class FlowDocumentSlideJumpCommitTests
{
    private const string ForeignSlideId = "foreign-slide-id";
    private const string LocalSlideId = "local-slide-id";

    [StaFact]
    public void FromFlowDocument_OrphansASlideJumpTheDestinationDeckCannotResolve()
    {
        var doc = DocumentWithSlideJump(ForeignSlideId);

        var body = TextBodyFlowDocumentConverter.FromFlowDocument(
            doc,
            originalBody: null,
            destinationSlideIds: new[] { LocalSlideId });

        var hyperlink = SoleHyperlink(body);
        hyperlink.TargetSlideId.Should().BeNull();
        hyperlink.Tooltip.Should().Be("jump");
    }

    [StaFact]
    public void FromFlowDocument_KeepsASlideJumpTheDestinationDeckHas()
    {
        var doc = DocumentWithSlideJump(LocalSlideId);

        var body = TextBodyFlowDocumentConverter.FromFlowDocument(
            doc,
            originalBody: null,
            destinationSlideIds: new[] { LocalSlideId });

        SoleHyperlink(body).TargetSlideId.Should().Be(LocalSlideId);
    }

    [StaFact]
    public void FromFlowDocument_WithoutDestinationSlideIdsKeepsTheReconstructedTarget()
    {
        var doc = DocumentWithSlideJump(ForeignSlideId);

        var body = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        SoleHyperlink(body).TargetSlideId.Should().Be(ForeignSlideId);
    }

    [StaFact]
    public void FromFlowDocument_LeavesAnExternalUrlAlone()
    {
        var source = BodyWithHyperlink(new Hyperlink { Url = "https://example.test" });
        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(source, 18);

        var body = TextBodyFlowDocumentConverter.FromFlowDocument(
            doc,
            originalBody: null,
            destinationSlideIds: Array.Empty<string>());

        SoleHyperlink(body).Url.Should().Be("https://example.test");
    }

    /// <summary>
    /// Builds the document the same way a native WPF paste would leave it: through
    /// <see cref="TextBodyFlowDocumentConverter.ToFlowDocument"/>, which is what produced the
    /// XamlPackage published on the clipboard in the first place.
    /// </summary>
    private static System.Windows.Documents.FlowDocument DocumentWithSlideJump(
        string targetSlideId) =>
        TextBodyFlowDocumentConverter.ToFlowDocument(
            BodyWithHyperlink(new Hyperlink { TargetSlideId = targetSlideId, Tooltip = "jump" }),
            18);

    private static TextBody BodyWithHyperlink(Hyperlink hyperlink) => new()
    {
        Paragraphs =
        {
            new Paragraph { Runs = { new Run { Text = "Jump", Hyperlink = hyperlink } } },
        },
    };

    private static Hyperlink SoleHyperlink(TextBody body) =>
        body.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Hyperlink)
            .Where(link => link is not null)
            .Should().ContainSingle().Subject!;
}
