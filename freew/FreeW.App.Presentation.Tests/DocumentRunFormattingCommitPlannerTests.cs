using System.IO;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentRunFormattingCommitPlannerTests
{
    [Fact]
    public void UnchangedRenderedInheritance_PreservesDirectNullsAndFlags()
    {
        var direct = RunFormatting.Default;
        var rendered = direct with
        {
            Bold = true,
            FontFamily = "Aptos",
            FontSizePt = 12,
            ColorHex = "#112233"
        };

        DocumentRunFormattingCommitPlanner.Resolve(direct, rendered, rendered, isVisuallyHidden: false)
            .Should().Be(direct);
    }

    [Fact]
    public void NativeChanges_BecomeDirectFormattingWithoutFlatteningUnchangedFields()
    {
        var direct = RunFormatting.Default;
        var rendered = direct with
        {
            FontFamily = "Aptos",
            FontSizePt = 12,
            ColorHex = "#112233"
        };
        var observed = rendered with { FontFamily = "Georgia", Bold = true };

        var committed = DocumentRunFormattingCommitPlanner.Resolve(
            direct,
            rendered,
            observed,
            isVisuallyHidden: false);

        committed.FontFamily.Should().Be("Georgia");
        committed.Bold.Should().BeTrue();
        committed.FontSizePt.Should().BeNull();
        committed.ColorHex.Should().BeNull();
    }

    [Fact]
    public void RemovingLocalTypography_ClearsPreviouslyDirectValues()
    {
        var direct = RunFormatting.Default with
        {
            FontFamily = "Georgia",
            FontSizePt = 14,
            ColorHex = "#445566"
        };

        var committed = DocumentRunFormattingCommitPlanner.Resolve(
            direct,
            direct,
            direct with { FontFamily = null, FontSizePt = null, ColorHex = null },
            isVisuallyHidden: false);

        committed.FontFamily.Should().BeNull();
        committed.FontSizePt.Should().BeNull();
        committed.ColorHex.Should().BeNull();
    }

    [Fact]
    public void RenderOnlySuperscriptSize_PreservesInheritedDirectSize()
    {
        var direct = RunFormatting.Default with { VerticalAlign = VerticalAlign.Superscript };
        var rendered = direct with { FontSizePt = 11 };

        DocumentRunFormattingCommitPlanner.Resolve(direct, rendered, rendered, isVisuallyHidden: false)
            .FontSizePt.Should().BeNull();
    }

    [Fact]
    public void HiddenPresentationChrome_PreservesAllDirectFormatting()
    {
        var direct = RunFormatting.Default with
        {
            FontFamily = "Georgia",
            FontSizePt = 14,
            ColorHex = "#445566",
            Hidden = true
        };

        DocumentRunFormattingCommitPlanner.Resolve(
                direct,
                direct,
                RunFormatting.Default,
                isVisuallyHidden: true)
            .Should().Be(direct);
    }

    [Fact]
    public void WpfCommitAdapter_KeepsDirectAndRenderedSnapshotsAndUsesSharedPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Editing",
            "DocumentView.cs"));

        source.Should().Contain("new CharacterFormatMarker(run.Formatting, fmt)")
            .And.Contain("run.ReadLocalValue(TextElement.FontFamilyProperty)")
            .And.Contain("run.ReadLocalValue(TextElement.FontSizeProperty)")
            .And.Contain("run.ReadLocalValue(TextElement.ForegroundProperty)")
            .And.Contain("DocumentRunFormattingCommitPlanner.Resolve(")
            .And.Contain("normalizedColor is null ? null! : new SolidColorBrush(color)")
            .And.NotContain("normalizedColor is null ? Brushes.Black : new SolidColorBrush(color)")
            .And.NotContain("FontFamily = run.FontFamily.Source");
    }
}
