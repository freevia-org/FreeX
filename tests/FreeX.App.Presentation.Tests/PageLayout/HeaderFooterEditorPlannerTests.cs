using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class HeaderFooterEditorPlannerTests
{
    [Fact]
    public void InsertToken_InsertsAtBoundedCaret()
    {
        HeaderFooterEditorPlanner.InsertToken("Page  of", caretIndex: 5, "&[Page]")
            .Should()
            .Be("Page &[Page] of");

        HeaderFooterEditorPlanner.InsertToken("End", caretIndex: 99, "!")
            .Should()
            .Be("End!");

        HeaderFooterEditorPlanner.InsertToken(null, caretIndex: -3, "&[Date]")
            .Should()
            .Be("&[Date]");
    }

    [Fact]
    public void ApplyCenterPreset_ReplacesOnlyCenterSection()
    {
        HeaderFooterEditorPlanner.ApplyCenterPreset(
                new WorksheetHeaderFooter("L", "old", "R"),
                "Page &[Page]")
            .Should()
            .Be(new WorksheetHeaderFooter("L", "Page &[Page]", "R"));
    }

    [Theory]
    [InlineData("Logo &[Picture]")]
    [InlineData("Logo &G")]
    [InlineData("Logo &g")]
    public void ContainsPictureToken_DetectsBracketedAndLegacyTokens(string text)
    {
        HeaderFooterEditorPlanner.ContainsPictureToken(text).Should().BeTrue();
    }

    [Fact]
    public void PrunePicturesWithoutTokens_DropsOnlyPicturesWhoseSectionTokenWasDeleted()
    {
        var left = Picture("left.png");
        var center = Picture("center.png");
        var right = Picture("right.png");
        var text = new WorksheetHeaderFooter("Logo &[Picture]", "Legacy &G", "No picture");
        var pictures = new WorksheetHeaderFooterPictureSet(left, center, right);

        var pruned = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(text, pictures);

        pruned.Left.Should().BeSameAs(left);
        pruned.Center.Should().BeSameAs(center);
        pruned.Right.Should().BeNull();
    }

    [Fact]
    public void GetAndSetPicture_EditOnlyRequestedSection()
    {
        var existing = Picture("existing.png");
        var replacement = Picture("replacement.png");
        var pictures = new WorksheetHeaderFooterPictureSet(existing, null, null);

        var updated = HeaderFooterEditorPlanner.SetPicture(
            pictures,
            HeaderFooterEditorSection.Center,
            replacement);

        HeaderFooterEditorPlanner.GetPicture(updated, HeaderFooterEditorSection.Left).Should().BeSameAs(existing);
        HeaderFooterEditorPlanner.GetPicture(updated, HeaderFooterEditorSection.Center).Should().BeSameAs(replacement);
        HeaderFooterEditorPlanner.GetPicture(updated, HeaderFooterEditorSection.Right).Should().BeNull();
    }

    [Fact]
    public void LabelKeys_MapTargetsToRendererLocalResources()
    {
        HeaderFooterEditorPlanner.ScopeLabelResourceKey(HeaderFooterEditorScope.FirstPageFooter)
            .Should()
            .Be("HeaderFooter_FirstPageFooter");
        HeaderFooterEditorPlanner.SectionLabelResourceKey(HeaderFooterEditorSection.Right)
            .Should()
            .Be("HeaderFooterPicture_RightSection");
    }

    [Fact]
    public void TargetEnablement_RequiresFirstAndEvenFlagsForOptionalScopes()
    {
        HeaderFooterEditorPlanner.IsScopeEnabled(
                HeaderFooterEditorScope.Header,
                differentFirstPage: false,
                differentOddEvenPages: false)
            .Should()
            .BeTrue();

        HeaderFooterEditorPlanner.IsScopeEnabled(
                HeaderFooterEditorScope.FirstPageHeader,
                differentFirstPage: false,
                differentOddEvenPages: true)
            .Should()
            .BeFalse();

        HeaderFooterEditorPlanner.IsTargetEnabled(
                new HeaderFooterEditorTarget(HeaderFooterEditorScope.EvenPageFooter, HeaderFooterEditorSection.Right),
                differentFirstPage: true,
                differentOddEvenPages: true)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void CoerceToEnabledTarget_FallsBackToMatchingStandardHeaderOrFooter()
    {
        HeaderFooterEditorPlanner.CoerceToEnabledTarget(
                new HeaderFooterEditorTarget(HeaderFooterEditorScope.FirstPageHeader, HeaderFooterEditorSection.Left),
                differentFirstPage: false,
                differentOddEvenPages: true)
            .Should()
            .Be(new HeaderFooterEditorTarget(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Center));

        HeaderFooterEditorPlanner.CoerceToEnabledTarget(
                new HeaderFooterEditorTarget(HeaderFooterEditorScope.EvenPageFooter, HeaderFooterEditorSection.Right),
                differentFirstPage: true,
                differentOddEvenPages: false)
            .Should()
            .Be(new HeaderFooterEditorTarget(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Center));
    }

    [Fact]
    public void EditorState_RoundTripsSheetAndPageSetupFields()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            PageHeader = new WorksheetHeaderFooter("L", "C", "R"),
            PageFooter = new WorksheetHeaderFooter("FL", "FC", "FR"),
            PageHeaderPictures = new WorksheetHeaderFooterPictureSet(Picture("left.png"), null, null),
            DifferentFirstPageHeaderFooter = true,
            HeaderFooterScaleWithDocument = false,
        };

        var state = HeaderFooterEditorState.FromSheet(sheet);
        var fields = state.ApplyTo(PageSetupDialogModel.FromSheet(new Sheet(SheetId.New(), "Target")));
        var request = state.DeepClone();

        state.HeaderPictures.Should().NotBeSameAs(sheet.PageHeaderPictures);
        fields.HeaderFooter.Header.Should().Be(sheet.PageHeader);
        fields.HeaderFooter.Footer.Should().Be(sheet.PageFooter);
        fields.HeaderFooter.DifferentFirstPage.Should().BeTrue();
        fields.HeaderFooter.ScaleWithDocument.Should().BeFalse();
        request.Header.Should().Be(sheet.PageHeader);
        request.HeaderPictures.Should().NotBeSameAs(state.HeaderPictures);
    }

    [Fact]
    public void EditorState_PrunesAllPictureSetsThroughOnePortableOperation()
    {
        var picture = Picture("logo.png");
        var state = new HeaderFooterEditorState(
            new WorksheetHeaderFooter("&[Picture]", "", ""),
            new WorksheetHeaderFooter("", "No token", ""),
            new WorksheetHeaderFooter("", "", ""),
            new WorksheetHeaderFooter("", "", ""),
            new WorksheetHeaderFooter("", "", ""),
            new WorksheetHeaderFooter("", "", ""),
            new WorksheetHeaderFooterPictureSet(picture, null, null),
            new WorksheetHeaderFooterPictureSet(null, picture, null),
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            WorksheetHeaderFooterPictureSet.Empty,
            false,
            false,
            true,
            true);

        var pruned = state.PrunePicturesWithoutTokens();

        pruned.HeaderPictures.Left.Should().BeSameAs(picture);
        pruned.FooterPictures.Center.Should().BeNull();
    }

    [Fact]
    public void R127_ComposeTargetLabel_JoinsThroughTheResourceFormatDelegateInsteadOfHardcodingOrder()
    {
        var calls = new List<(string Key, object?[] Args)>();
        string FormatResource(string key, object?[] args)
        {
            calls.Add((key, args));
            return $"{args[1]} <- {args[0]}";
        }

        var label = HeaderFooterEditorPlanner.ComposeTargetLabel("Header", "left section", FormatResource);

        label.Should().Be("left section <- Header");
        calls.Should().ContainSingle();
        calls[0].Key.Should().Be(HeaderFooterEditorPlanner.TargetLabelFormatResourceKey);
        calls[0].Args.Should().Equal("Header", "left section");
    }

    [Fact]
    public void R127_ComposeTargetLabel_FallsBackToSectionOnlyWhenScopeIsBlank()
    {
        var formatterInvoked = false;
        string FormatResource(string key, object?[] args)
        {
            formatterInvoked = true;
            return "should not be used";
        }

        HeaderFooterEditorPlanner.ComposeTargetLabel(string.Empty, "current section", FormatResource)
            .Should()
            .Be("current section");
        HeaderFooterEditorPlanner.ComposeTargetLabel("   ", "current section", FormatResource)
            .Should()
            .Be("current section");

        formatterInvoked.Should().BeFalse();
    }

    [Fact]
    public void EditorState_GetAndWithOperationsOwnScopeMutation()
    {
        var state = HeaderFooterEditorState.FromSheet(new Sheet(SheetId.New(), "Sheet1"));
        var footer = new WorksheetHeaderFooter("L", "C", "R");
        var pictures = new WorksheetHeaderFooterPictureSet(null, Picture("center.png"), null);

        var updated = state
            .WithValue(HeaderFooterEditorScope.FirstPageFooter, footer)
            .WithPictures(HeaderFooterEditorScope.FirstPageFooter, pictures);

        updated.GetValue(HeaderFooterEditorScope.FirstPageFooter).Should().Be(footer);
        updated.GetPictures(HeaderFooterEditorScope.FirstPageFooter).Should().Be(pictures);
        updated.Header.Should().Be(state.Header);
        updated.HeaderPictures.Should().Be(state.HeaderPictures);
    }

    [Fact]
    public void BuildResult_ReadsEveryScopeAndPrunesPicturesAfterTextChanges()
    {
        var picture = Picture("logo.png");
        var state = HeaderFooterEditorState.FromSheet(new Sheet(SheetId.New(), "Sheet1"))
            .WithPictures(
                HeaderFooterEditorScope.EvenPageFooter,
                new WorksheetHeaderFooterPictureSet(null, picture, null));
        var visited = new List<HeaderFooterEditorScope>();

        var result = HeaderFooterEditorPlanner.BuildResult(
            state,
            scope =>
            {
                visited.Add(scope);
                return scope == HeaderFooterEditorScope.EvenPageFooter
                    ? new WorksheetHeaderFooter("", "No token", "")
                    : new WorksheetHeaderFooter(scope.ToString(), "", "");
            },
            differentFirstPage: true,
            differentOddEvenPages: true,
            scaleWithDocument: false,
            alignWithMargins: false);

        visited.Should().Equal(
            HeaderFooterEditorScope.Header,
            HeaderFooterEditorScope.Footer,
            HeaderFooterEditorScope.FirstPageHeader,
            HeaderFooterEditorScope.FirstPageFooter,
            HeaderFooterEditorScope.EvenPageHeader,
            HeaderFooterEditorScope.EvenPageFooter);
        result.Footer.Left.Should().Be(nameof(HeaderFooterEditorScope.Footer));
        result.EvenPageFooterPictures.Center.Should().BeNull();
        result.DifferentFirstPage.Should().BeTrue();
        result.ScaleWithDocument.Should().BeFalse();
    }

    [Fact]
    public void CoerceToEnabledTargetForTab_ChangesFamiliesBeforeOptionalScopeFallback()
    {
        HeaderFooterEditorPlanner.CoerceToEnabledTargetForTab(
                new HeaderFooterEditorTarget(HeaderFooterEditorScope.EvenPageHeader, HeaderFooterEditorSection.Right),
                HeaderFooterEditorScope.Footer,
                differentFirstPage: true,
                differentOddEvenPages: true)
            .Should()
            .Be(new HeaderFooterEditorTarget(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Center));

        HeaderFooterEditorPlanner.CoerceToEnabledTargetForTab(
                new HeaderFooterEditorTarget(HeaderFooterEditorScope.FirstPageFooter, HeaderFooterEditorSection.Left),
                HeaderFooterEditorScope.Footer,
                differentFirstPage: false,
                differentOddEvenPages: true)
            .Should()
            .Be(new HeaderFooterEditorTarget(HeaderFooterEditorScope.Footer, HeaderFooterEditorSection.Center));
    }

    [Theory]
    [InlineData(HeaderFooterEditorScope.Header, HeaderFooterEditorSection.Left, "HeaderFooter_HeaderLeft")]
    [InlineData(HeaderFooterEditorScope.FirstPageFooter, HeaderFooterEditorSection.Center, "HeaderFooter_FirstFooterCenter")]
    [InlineData(HeaderFooterEditorScope.EvenPageHeader, HeaderFooterEditorSection.Right, "HeaderFooter_EvenHeaderRight")]
    public void EditorFieldLabelResourceKey_MapsPortableScopeAndSectionPolicy(
        HeaderFooterEditorScope scope,
        HeaderFooterEditorSection section,
        string expected)
    {
        HeaderFooterEditorPlanner.EditorFieldLabelResourceKey(new HeaderFooterEditorTarget(scope, section))
            .Should()
            .Be(expected);
    }

    private static WorksheetHeaderFooterPicture Picture(string fileName) =>
        new([1, 2, 3], "image/png", fileName, 120, 48);
}
