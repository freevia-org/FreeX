using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonFormattingSessionTests
{
    [Fact]
    public void ParagraphValuesShareInvariantParsingFormattingAndDispatch()
    {
        var paragraph = ParagraphFormatting.Default with
        {
            IndentLeftPt = 12.5,
            IndentRightPt = 8,
            SpaceBeforePt = 3,
            SpaceAfterPt = 6,
        };
        var applied = new List<(FreeWParagraphValueKind Kind, double Value)>();
        var session = CreateSession(
            TextDocument.CreateEmpty(),
            () => paragraph,
            (kind, value) => applied.Add((kind, value)));

        session.CurrentParagraphValue(FreeWParagraphValueKind.IndentLeft).Should().Be("12.5");
        session.ApplyParagraphValue(FreeWParagraphValueKind.IndentRight, "24.25").Should().BeTrue();
        session.ApplyParagraphValue(FreeWParagraphValueKind.SpaceBefore, "-1").Should().BeFalse();
        session.ApplyParagraphValue(FreeWParagraphValueKind.SpaceAfter, "not-a-number").Should().BeFalse();

        applied.Should().Equal((FreeWParagraphValueKind.IndentRight, 24.25));
    }

    [Fact]
    public void ParagraphStyleResolutionIsSharedAcrossIdsDisplayNamesAndCompactedNames()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["CustomHeading"] = new DocumentStyle
        {
            Id = "CustomHeading",
            Name = "Custom Heading",
            Type = StyleType.Paragraph,
        };
        string? appliedStyle = null;
        var session = CreateSession(document, applyStyle: value => appliedStyle = value);

        session.ApplyParagraphStyle("custom heading").Should().BeTrue();
        appliedStyle.Should().Be("CustomHeading");
        FreeWRibbonFormattingSession.ResolveParagraphStyleId(document, "CustomHeading")
            .Should().Be("CustomHeading");
        FreeWRibbonFormattingSession.ResolveParagraphStyleName(document, "CustomHeading")
            .Should().Be("Custom Heading");
        session.ApplyParagraphStyle("missing").Should().BeFalse();
    }

    [Fact]
    public void ThemeAndStyleSetChoicesResolveThroughCanonicalModelCatalogs()
    {
        var document = TextDocument.CreateEmpty();
        DocumentTheme? appliedTheme = null;
        DocumentStyleSet? appliedStyleSet = null;
        var session = CreateSession(
            document,
            applyTheme: value => appliedTheme = value,
            applyStyleSet: value => appliedStyleSet = value);
        var theme = DocumentTheme.Catalog.Last();
        var styleSet = DocumentStyleSet.Catalog.Last();

        session.ApplyTheme(theme.Name).Should().BeTrue();
        session.ApplyStyleSet(styleSet.Name).Should().BeTrue();
        session.ApplyTheme("missing").Should().BeFalse();
        session.ApplyStyleSet(null).Should().BeFalse();

        appliedTheme.Should().BeSameAs(theme);
        appliedStyleSet.Should().BeSameAs(styleSet);
        session.CurrentThemeName().Should().Be(document.Theme.Name);
    }

    [Fact]
    public void PortableCommandsOwnParagraphValueAndStyleRibbonAdapters()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["CustomHeading"] = new DocumentStyle
        {
            Id = "CustomHeading",
            Name = "Custom Heading",
            Type = StyleType.Paragraph,
        };
        var paragraph = ParagraphFormatting.Default with { IndentLeftPt = 12.5 };
        var appliedValues = new List<(FreeWParagraphValueKind Kind, double Value)>();
        string? appliedStyle = null;
        var session = CreateSession(
            document,
            () => paragraph,
            (kind, value) => appliedValues.Add((kind, value)),
            value => appliedStyle = value,
            getCurrentStyleId: () => "CustomHeading");
        var valueCommand = new FreeWRibbonParagraphValueCommand(
            session,
            FreeWParagraphValueKind.IndentLeft);
        var styleCommand = new FreeWRibbonParagraphStyleCommand(session);

        valueCommand.GetState().Value.Should().Be("12.5");
        valueCommand.Execute(RibbonCommandContext.ForSelectedValue("24"));
        valueCommand.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "36" }));
        appliedValues.Should().Equal(
            (FreeWParagraphValueKind.IndentLeft, 24),
            (FreeWParagraphValueKind.IndentLeft, 36));

        styleCommand.GetState().Value.Should().Be("Custom Heading");
        styleCommand.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "Custom Heading" }));
        appliedStyle.Should().Be("CustomHeading");
    }

    private static FreeWRibbonFormattingSession CreateSession(
        TextDocument document,
        Func<ParagraphFormatting>? getParagraph = null,
        Action<FreeWParagraphValueKind, double>? applyParagraphValue = null,
        Action<string>? applyStyle = null,
        Action<DocumentTheme>? applyTheme = null,
        Action<DocumentStyleSet>? applyStyleSet = null,
        Func<string?>? getCurrentStyleId = null) =>
        new(new FreeWRibbonFormattingPorts(
            getParagraph ?? (() => ParagraphFormatting.Default),
            value => applyParagraphValue?.Invoke(FreeWParagraphValueKind.IndentLeft, value),
            value => applyParagraphValue?.Invoke(FreeWParagraphValueKind.IndentRight, value),
            value => applyParagraphValue?.Invoke(FreeWParagraphValueKind.SpaceBefore, value),
            value => applyParagraphValue?.Invoke(FreeWParagraphValueKind.SpaceAfter, value),
            () => document,
            getCurrentStyleId ?? (() => null),
            applyStyle ?? (_ => { }),
            applyTheme ?? (_ => { }),
            applyStyleSet ?? (_ => { })));
}
